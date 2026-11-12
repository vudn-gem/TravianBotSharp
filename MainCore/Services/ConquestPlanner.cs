namespace MainCore.Services
{
    public sealed record ConquestPlanCalculation(
        double Distance,
        TimeSpan TravelTime,
        DateTime SendAt);

    public static class ConquestPlanner
    {
        public static Result<ConquestPlanCalculation> Calculate(
            int originX,
            int originY,
            int targetX,
            int targetY,
            double slowestTroopSpeed,
            int tournamentSquareLevel,
            double bootsPercent,
            double artifactMultiplier,
            double delaySeconds,
            DateTime arrivalAt)
        {
            if (!IsValidCoordinate(originX) || !IsValidCoordinate(originY) ||
                !IsValidCoordinate(targetX) || !IsValidCoordinate(targetY))
                return Result.Fail("Coordinates must be between -200 and 200.");
            if (!double.IsFinite(slowestTroopSpeed) || slowestTroopSpeed <= 0)
                return Result.Fail("Slowest troop speed must be greater than zero.");
            if (tournamentSquareLevel < 0)
                return Result.Fail("Tournament Square level cannot be negative.");
            if (!double.IsFinite(bootsPercent) || bootsPercent < 0)
                return Result.Fail("Boots bonus cannot be negative.");
            if (!double.IsFinite(artifactMultiplier) || artifactMultiplier <= 0)
                return Result.Fail("Artifact multiplier must be greater than zero.");
            if (!double.IsFinite(delaySeconds) || delaySeconds < 0)
                return Result.Fail("Network delay cannot be negative.");

            var deltaX = WrappedDelta(originX, targetX);
            var deltaY = WrappedDelta(originY, targetY);
            var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            var troopSpeed = slowestTroopSpeed * artifactMultiplier;
            if (!double.IsFinite(troopSpeed) || troopSpeed <= 0)
                return Result.Fail("Combined troop and artifact speed must be a finite positive value.");

            var hours = distance <= 20
                ? distance / troopSpeed
                : 20 / troopSpeed +
                  (distance - 20) /
                  (troopSpeed * (1 + tournamentSquareLevel * 2d / 10 + bootsPercent / 100));
            var travelSeconds = Math.Floor(hours * 3600);
            if (!double.IsFinite(travelSeconds) ||
                travelSeconds >= TimeSpan.MaxValue.TotalSeconds ||
                delaySeconds >= TimeSpan.MaxValue.TotalSeconds)
                return Result.Fail("Travel time or network delay is too large to schedule.");

            var travelTime = TimeSpan.FromSeconds(travelSeconds);
            var delay = TimeSpan.FromSeconds(delaySeconds);
            if (travelTime.Ticks > long.MaxValue - delay.Ticks ||
                arrivalAt.Ticks < travelTime.Ticks + delay.Ticks)
                return Result.Fail("The calculated send time is outside the supported date range.");

            var sendAt = arrivalAt.AddTicks(-(travelTime.Ticks + delay.Ticks));

            return Result.Ok(new ConquestPlanCalculation(distance, travelTime, sendAt));
        }

        private static bool IsValidCoordinate(int coordinate) => coordinate is >= -200 and <= 200;

        private static int WrappedDelta(int origin, int target)
        {
            var delta = Math.Abs(origin - target);
            return Math.Min(delta, 401 - delta);
        }
    }
}
