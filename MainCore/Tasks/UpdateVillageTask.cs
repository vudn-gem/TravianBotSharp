using MainCore.Commands.Navigate;
using MainCore.Commands.Update;
using MainCore.Infrasturecture.Extensions;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class UpdateVillageTask(
        IChromeBrowser browser,
        UpdateBuildingCommand.Handler updateBuildingCommand,
        ToDorfCommand.Handler toDorfCommand,
        IDbContextFactory<AppDbContext> contextFactory)
    {
        public sealed class Task(AccountId accountId, VillageId villageId) : VillageTask(accountId, villageId)
        {
            protected override string TaskName => "Update village";

            public override bool CanStart(AppDbContext context)
            {
                var settingEnable = context.BooleanByName(VillageId, VillageSettingEnums.AutoRefreshEnable);
                if (!settingEnable) return false;

                return true;
            }
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            var path = Uri.TryCreate(browser.CurrentUrl, UriKind.Absolute, out var uri)
                ? uri.AbsolutePath
                : string.Empty;
            Result result;

            bool isFailed;
            IReadOnlyList<IError> errors;

            if (path == "/resources")
            {
                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);
            }
            else if (path == "/village")
            {
                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);

                result = await toDorfCommand.HandleAsync(new(1), cancellationToken);
                if (result.IsFailed) return result;

                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);
            }
            else
            {
                result = await toDorfCommand.HandleAsync(new(1), cancellationToken);
                if (result.IsFailed) return result;

                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);
            }

            task.ExecuteAt = GetNextExecuteTime(task.VillageId);
            return Result.Ok();
        }

        private DateTime GetNextExecuteTime(VillageId villageId)
        {
            using var context = contextFactory.CreateDbContext();
            var seconds = context.ByName(
                villageId,
                VillageSettingEnums.AutoRefreshMin,
                VillageSettingEnums.AutoRefreshMax,
                60);
            var nextExecute = DateTime.Now.AddSeconds(seconds);
            return nextExecute;
        }
    }
}
