namespace MainCore.Entities
{
    public sealed class ConquestPlan
    {
        public Guid Id { get; set; }
        public int AccountId { get; set; }
        public int VillageId { get; set; }
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public string WavesJson { get; set; } = "";
        public DateTime ArrivalAt { get; set; }
        public DateTime SendAt { get; set; }
        public DateTime PrepareAt { get; set; }
        public double DelaySeconds { get; set; }
        public string OriginName { get; set; } = "";
        public string Status { get; set; } = "";
        public string LastError { get; set; } = "";
        public int Attempts { get; set; }
        public bool ConfirmationAttempted { get; set; }
        public bool CancelRequested { get; set; }
        public bool IsFinished { get; set; }
    }
}
