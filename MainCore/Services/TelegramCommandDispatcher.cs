using System.Globalization;

using MainCore.Infrasturecture.Extensions;

namespace MainCore.Services
{
    [RegisterSingleton<TelegramCommandDispatcher>]
    public sealed class TelegramCommandDispatcher(
        IDbContextFactory<AppDbContext> contextFactory, TaskManager taskManager, RxQueue rxQueue)
    {
        private sealed record Command(string Name, int Arguments, string Usage, string Description);

        private static readonly Command[] Commands =
        [
            new("tbs-command", 0, "/tbs-command", "Show available commands."),
            new("accounts", 0, "/accounts", "List account IDs and status."),
            new("status", 1, "/status <account-id>", "Show status and queued tasks."),
            new("farm-lists", 1, "/farm-lists <account-id>", "List saved farm list IDs and Active settings."),
            new("hero-farm", 2, "/hero-farm <account-id> <farm-list-id>", "Queue Hero clear oasis using saved settings."),
            new("pause", 1, "/pause <account-id>", "Pause an online account."),
            new("resume", 1, "/resume <account-id>", "Resume a paused account; does not log in an offline account."),
            new("start-farm", 1, "/start-farm <account-id>", "Queue Start farm list using saved settings."),
            new("stop-farm", 1, "/stop-farm <account-id>", "Remove Start farm list; an in-progress send may finish."),
        ];

        public async Task<string> Execute(string text, CancellationToken cancellationToken)
        {
            var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "Send /tbs-command for available commands.";
            var name = parts[0].TrimStart('/').Replace('_', '-').ToLowerInvariant();
            var command = Commands.SingleOrDefault(x => x.Name == name);
            if (!parts[0].StartsWith('/') || command is null)
                return "Unknown command. Send /tbs-command for available commands.";
            if (parts.Length != command.Arguments + 1) return $"Usage: {command.Usage}";
            if (command.Arguments == 0 && name == "tbs-command")
                return "[TBS] Commands\n" + string.Join("\n", Commands.Select(x => $"{x.Usage} - {x.Description}")) +
                    "\nSend commands as plain text. Underscore aliases (such as /hero_farm) are also supported." +
                    "\nUse /accounts, then /farm-lists <account-id> to find IDs. Queued tasks wait while paused/offline.";

            using var context = contextFactory.CreateDbContext();
            if (name == "accounts")
            {
                var accounts = context.Accounts.AsNoTracking().OrderBy(x => x.Id).ToList();
                return accounts.Count == 0 ? "No accounts saved in TBS." :
                    "[TBS] Accounts\n" + string.Join("\n", accounts.Select(x =>
                        $"{x.Id}: {Label(x.Username)} - {taskManager.GetStatus(new AccountId(x.Id))}"));
            }

            if (!TryId(parts[1], out var accountValue)) return $"Usage: {command.Usage}. IDs must be positive integers.";
            var accountId = new AccountId(accountValue);
            if (!context.Accounts.Any(x => x.Id == accountValue)) return "Account not found. Send /accounts.";
            cancellationToken.ThrowIfCancellationRequested();
            var queue = taskManager.GetTaskQueue(accountId);
            if (name == "farm-lists")
            {
                var farms = context.FarmLists.AsNoTracking().Where(x => x.AccountId == accountValue).OrderBy(x => x.Id).ToList();
                return farms.Count == 0 ? "No saved farm lists. Load farm lists in TBS first." :
                    $"[TBS] Farm lists for account {accountValue}\n" +
                    string.Join("\n", farms.Select(x => $"{x.Id}: {Label(x.Name)} - {(x.IsActive ? "Active" : "Inactive")}"));
            }
            if (name == "pause")
            {
                lock (queue)
                {
                    if (queue.Status != StatusEnums.Online) return $"Cannot pause: account is {queue.Status}.";
                    taskManager.SetStatus(accountId, StatusEnums.Pausing);
                }
                await taskManager.StopCurrentTask(accountId);
                return $"Account {accountValue} paused.";
            }
            lock (queue)
            {
                if (name == "status")
                    return $"[TBS] Account {accountValue}: {queue.Status}\nTasks: {queue.Tasks.Count}\n" +
                        string.Join("\n", queue.Tasks.OrderBy(x => x.ExecuteAt).Take(20)
                            .Select(x => $"{Label(x.Description)} - {x.Stage}"));
                if (queue.Status is StatusEnums.Starting or StatusEnums.Pausing or StatusEnums.Stopping)
                    return $"Account is {queue.Status}. Wait and try again.";
                switch (name)
                {
                    case "resume":
                        if (queue.Status != StatusEnums.Paused) return $"Cannot resume: account is {queue.Status}. Log in using TBS if offline.";
                        taskManager.SetStatus(accountId, StatusEnums.Online);
                        return $"Account {accountValue} resumed.";
                    case "start-farm":
                        if (!context.BooleanByName(accountId, AccountSettingEnums.UseStartAllButton) &&
                            !context.FarmLists.Any(x => x.AccountId == accountValue && x.IsActive))
                            return "No active farm lists and Start all is disabled. Save farming settings in TBS first.";
                        if (!taskManager.TryAdd(new StartFarmListTask.Task(accountId)))
                            return "Start farm list is already queued. Its schedule was not changed.";
                        return $"Queued Start farm list for account {accountValue}. Current status: {queue.Status}.";
                    case "stop-farm":
                        var removed = queue.Tasks.RemoveAll(x => x is StartFarmListTask.Task);
                        rxQueue.Enqueue(new TasksModified(accountId));
                        return removed == 0 ? "No Start farm list task queued." :
                            "Removed Start farm list. A send already in progress may finish; raids already sent are not canceled.";
                    case "hero-farm":
                        if (!TryId(parts[2], out var farmValue)) return $"Usage: {command.Usage}. IDs must be positive integers.";
                        if (!context.FarmLists.Any(x => x.Id == farmValue && x.AccountId == accountValue))
                            return "Farm list does not belong to this account. Send /farm-lists <account-id>.";
                        var settings = context.HeroOasisSettings.AsNoTracking().SingleOrDefault(x => x.FarmId == farmValue);
                        if (settings is null) return "Save Hero clear oasis settings for this farm list in TBS first.";
                        var validation = HeroOasisSettings.Validate(settings);
                        if (validation.IsFailed)
                            return "Invalid saved hero settings: " + string.Join("; ", validation.Errors.Select(x => x.Message));
                        return taskManager.TryAddHeroClearOasis(accountId, new FarmId(farmValue))
                            ? $"Queued Hero clear oasis for account {accountValue}, farm list {farmValue}. Current status: {queue.Status}."
                            : "A Hero clear oasis task already exists for this account, or the farm list no longer exists.";
                    default:
                        return "Unknown command. Send /tbs-command.";
                }
            }
        }

        private static bool TryId(string value, out int id) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

        private static string Label(string value) =>
            new string(value.Replace('\n', ' ').Replace('\r', ' ').Take(200).ToArray());
    }
}
