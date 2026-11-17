using MainCore.Parsers;

namespace MainCore.Services
{
    public static class FarmTargetOasisPolicy
    {
        public static bool HasOnlySafeAllowedAnimals(
            IEnumerable<OasisAnimalTroop> troops, IReadOnlySet<int> allowedAnimals, int safeAnimalLimit)
        {
            if (safeAnimalLimit < 0) return false;
            var present = troops.Where(x => x.Count > 0).ToList();
            long total = 0;
            foreach (var animal in present)
            {
                if (!allowedAnimals.Contains(animal.UnitId) || animal.Count > safeAnimalLimit - total)
                    return false;
                total += animal.Count;
            }
            return true;
        }
    }
}
