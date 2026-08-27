using AnalysisITC.Core.Application;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class EnergyUnitFamilySettingsTests
    {
        [Theory]
        [InlineData(EnergyUnit.Joule, EnergyUnitFamily.Joules)]
        [InlineData(EnergyUnit.KiloJoule, EnergyUnitFamily.Joules)]
        [InlineData(EnergyUnit.MicroCal, EnergyUnitFamily.Calories)]
        [InlineData(EnergyUnit.Cal, EnergyUnitFamily.Calories)]
        [InlineData(EnergyUnit.KCal, EnergyUnitFamily.Calories)]
        public void EveryLegacyExactUnitMigratesToItsFamily(
            EnergyUnit legacyUnit,
            EnergyUnitFamily expectedFamily)
        {
            var original = PlatformServices.SettingsStore;
            var store = new InMemorySettingsStore();
            store.SetInt("EnergyUnit", (int)legacyUnit);
            PlatformServices.RegisterSettingsStore(store);

            try
            {
                AppSettings.Load();
                Assert.Equal(expectedFamily, AppSettings.EnergyUnitFamily);
                Assert.Equal((int)expectedFamily, store.GetInt("EnergyUnitFamily", -1));
                Assert.Equal((int)legacyUnit, store.GetInt("EnergyUnit", -1));
            }
            finally
            {
                PlatformServices.RegisterSettingsStore(original);
            }
        }

        [Fact]
        public void LegacyCaloriePreferenceMigratesAndLeavesLegacyValueUntouched()
        {
            var original = PlatformServices.SettingsStore;
            var store = new InMemorySettingsStore();
            store.SetInt("EnergyUnit", (int)EnergyUnit.MicroCal);
            PlatformServices.RegisterSettingsStore(store);

            try
            {
                AppSettings.Load();

                Assert.Equal(EnergyUnitFamily.Calories, AppSettings.EnergyUnitFamily);
                Assert.Equal((int)EnergyUnitFamily.Calories, store.GetInt("EnergyUnitFamily", -1));
                Assert.Equal((int)EnergyUnit.MicroCal, store.GetInt("EnergyUnit", -1));
            }
            finally
            {
                PlatformServices.RegisterSettingsStore(original);
            }
        }

        [Fact]
        public void ValidNewFamilyTakesPrecedenceOverLegacyExactUnit()
        {
            var original = PlatformServices.SettingsStore;
            var store = new InMemorySettingsStore();
            store.SetInt("EnergyUnitFamily", (int)EnergyUnitFamily.Joules);
            store.SetInt("EnergyUnit", (int)EnergyUnit.KCal);
            PlatformServices.RegisterSettingsStore(store);

            try
            {
                AppSettings.Load();
                Assert.Equal(EnergyUnitFamily.Joules, AppSettings.EnergyUnitFamily);
            }
            finally
            {
                PlatformServices.RegisterSettingsStore(original);
            }
        }

        [Fact]
        public void InvalidNewFamilyDefaultsToJoulesInsteadOfUsingLegacyValue()
        {
            var original = PlatformServices.SettingsStore;
            var store = new InMemorySettingsStore();
            store.SetInt("EnergyUnitFamily", 999);
            store.SetInt("EnergyUnit", (int)EnergyUnit.KCal);
            PlatformServices.RegisterSettingsStore(store);

            try
            {
                AppSettings.Load();
                Assert.Equal(EnergyUnitFamily.Joules, AppSettings.EnergyUnitFamily);
                Assert.Equal((int)EnergyUnitFamily.Joules, store.GetInt("EnergyUnitFamily", -1));
            }
            finally
            {
                PlatformServices.RegisterSettingsStore(original);
            }
        }

        [Fact]
        public void ResetUsesJouleFamily()
        {
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Calories;
            AppSettings.Reset();
            Assert.Equal(EnergyUnitFamily.Joules, AppSettings.EnergyUnitFamily);
        }

        [Fact]
        public void MissingAndInvalidLegacyValuesDefaultToJoules()
        {
            var original = PlatformServices.SettingsStore;
            var store = new InMemorySettingsStore();
            PlatformServices.RegisterSettingsStore(store);

            try
            {
                AppSettings.Load();
                Assert.Equal(EnergyUnitFamily.Joules, AppSettings.EnergyUnitFamily);

                store = new InMemorySettingsStore();
                store.SetInt("EnergyUnit", 999);
                PlatformServices.RegisterSettingsStore(store);
                AppSettings.Load();
                Assert.Equal(EnergyUnitFamily.Joules, AppSettings.EnergyUnitFamily);
                Assert.Equal((int)EnergyUnitFamily.Joules, store.GetInt("EnergyUnitFamily", -1));
            }
            finally
            {
                PlatformServices.RegisterSettingsStore(original);
            }
        }
    }
}
