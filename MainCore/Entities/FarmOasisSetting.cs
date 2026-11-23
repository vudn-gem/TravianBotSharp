using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainCore.Entities
{
    public class FarmOasisSetting
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int FarmId { get; set; }
        public int AllowedAnimals { get; set; } = 27;
        public int CheckAllowedAnimals { get; set; }
        public int SafeAnimal { get; set; } = 1;
        public bool CheckAutoActive { get; set; }
        public decimal SafetyFactor { get; set; } = 2.5m;
        public OasisAttackTroop AttackTroop { get; set; } = OasisAttackTroop.SteppeRider;
        public int RaidSteppeRiders { get; set; } = 2;
        public int RaidMarksmen { get; set; }
        public int RaidMarauders { get; set; }
        public int LimitAnimal { get; set; }
    }
}
