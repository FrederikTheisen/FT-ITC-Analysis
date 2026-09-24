using System;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection(FileTypeFixtureCollectionDefinition.Name)]
    public sealed class MolarEnergyExportTests
    {
        const double PeakAreaJoules = -0.0123456789012345;
        const double PeakAreaSdJoules = 0.000000987654321;
        const double InjectionMoles = 0.000001;
        const double FitJoulesPerMole = -12000.123456789;
        const double OffsetJoulesPerMole = 123.456789;

        [Theory]
        [InlineData(EnergyUnit.Joule, 1.0, "j_per_mol", "J")]
        [InlineData(EnergyUnit.KiloJoule, 1000.0, "kj_per_mol", "KJ")]
        [InlineData(EnergyUnit.Cal, 4.184, "cal_per_mol", "CAL")]
        [InlineData(EnergyUnit.KCal, 4184.0, "kcal_per_mol", "KCAL")]
        public void PeakAndCombinedExportsScaleAllMolarColumns(
            EnergyUnit unit, double joulesPerUnit, string headerUnit, string metadataUnit)
        {
            var data = CreateExperiment();
            var settings = new ExportAccessoryViewSettings { ExportEnergyUnit = unit };
            var observed = PeakAreaJoules / InjectionMoles;
            var sd = PeakAreaSdJoules / InjectionMoles;

            foreach (var offsetCorrected in new[] { false, true })
            {
                settings.ExportOffsetCorrected = offsetCorrected;
                var peak = offsetCorrected ? observed - OffsetJoulesPerMole : observed;
                var fit = offsetCorrected ? FitJoulesPerMole - OffsetJoulesPerMole : FitJoulesPerMole;
                var expected = new[] { peak, sd, fit, peak - fit }
                    .Select(value => value / joulesPerUnit).ToArray();

                var integrated = Exporter.BuildIntegratedPeakLines(data, settings);
                var combined = Exporter.BuildInterchangeLines(data, settings);

                Assert.Equal(
                    $"molar_ratio,integrated_enthalpy_{headerUnit},sd_{headerUnit},model_{headerUnit},residual_{headerUnit}",
                    integrated[0]);
                Assert.EndsWith(integrated[0], combined[0], StringComparison.Ordinal);
                AssertScaledValues(expected, integrated[1].Split(',').Skip(1).ToArray());

                // Combined Data deliberately exports uncorrected peaks, independently
                // of the offset option used by Integrated Peaks and ITCsim.
                var combinedExpected = new[]
                {
                    observed, sd, FitJoulesPerMole, observed - FitJoulesPerMole
                }.Select(value => value / joulesPerUnit).ToArray();
                AssertScaledValues(combinedExpected, combined[1].Split(',').Skip(4).ToArray());
            }

            settings.Export = ExportType.ITCsim;
            settings.ExportOffsetCorrected = false;
            var itcsim = Exporter.BuildITCsimLines(data, settings);
            Assert.Equal("MolarRatio,Included,PeakHeat,InjVolume,InjDelay", itcsim[0]);
            AssertClose(observed / joulesPerUnit, Parse(itcsim[1].Split(',')[2]));
            Assert.Contains($"#EXPINFO ENERGYUNIT {metadataUnit}", itcsim);

            settings.ExportOffsetCorrected = true;
            itcsim = Exporter.BuildITCsimLines(data, settings);
            AssertClose((observed - OffsetJoulesPerMole) / joulesPerUnit, Parse(itcsim[1].Split(',')[2]));
        }

        [Fact]
        public void EnergyChoiceDefaultsFromPreferenceEachTime()
        {
            var original = AppSettings.EnergyUnitFamily;
            try
            {
                AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
                var first = ExportAccessoryViewSettings.CreateDefault(ExportType.Peaks);
                Assert.Equal(EnergyUnit.KiloJoule, first.ExportEnergyUnit);
                first.ExportEnergyUnit = EnergyUnit.Joule;
                Assert.Equal(EnergyUnit.KiloJoule,
                    ExportAccessoryViewSettings.CreateDefault(ExportType.Peaks).ExportEnergyUnit);

                AppSettings.EnergyUnitFamily = EnergyUnitFamily.Calories;
                Assert.Equal(EnergyUnit.KCal,
                    ExportAccessoryViewSettings.CreateDefault(ExportType.ITCsim).ExportEnergyUnit);
            }
            finally
            {
                AppSettings.EnergyUnitFamily = original;
            }
        }

        [Fact]
        public void SmallMolarHeatRetainsPrecisionAndUsesInvariantDecimalPoint()
        {
            var data = CreateExperiment();
            data.Injections[0].SetPeakArea(new FloatWithError(-1.23456789012345e-12, 9.87654321098765e-14));
            var settings = new ExportAccessoryViewSettings
            {
                Export = ExportType.ITCsim,
                ExportEnergyUnit = EnergyUnit.KCal
            };
            var original = CultureInfo.CurrentCulture;
            try
            {
                var reference = Exporter.BuildIntegratedPeakLines(data, settings);
                var itcsimReference = Exporter.BuildITCsimLines(data, settings);
                foreach (var name in new[] { "en-US", "da-DK", "de-DE" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                    Assert.Equal(reference, Exporter.BuildIntegratedPeakLines(data, settings));
                    Assert.Equal(itcsimReference, Exporter.BuildITCsimLines(data, settings));
                }

                var exported = Parse(reference[1].Split(',')[1]);
                var independentlyCalculated = (-1.23456789012345e-12 / InjectionMoles) / 4184.0;
                AssertClose(independentlyCalculated, exported, relativeTolerance: 1e-14);
                Assert.NotEqual(0, exported);
                Assert.Contains("E", reference[1].Split(',')[1], StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        static ExperimentData CreateExperiment()
        {
            var data = new ExperimentData("molar-energy-export.itc")
            {
                SyringeConcentration = new FloatWithError(0.001),
                CellConcentration = new FloatWithError(0.0001),
                CellVolume = 0.0014,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };
            var injection = new InjectionData(data, 0, 0.001, InjectionMoles, include: true)
            {
                Ratio = 1.25,
                ActualCellConcentration = 0.0001,
                ActualTitrantConcentration = 0.0002,
            };
            injection.SetPeakArea(new FloatWithError(PeakAreaJoules, PeakAreaSdJoules));
            data.Injections.Add(injection);

            var model = new FixedMolarHeatModel(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, OffsetJoulesPerMole);
            data.Model = model;
            model.Solution = SolutionInterface.FromModel(
                model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            return data;
        }

        static void AssertScaledValues(double[] expected, string[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (var index = 0; index < expected.Length; index++)
                AssertClose(expected[index], Parse(actual[index]));
        }

        static double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

        static void AssertClose(double expected, double actual, double relativeTolerance = 1e-12)
        {
            var tolerance = Math.Max(1e-24, Math.Abs(expected) * relativeTolerance);
            Assert.InRange(actual, expected - tolerance, expected + tolerance);
        }

        sealed class FixedMolarHeatModel : Model
        {
            public FixedMolarHeatModel(ExperimentData data) : base(data) { }

            public override double Evaluate(int injectionindex, bool withoffset = true)
            {
                var molarHeat = withoffset
                    ? FitJoulesPerMole
                    : FitJoulesPerMole - OffsetJoulesPerMole;
                return molarHeat * Data.Injections[injectionindex].InjectionMass;
            }
        }
    }
}
