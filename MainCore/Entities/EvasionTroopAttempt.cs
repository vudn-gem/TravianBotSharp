namespace MainCore.Entities
{
    public sealed class EvasionTroopAttempt
    {
        public int AccountId { get; set; }
        public int VillageId { get; set; }
        public long MovementId { get; set; }
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public string Status { get; set; } = "Check in game";
    }
}
