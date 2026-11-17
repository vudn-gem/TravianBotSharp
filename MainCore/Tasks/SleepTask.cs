using MainCore.Commands.Misc;
using MainCore.Infrasturecture.Extensions;
using MainCore.Services;
using MainCore.Tasks.Base;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class SleepTask(
        IDbContextFactory<AppDbContext> contextFactory,
        IChromeBrowser browser,
        ILogger logger,
        OpenBrowserCommand.Handler openBrowserCommand,
        TaskManager taskManager,
        SleepNotificationService sleepNotificationService)
    {
        public sealed class Task(AccountId accountId) : AccountTask(accountId)
        {
            protected override string TaskName => "Sleep";
        }

        private async ValueTask<Result> HandleAsync(
            Task task,
            CancellationToken cancellationToken)
        {
            taskManager.Remove<StartFarmListTask.Task>(task.AccountId);
            await browser.Shutdown();
            if (cancellationToken.IsCancellationRequested) return Cancel.Error;
            var sleepDuration = GetSleepDuration(task.AccountId);
            var expectedWakeUp = DateTimeOffset.Now.Add(sleepDuration);
            sleepNotificationService.NotifySleep(task.AccountId, sleepDuration, expectedWakeUp);
            await Sleep(expectedWakeUp, cancellationToken);

            var (_, isFailed, access, errors) = await GetAccess(task.AccountId);
            if (isFailed) return Result.Fail(errors);
            await openBrowserCommand.HandleAsync(new(task.AccountId, access), cancellationToken);

            var activityDuration = GetWorkDuration(task.AccountId);
            task.ExecuteAt = DateTime.Now.Add(activityDuration);
            sleepNotificationService.NotifyWakeUp(task.AccountId, access.Username, activityDuration);
            return Result.Ok();
        }

        private async Task<Result<AccessDto>> GetAccess(AccountId accountId)
        {
            using var context = contextFactory.CreateDbContext();
            return await context.GetValidAccess(accountId);
        }

        private async Task<Result> Sleep(DateTimeOffset expectedWakeUp, CancellationToken cancellationToken)
        {
            int lastMinute = 0;
            while (true)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var timeRemaining = expectedWakeUp - DateTimeOffset.Now;
                if (timeRemaining < TimeSpan.Zero) return Result.Ok();
                try
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    return Cancel.Error;
                }

                var currentMinute = (int)timeRemaining.TotalMinutes;
                if (lastMinute != currentMinute)
                {
                    logger.Information("Chrome will reopen in {CurrentMinute} mins", currentMinute);
                    lastMinute = currentMinute;
                }
            }
        }

        private TimeSpan GetSleepDuration(AccountId accountId)
        {
            using var context = contextFactory.CreateDbContext();
            var sleepSeconds = context.ByName(
                accountId,
                AccountSettingEnums.SleepTimeMin,
                AccountSettingEnums.SleepTimeMax,
                60);
            return TimeSpan.FromSeconds(sleepSeconds);
        }

        private TimeSpan GetWorkDuration(AccountId accountId)
        {
            using var context = contextFactory.CreateDbContext();
            var workSeconds = context.ByName(
                accountId,
                AccountSettingEnums.WorkTimeMin,
                AccountSettingEnums.WorkTimeMax,
                60);
            return TimeSpan.FromSeconds(workSeconds);
        }
    }
}