using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainCore.Entities
{
    public class FarmOasisSetting
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int FarmId { get; set; }
        public int AllowedAnimals { get; set; }
        public int SafetyFactor { get; set; } = 2;
        public OasisAttackTroop AttackTroop { get; set; } = OasisAttackTroop.SteppeRider;
        public int RaidSteppeRiders { get; set; } = 2;
        public int RaidMarksmen { get; set; }
        public int RaidMarauders { get; set; }
        public int LimitAnimal { get; set; }
    }
}
