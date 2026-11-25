using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Extensions;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.UI.Models.Input;
using Microsoft.EntityFrameworkCore;
using ReactiveUI.Builder;

namespace MainCore.Test
{
    public sealed class EvasionSettingsTest : IDisposable
    {
        private readonly AppDbContext _context;

        public EvasionSettingsTest()
        {
            _context = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
            _context.Database.OpenConnection();
            _context.Database.EnsureCreated();
            _context.Accounts.AddRange(
                new Account { Id = 1, Username = "test", Server = "https://example.com" },
                new Account { Id = 2, Username = "test2", Server = "https://example.com" });
            _context.Villages.AddRange(
                new Village { Id = 1, AccountId = 1, Name = "Origin", X = 1, Y = 1 },
                new Village { Id = 2, AccountId = 1, Name = "Destination", X = -20, Y = 30 },
                new Village { Id = 3, AccountId = 2, Name = "Other account" });
            _context.SaveChanges();
            _context.FillVillageSettings();
        }

        [Fact]
        public void DefaultsDisabledWith120SecondsAndNone()
        {
            var settings = EvasionSettings.Read(_context, new(1), new(1));
            settings.IsSuccess.ShouldBeTrue();
            settings.Value.Enabled.ShouldBeFalse();
            settings.Value.TrainTroop.ShouldBe(TroopEnums.None);
            settings.Value.ResourceDestination.ShouldBeNull();
            settings.Value.EvadeTroops.ShouldBeFalse();
            settings.Value.LeadSeconds.ShouldBe(120);
        }

        [Fact]
        public void InputAndDatabaseRoundTripPreserveOneTroopAndDestinationCoordinates()
        {
            RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
            var input = new VillageSettingInput();
            input.Set(new()
            {
                [VillageSettingEnums.Tribe] = (int)TribeEnums.Huns,
                [VillageSettingEnums.EvasionEnable] = 1,
                [VillageSettingEnums.EvasionTrainTroop] = (int)TroopEnums.SteppeRider,
                [VillageSettingEnums.EvasionResourceVillage] = 2,
                [VillageSettingEnums.EvasionTroopEnable] = 1,
                [VillageSettingEnums.EvasionX] = -40,
                [VillageSettingEnums.EvasionY] = 50,
                [VillageSettingEnums.EvasionLeadSeconds] = 120,
            });
            var values = input.Get();
            _context.SaveVillageSetting(new(1), values);
            var settings = EvasionSettings.Read(_context, new(1), new(1)).Value;
            settings.Enabled.ShouldBeTrue();
            settings.TrainTroop.ShouldBe(TroopEnums.SteppeRider);
            settings.ResourceDestination.ShouldBe(new EvasionDestination(2, "Destination", -20, 30));
            settings.EvadeTroops.ShouldBeTrue();
            settings.X.ShouldBe(-40);
            settings.Y.ShouldBe(50);
            EvasionSettings.IsEnabled(_context, new(1)).ShouldBeTrue();
            EvasionSettings.IsEnabled(_context, new(2)).ShouldBeFalse();
        }

        [Theory]
        [InlineData(VillageSettingEnums.EvasionLeadSeconds, 0)]
        [InlineData(VillageSettingEnums.EvasionLeadSeconds, 3601)]
        [InlineData(VillageSettingEnums.EvasionResourceVillage, 1)]
        [InlineData(VillageSettingEnums.EvasionResourceVillage, 3)]
        [InlineData(VillageSettingEnums.EvasionResourceVillage, 999)]
        [InlineData(VillageSettingEnums.EvasionTrainTroop, 999)]
        [InlineData(VillageSettingEnums.EvasionEnable, 2)]
        [InlineData(VillageSettingEnums.EvasionTroopEnable, -1)]
        public void RejectsInvalidOrCrossAccountConfiguration(VillageSettingEnums key, int value)
        {
            var values = AppDbContext.VillageDefaultSettings.ToDictionary();
            values[key] = value;
            EvasionSettings.Read(_context, new(1), new(1), values).IsFailed.ShouldBeTrue();
        }

        [Fact]
        public void RejectsOriginOrOutOfRangeTroopDestination()
        {
            var values = AppDbContext.VillageDefaultSettings.ToDictionary();
            values[VillageSettingEnums.EvasionTroopEnable] = 1;
            values[VillageSettingEnums.EvasionX] = 1;
            values[VillageSettingEnums.EvasionY] = 1;
            EvasionSettings.Read(_context, new(1), new(1), values).IsFailed.ShouldBeTrue();
            values[VillageSettingEnums.EvasionX] = -401;
            EvasionSettings.Read(_context, new(1), new(1), values).IsFailed.ShouldBeTrue();
        }

        public void Dispose() => _context.Dispose();
    }
}
