using MainCore.Entities;
using MainCore.Enums;
using MainCore.Models;
using MainCore.Tasks;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.Test
{
    public class ConquestWaveTest
    {
        [Theory]
        [InlineData(TribeEnums.Romans, "Legionnaire", "Fire Catapult", "Senator")]
        [InlineData(TribeEnums.Teutons, "Clubswinger", "Catapult", "Chief")]
        [InlineData(TribeEnums.Gauls, "Phalanx", "Trebuchet", "Chieftain")]
        [InlineData(TribeEnums.Egyptians, "Slave Militia", "Stone Catapult", "Nomarch")]
        [InlineData(TribeEnums.Huns, "Mercenary", "Catapult", "Logades")]
        [InlineData(TribeEnums.Spartan, "Hoplite", "Catapult", "Ephor")]
        public void LabelsFollowVillageTribeWithoutChangingWaveData(
            TribeEnums tribe, string firstTroop, string catapult, string chief)
        {
            var wave = new ConquestWaveViewModel(1);
            wave.Troops[0].Count = "19";
            wave.Troops[7].Count = "20";
            wave.CatapultTarget1 = 4;
            wave.CatapultTarget2 = 25;
            wave.SetTribe(tribe);
            wave.Troops[0].Label.ShouldBe($"t1: {firstTroop}");
            wave.Troops[7].Label.ShouldBe($"t8: {catapult}");
            wave.Troops[8].Label.ShouldBe($"t9: {chief}");
            wave.Troops[9].Label.ShouldBe("t10: Settler");
            wave.Troops[10].Label.ShouldBe("Hero");
            var copy = wave.Copy(2);
            copy.Troops.Select(troop => troop.Label).ShouldBe(wave.Troops.Select(troop => troop.Label));
            wave.SetTribe(TribeEnums.Any);
            wave.Troops[0].Label.ShouldBe("t1");
            wave.Troops[9].Label.ShouldBe("t10");
            wave.Troops[10].Label.ShouldBe("Hero");
            wave.Troops[0].Count.ShouldBe("19");
            wave.CatapultTarget1.ShouldBe(4);
            wave.CatapultTarget2.ShouldBe(25);
        }

        [Fact]
        public void HunTroopOrderMatchesRallyPoint()
        {
            var wave = new ConquestWaveViewModel(1);
            wave.SetTribe(TribeEnums.Huns);
            wave.Troops.Select(troop => troop.Label).ShouldBe(new[]
            {
                "t1: Mercenary", "t2: Bowman", "t3: Spotter", "t4: Steppe Rider",
                "t5: Marksman", "t6: Marauder", "t7: Ram", "t8: Catapult",
                "t9: Logades", "t10: Settler", "Hero",
            });
        }

        [Theory]
        [InlineData("0", false, false)]
        [InlineData("1", true, false)]
        [InlineData("19", true, false)]
        [InlineData("20", true, true)]
        [InlineData("21", true, true)]
        [InlineData("-1", false, false)]
        [InlineData("invalid", false, false)]
        public void CatapultCountControlsTargetSelectors(string count, bool firstEnabled, bool secondEnabled)
        {
            var row = new ConquestWaveViewModel(1);
            row.Troops.Count.ShouldBe(11);
            row.Troops[10].Label.ShouldBe("Hero");
            row.Troops[7].Count = count;
            row.HasCatapults.ShouldBe(firstEnabled);
            row.CanSelectSecondTarget.ShouldBe(secondEnabled);
        }

        [Fact]
        public void ReducingCatapultsClearsSecondTarget()
        {
            var row = new ConquestWaveViewModel(1);
            row.Troops[7].Count = "20";
            row.CatapultTarget2 = 25;
            row.Troops[7].Count = "19";
            row.CatapultTarget2.ShouldBeNull();
            row.BuildWave().IsSuccess.ShouldBeTrue();
        }

        [Fact]
        public void CopyPreservesTargetsAndHasIndependentTroopInputs()
        {
            var row = new ConquestWaveViewModel(1);
            row.Troops[0].Count = "19";
            row.Troops[7].Count = "20";
            row.CatapultTarget1 = 4;
            row.CatapultTarget2 = 25;

            var copy = row.Copy(2);
            copy.Number.ShouldBe(2);
            copy.Troops[0].Count.ShouldBe("19");
            copy.Troops[7].Count.ShouldBe("20");
            copy.CatapultTarget1.ShouldBe(4);
            copy.CatapultTarget2.ShouldBe(25);
            copy.Troops[0].Count = "99";
            row.Troops[0].Count.ShouldBe("19");
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("1.5")]
        [InlineData("9223372036854775808")]
        public void InvalidTroopInputIsRejected(string input)
        {
            var row = new ConquestWaveViewModel(1);
            row.Troops[0].Count = input;
            row.BuildWave().IsFailed.ShouldBeTrue();
        }

        [Fact]
        public void EmptyWaveIsRejected()
        {
            new ConquestWaveViewModel(1).BuildWave().IsFailed.ShouldBeTrue();
        }

        [Theory]
        [InlineData("0", true)]
        [InlineData("", true)]
        [InlineData(" ", true)]
        [InlineData("00", true)]
        [InlineData("1", false)]
        [InlineData("-1", false)]
        [InlineData("invalid", false)]
        public void OnlyEmptyOrZeroTroopRowsAreDrafts(string input, bool expectedDraft)
        {
            var row = new ConquestWaveViewModel(1);
            row.Troops[0].Count = input;
            row.IsDraft.ShouldBe(expectedDraft);
        }

        [Fact]
        public void FilledWaveTreatsBlankFieldsAsZero()
        {
            var row = new ConquestWaveViewModel(1);
            foreach (var troop in row.Troops) troop.Count = "";
            row.Troops[0].Count = "19";
            row.IsDraft.ShouldBeFalse();
            var result = row.BuildWave();
            result.IsSuccess.ShouldBeTrue();
            result.Value.Troops[0].ShouldBe(19);
            result.Value.Troops.Skip(1).ShouldAllBe(count => count == 0);
            ConquestWave.MaxWaves.ShouldBe(8);
        }

        [Theory]
        [InlineData(19, 4, 25, false)]
        [InlineData(20, 4, 25, true)]
        [InlineData(21, 4, 25, true)]
        [InlineData(1, 27, null, true)]
        [InlineData(1, 44, null, true)]
        [InlineData(20, 4, 44, true)]
        [InlineData(1, 41, null, true)]
        [InlineData(20, 4, 41, true)]
        [InlineData(20, 999, null, false)]
        [InlineData(20, 4, 999, false)]
        public void WaveValidationUsesGameTargetValuesAndTwentyCatapultThreshold(
            long catapults, int firstTarget, int? secondTarget, bool valid)
        {
            var troops = new long[11];
            troops[7] = catapults;
            new ConquestWave(troops, firstTarget, secondTarget).Validate().IsSuccess.ShouldBe(valid);
        }

        [Fact]
        public void CatalogMatchesSuppliedGameOptions()
        {
            ConquestCatapultTargets.Options.Count.ShouldBe(36);
            ConquestCatapultTargets.Options.Select(option => option.Value).Distinct().Count().ShouldBe(36);
            ConquestCatapultTargets.Options.Single(option => option.Name == "Random").Value.ShouldBe(99);
            ConquestCatapultTargets.Options.Single(option => option.Name == "Cropland").Value.ShouldBe(4);
            ConquestCatapultTargets.Options.Single(option => option.Name == "Residence").Value.ShouldBe(25);
            var commandCenter = ConquestCatapultTargets.Options.Single(option => option.Name == "Command Center");
            commandCenter.Value.ShouldBe(44);
            commandCenter.Group.ShouldBe("Infrastructure");
            ConquestCatapultTargets.OptionalOptions.ShouldContain(commandCenter);
            var horseDrinkingTrough = ConquestCatapultTargets.Options.Single(option => option.Name == "Horse Drinking Trough");
            horseDrinkingTrough.Value.ShouldBe(41);
            horseDrinkingTrough.Group.ShouldBe("Military");
            ConquestCatapultTargets.OptionalOptions.ShouldContain(horseDrinkingTrough);
            ConquestCatapultTargets.Options.Single(option => option.Name == "Palace").Value.ShouldBe(26);
            ConquestCatapultTargets.Options.Single(option => option.Name == "Treasury").Value.ShouldBe(27);
            ConquestCatapultTargets.OptionalOptions[0].Value.ShouldBeNull();
        }

        [Fact]
        public void QueuedWaveIsASnapshotAndTargetsArePartOfTaskIdentity()
        {
            var row = new ConquestWaveViewModel(1);
            row.Troops[7].Count = "20";
            row.CatapultTarget1 = 4;
            row.CatapultTarget2 = 25;
            var wave = row.BuildWave().Value;
            var arrival = DateTime.Now.AddHours(2);
            var send = DateTime.Now.AddHours(1);
            var original = new ConquestTask.Task(new AccountId(1), new VillageId(2), 3, 4, [wave], arrival, send, 0.4);
            row.Troops[7].Count = "21";
            row.CatapultTarget2 = 26;
            var modified = new ConquestTask.Task(new AccountId(1), new VillageId(2), 3, 4,
                [row.BuildWave().Value], arrival, send, 0.4);
            original.Waves[0].Troops[7].ShouldBe(20);
            original.Waves[0].CatapultTarget2.ShouldBe(25);
            original.Key.ShouldNotBe(modified.Key);

            var changedTarget = new ConquestTask.Task(new AccountId(1), new VillageId(2), 3, 4,
                [new ConquestWave(wave.Troops, 4, 26)], arrival, send, 0.4);
            original.Key.ShouldNotBe(changedTarget.Key);
        }
    }
}
