namespace MainCore.Services
{
    public sealed class ReportNotification
    {
        public long Id { get; set; }
        public int AccountId { get; set; }
        public string Text { get; set; } = "";
        public int Attempts { get; set; }
        public long NextAttemptUtcTicks { get; set; }
        public bool Failed { get; set; }
    }

    public sealed class ReportNotificationOutbox(string filePath)
    {
        private readonly Lock _sync = new();
        private bool _initialized;

        private sealed class OutboxContext(DbContextOptions<OutboxContext> options) : DbContext(options)
        {
            public DbSet<ReportNotification> Notifications => Set<ReportNotification>();
        }

        private OutboxContext Open()
        {
            var options = new DbContextOptionsBuilder<OutboxContext>()
                .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = filePath, Pooling = false }.ToString())
                .Options;
            if (!_initialized)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                using var initializer = new OutboxContext(options);
                initializer.Database.EnsureCreated();
                _initialized = true;
            }
            return new OutboxContext(options);
        }

        public void Enqueue(AccountId accountId, string text)
        {
            lock (_sync)
            {
                using var context = Open();
                context.Notifications.Add(new ReportNotification
                {
                    AccountId = accountId.Value,
                    Text = text,
                    NextAttemptUtcTicks = DateTime.UtcNow.Ticks,
                });
                context.SaveChanges();
            }
        }

        public ReportNotification? GetNext(DateTime utcNow)
        {
            lock (_sync)
            {
                using var context = Open();
                return context.Notifications.AsNoTracking()
                    .Where(x => !x.Failed && x.NextAttemptUtcTicks <= utcNow.Ticks)
                    .OrderBy(x => x.Id)
                    .FirstOrDefault();
            }
        }

        public void Complete(long id)
        {
            lock (_sync)
            {
                using var context = Open();
                context.Notifications.Where(x => x.Id == id).ExecuteDelete();
            }
        }

        public void RecordFailure(ReportNotification notification, bool retryable, TimeSpan? retryAfter)
        {
            lock (_sync)
            {
                using var context = Open();
                notification.Attempts++;
                notification.Failed = !retryable || notification.Attempts >= 3;
                var delay = retryAfter ?? TimeSpan.FromSeconds(5 * Math.Pow(2, notification.Attempts - 1));
                notification.NextAttemptUtcTicks = DateTime.UtcNow.Add(delay).Ticks;
                context.Update(notification);
                context.SaveChanges();
            }
        }
    }
}
