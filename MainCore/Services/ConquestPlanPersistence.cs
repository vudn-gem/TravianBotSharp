using System.Text.Json;

namespace MainCore.Services
{
    public static class ConquestPlanPersistence
    {
        public sealed record SavedWave(long[] Troops, int CatapultTarget1, int? CatapultTarget2);

        public static ConquestPlan Capture(ConquestTask.Task task) => new()
        {
            Id = task.PlanId,
            AccountId = task.AccountId.Value,
            VillageId = task.VillageId.Value,
            TargetX = task.TargetX,
            TargetY = task.TargetY,
            WavesJson = JsonSerializer.Serialize(task.Waves.Select(wave =>
                new SavedWave(wave.Troops.ToArray(), wave.CatapultTarget1, wave.CatapultTarget2))),
            ArrivalAt = task.ArrivalAt,
            SendAt = task.SendAt,
            PrepareAt = task.PrepareAt,
            DelaySeconds = task.DelaySeconds,
            OriginName = task.OriginName,
            Status = task.Status,
            LastError = task.LastError,
            Attempts = task.Attempts,
            ConfirmationAttempted = task.ConfirmationAttempted,
            CancelRequested = task.CancelRequested,
            IsFinished = task.IsFinished,
        };

        public static ConquestTask.Task Restore(ConquestPlan plan, DateTime now, bool ownsVillage)
        {
            var waves = JsonSerializer.Deserialize<SavedWave[]>(plan.WavesJson)
                ?? throw new JsonException($"Conquest plan {plan.Id} has no wave data.");
            var task = new ConquestTask.Task(new(plan.AccountId), new(plan.VillageId),
                plan.TargetX, plan.TargetY,
                waves.Select(wave => new ConquestWave(wave.Troops, wave.CatapultTarget1, wave.CatapultTarget2)).ToArray(),
                plan.ArrivalAt, plan.SendAt, plan.DelaySeconds)
            {
                PlanId = plan.Id,
                PrepareAt = plan.PrepareAt,
                OriginName = plan.OriginName,
                Status = plan.Status,
                LastError = plan.LastError,
                Attempts = plan.Attempts,
                ConfirmationAttempted = plan.ConfirmationAttempted,
                IsFinished = plan.IsFinished,
            };
            if (task.IsFinished) return task;

            if (plan.ConfirmationAttempted)
            {
                task.Status = "Check in game";
                task.LastError = "TBS stopped after confirmation was attempted. Verify the attack in game; it will not be resent.";
                task.IsFinished = true;
            }
            else if (plan.CancelRequested)
            {
                task.RequestCancel();
                task.Status = "Canceled";
                task.IsFinished = true;
            }
            else if (!ownsVillage || plan.SendAt <= now || plan.Attempts >= 2)
            {
                task.Status = plan.SendAt <= now ? "Expired" : "Failed";
                task.LastError = !ownsVillage ? "Origin village no longer belongs to this account." :
                    plan.SendAt <= now ? "The planned send time has passed." : "The two-attempt limit has been reached.";
                task.IsFinished = true;
            }
            else
            {
                if (waves.Length is < 1 or > ConquestWave.MaxWaves ||
                    task.Waves.Any(wave => wave.Validate().IsFailed))
                    throw new InvalidDataException($"Conquest plan {plan.Id} contains invalid waves.");
                task.Status = "Scheduled";
            }
            return task;
        }
    }
}
