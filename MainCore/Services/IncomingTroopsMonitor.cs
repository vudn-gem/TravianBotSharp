namespace MainCore.Services
{
    [RegisterSingleton<IncomingTroopsMonitor>]
    public sealed class IncomingTroopsMonitor(ReportNotificationOutbox outbox, TimeProvider timeProvider)
    {
        private readonly Lock _sync = new();
        private readonly Dictionary<AccountId, Session> _sessions = [];
        private readonly Dictionary<AccountId, long> _lastScans = [];

        private sealed class Session
        {
            public Guid Id { get; } = Guid.NewGuid();
            public Dictionary<int, (int Attacks, int Raids)> Villages { get; } = [];
        }

        public void Reset(AccountId accountId)
        {
            lock (_sync) _sessions.Remove(accountId);
        }

        public Guid? TryBeginScan(AccountId accountId)
        {
            lock (_sync)
            {
                var now = timeProvider.GetTimestamp();
                if (_lastScans.TryGetValue(accountId, out var last) &&
                    timeProvider.GetElapsedTime(last, now) < TimeSpan.FromMinutes(1)) return null;
                _lastScans[accountId] = now;
                if (!_sessions.TryGetValue(accountId, out var session))
                {
                    session = new Session();
                    _sessions.Add(accountId, session);
                }
                return session.Id;
            }
        }

        public DateTime GetFallbackExecuteAt(AccountId accountId)
        {
            lock (_sync)
            {
                var remaining = TimeSpan.FromMinutes(10);
                if (_lastScans.TryGetValue(accountId, out var last))
                {
                    remaining -= timeProvider.GetElapsedTime(last, timeProvider.GetTimestamp());
                }
                return timeProvider.GetLocalNow().DateTime.Add(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }
        }

        public void Observe(AccountId accountId, Guid sessionId, List<IncomingVillage> villages, string accountName)
        {
            lock (_sync)
            {
                if (!_sessions.TryGetValue(accountId, out var session) || session.Id != sessionId) return;
                foreach (var village in villages)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(village.Attacks);
                    ArgumentOutOfRangeException.ThrowIfNegative(village.Raids);
                    var previous = session.Villages.GetValueOrDefault(village.Id);
                    if (village.Attacks > previous.Attacks || village.Raids > previous.Raids)
                    {
                        outbox.Enqueue(accountId,
                            $"[TBS] Incoming attack/raid\nAccount: {Limit(accountName, 200)}\nVillage: {Limit(village.Name, 200)} (ID {village.Id})\nCoordinates: {Limit(village.Coordinates, 60)}\nIncoming attacks: {previous.Attacks} -> {village.Attacks}\nIncoming raids: {previous.Raids} -> {village.Raids}\nDetected: {timeProvider.GetLocalNow():yyyy-MM-dd HH:mm:ss zzz}");
                    }
                    session.Villages[village.Id] = (village.Attacks, village.Raids);
                }
                // Missing rows (e.g. collapsed groups) do not mean their incoming has cleared.
            }
        }

        private static string Limit(string text, int length) =>
            text.Length <= length ? text : text[..length] + "...";
    }
}
