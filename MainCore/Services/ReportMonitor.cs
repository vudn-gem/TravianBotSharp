namespace MainCore.Services
{
    [RegisterSingleton<ReportMonitor>]
    public sealed class ReportMonitor(ReportNotificationOutbox outbox, ILogger logger)
    {
        private readonly Lock _sync = new();
        private readonly Dictionary<AccountId, Session> _sessions = [];

        private sealed class Session
        {
            public Guid Id { get; } = Guid.NewGuid();
            public int? Count { get; set; }
        }

        public void Reset(AccountId accountId)
        {
            lock (_sync)
            {
                _sessions.Remove(accountId);
            }
        }

        public Guid GetSession(AccountId accountId)
        {
            lock (_sync)
            {
                if (!_sessions.TryGetValue(accountId, out var session))
                {
                    session = new Session();
                    _sessions.Add(accountId, session);
                }
                return session.Id;
            }
        }

        public bool Observe(AccountId accountId, Guid sessionId, int count, string accountName, bool notify = true)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            lock (_sync)
            {
                if (!_sessions.TryGetValue(accountId, out var session) || session.Id != sessionId) return false;

                var previous = session.Count;
                if (notify && previous.HasValue && count > previous.Value)
                {
                    var message = $"[TBS] Report badge increased\nAccount: {accountName}\nUnread: {previous.Value} -> {count}\nDetected: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}";
                    // Persist before advancing the in-memory baseline; failed writes can be retried.
                    outbox.Enqueue(accountId, message);
                }

                session.Count = count;
                logger.ForContext("AccountId", accountId).Information(
                    "Report badge scanned: {PreviousCount} -> {CurrentCount}; baseline: {IsBaseline}",
                    previous, count, !previous.HasValue);
                return previous.HasValue && count != previous.Value;
            }
        }
    }
}
