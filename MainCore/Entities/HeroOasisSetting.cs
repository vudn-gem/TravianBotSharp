using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainCore.Entities
{
    public class HeroOasisSetting
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int FarmId { get; set; }
        public int AllowedAnimals { get; set; }
        public int MinAnimal { get; set; } = 1;
        public int MaxAnimal { get; set; } = 100;
        public int SteppeRiders { get; set; } = 1;
        public int Marksmen { get; set; } = 1;
        public int Marauders { get; set; } = 1;
        public int MinHeroHealth { get; set; } = 50;
        public int MaxHeroHealthLost { get; set; } = 10;
        public int MaxSubTroopLost { get; set; }
    }
}
