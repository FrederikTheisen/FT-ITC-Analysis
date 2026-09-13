using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Buffer = AnalysisITC.Core.Data.Buffer;

using Xunit;

namespace AnalysisITC.Core.Tests;

/// <summary>
/// Independent reference checks for the advertised derived analyses.  The
/// expected values in this file are source equations or synthetic targets,
/// rather than values copied from the implementation under test.
/// </summary>
[Collection("Published model reproduction")]
public sealed class DerivedScientificReferenceTests
{
    [Fact]
    public void SpolarEntropyContributionConvertsCelsiusToKelvin()
    {
        var output = new FTSRMethod.SROutput(
            new FloatWithError(-2.0), new FloatWithError(3.0),
            new FloatWithError(1.0), new FloatWithError(25.0));

        // ΔH = −T·ΔS, with the public API accepting °C and thermodynamics
        // requiring kelvin. This is an independent unit target for the
        // temperature-derived presentation quantity.
        Assert.Equal(2.0 * (273.15 + 37.0), output.HydrationContribution(37).Value, 12);
        Assert.Equal(-3.0 * (273.15 + 37.0), output.ConformationalContribution(37).Value, 12);
    }

    [Fact]
    public void TemperatureDependenceFitsEnthalpyHeatCapacityAndAffinityInPhysicalUnits()
    {
        var result = LoadSyntheticTemperatureResult();
        var temperature = result.Solution.TemperatureDependence[ParameterType.Enthalpy1];
        var entropy = result.Solution.TemperatureDependence[ParameterType.EntropyContribution1];
        var gibbs = result.Solution.TemperatureDependence[ParameterType.Gibbs1];

        // Independent target: ΔH(25 °C) = −9 kJ/mol and ΔCp = +100
        // J/mol/K. The member affinities are all log10(K/M) = 6, hence Kd is
        // 1 µM at every temperature. The fitted values are derived from the
        // three original member solutions, not from bootstrap summaries.
        Assert.Equal(-9_000.0, temperature.Intercept.Value, 9);
        Assert.Equal(100.0, temperature.Slope.Value, 9);
        Assert.Equal(25.0, temperature.ReferenceT, 12);
        Assert.Equal(-8.31446261815324 * Math.Log(1_000_000.0), gibbs.Slope.Value, 2);
        Assert.Equal(gibbs.Slope.Value - temperature.Slope.Value, entropy.Slope.Value, 2);

        var evaluatedEnthalpy = temperature.Evaluate(35).Value;
        Assert.Equal(-8_000.0, evaluatedEnthalpy, 8);
        Assert.Equal(1.0e-6, result.Solution.Solutions[2].ReportParameters[ParameterType.Affinity1].Value, 6);

        var spolar = new FTSRMethod(result);
        spolar.TempMode = FTSRMethod.SRTempMode.MeanTemperature;
        Assert.Equal(25.0, spolar.EvalutationTemperature(sample: false), 12);
    }

    [Fact]
    public void IonicStrengthDependenceRecoversKnownDebyeHuckelParametersAndUnits()
    {
        const double kd0 = 2.0e-6;
        const double sensitivity = -1.25;
        var ionicStrengths = new[] { 0.0, 0.01, 0.04, 0.09 };
        var kds = ionicStrengths
            .Select(ionicStrength => kd0 * Math.Exp(sensitivity * Math.Sqrt(ionicStrength)))
            .ToArray();

        var fitted = IonicStrengthDependence.FitIonicStrengthDependence(ionicStrengths, kds);

        Assert.NotNull(fitted);
        Assert.Equal(kd0, fitted.Kd0, 11);
        Assert.Equal(sensitivity, fitted.SaltSensitivity, 11);
        Assert.False(fitted.UsesCurvature);
        Assert.Equal(0.0, fitted.Curvature, 12);

        // The report/viewer evaluator receives sqrt(I) and returns log10(Kd).
        var reportFit = new IonicStrengthDependenceFit(
            new FloatWithError(kd0), new FloatWithError(sensitivity), new FloatWithError(0));
        var sqrtI = Math.Sqrt(0.04);
        var expectedLog10Kd = Math.Log10(kd0 * Math.Exp(sensitivity * sqrtI));
        Assert.Equal(expectedLog10Kd, reportFit.Evaluate(sqrtI).Value, 12);
    }

    [Fact]
    public void IonicStrengthDisplayEvaluatorPreservesOptionalCurvatureTerm()
    {
        const double kd0 = 1.0e-6;
        const double sensitivity = 2.0;
        const double curvature = -3.0;
        var fit = new IonicStrengthDependenceFit(
            new FloatWithError(kd0), new FloatWithError(sensitivity),
            new FloatWithError(curvature), usesCurvature: true);
        var sqrtI = 0.2;
        var expected = Math.Log10(kd0 * Math.Exp(sensitivity * sqrtI + curvature * sqrtI * sqrtI));

        Assert.True(fit.UsesCurvature);
        Assert.Equal(expected, fit.Evaluate(sqrtI).Value, 12);
    }

    [Fact]
    public void CounterIonReleaseRouteMapsSaltActivityAndAffinityInLogSpace()
    {
        var result = LoadSyntheticTemperatureResult();
        for (var index = 0; index < result.Solution.Model.Models.Count; index++)
        {
            var model = result.Solution.Model.Models[index];
            var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
            salt.IntValue = (int)Salt.NaCl;
            salt.ParameterValue = new FloatWithError(0.01 * (index + 1));
            model.Data.Attributes.Add(salt);
        }

        var points = new ElectrostaticsAnalysis(result)
            .GetDataPoints(ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease);

        Assert.Equal(result.Solution.Solutions.Count, points.Count);
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var concentration = 0.01 * (index + 1);
            // NaCl contributes one cation and one anion, so this route's
            // concentration-power proxy is c² before taking ln(activity).
            var expectedActivity = concentration * concentration;
            Assert.True(double.IsFinite(point.Item1));
            Assert.Equal(Math.Log(expectedActivity), point.Item1, 12);
            Assert.Equal(Math.Log(1.0e-6), point.Item2.Value, 12);
        }
    }

    [Fact]
    public async Task ProtonationDependenceUsesBindingEnthalpyPlusProtonationChangeTimesBufferEnthalpy()
    {
        var analysis = new ProtonationAnalysis(LoadResultWithBuffers());
        analysis.DataPoints = new List<Tuple<double, FloatWithError>>
        {
            // Typical buffer enthalpy magnitudes in J/mol. The linear
            // regression must not require rescaling these to kJ/mol.
            Tuple.Create(10_000.0, new FloatWithError(4_000.0)),
            Tuple.Create(20_000.0, new FloatWithError(7_000.0)),
            Tuple.Create(30_000.0, new FloatWithError(10_000.0)),
        };

        var previousIterations = ResultAnalysisController.CalculationIterations;
        try
        {
            ResultAnalysisController.CalculationIterations = 1;
            Assert.True(await analysis.PerformAnalysisAsync());
        }
        finally
        {
            ResultAnalysisController.CalculationIterations = previousIterations;
        }

        // Synthetic target: observed dH is 1000 J/mol + 0.3 times buffer
        // protonation enthalpy.  Both slope and intercept are independent of
        // the registry constants and of any bootstrap distribution.
        Assert.Equal(1_000.0, analysis.BindingEnthalpy.Value, 9);
        Assert.Equal(0.3, analysis.ProtonationChange.Value, 12);
        Assert.Equal(1, analysis.CompletedIterations);

        // A constant buffer-enthalpy coordinate cannot determine a slope.
        // Reject that run and retain the previously committed valid result.
        analysis.DataPoints = analysis.DataPoints.Select(point =>
            Tuple.Create(20_000.0, point.Item2)).ToList();
        Assert.False(await analysis.PerformAnalysisAsync());
        Assert.Equal(1_000.0, analysis.BindingEnthalpy.Value, 9);
        Assert.Equal(0.3, analysis.ProtonationChange.Value, 12);
    }

    [Fact]
    public void TapsoConstantsMatchVisuallyVerifiedNistTableAndLocalTemperatureTrend()
    {
        var properties = Buffer.TAPSO.GetProperties();
        // NIST Table 7.62 (printed p.346), independently transcribed from
        // the page image: pKa(20 °C)=7.7479, pKa(30 °C)=7.5244.
        // A local linear approximation is checked only across this window.
        var slopeFromTable = (7.5244 - 7.7479) / 10.0;
        Assert.Equal(7.635, properties.pKaValues[0], 12);
        Assert.InRange(Math.Abs(properties.dPKadT[0] - slopeFromTable), 0, 0.0001);
        Assert.InRange(Math.Abs(properties.pKaValues[0] - 5 * properties.dPKadT[0] - 7.7479), 0, 0.002);
        Assert.InRange(Math.Abs(properties.pKaValues[0] + 5 * properties.dPKadT[0] - 7.5244), 0, 0.002);
        Assert.Equal(39_090.0, properties.ProtonationEnthalpy.Evaluate(25), 6);
        // PDF text extraction renders '= −16' as '5216'. The page image
        // confirms −16 J/(mol K); this existing value must be preserved.
        Assert.Equal(-16.0, properties.ProtonationEnthalpy.Slope, 6);
    }

    [Fact]
    public void ImidazoleIonizationHeatCapacityUsesJoulesPerMoleKelvin()
    {
        var properties = Buffer.Imidazole.GetProperties();
        // NIST Table 7.41 (printed p.302): 36.64 kJ/mol at 298.15 K,
        // with ΔCp = −9 J/(mol K), not −0.009 J/(mol K).
        Assert.Equal(36_640.0, properties.ProtonationEnthalpy.Evaluate(25), 8);
        Assert.Equal(-9.0, properties.ProtonationEnthalpy.Slope, 12);
        Assert.Equal(36_550.0, properties.ProtonationEnthalpy.Evaluate(35), 8);
    }

    [Fact]
    public void TapsoZwitterionUsesNeutralProtonatedChargeInIonicStrengthCalculation()
    {
        var previous = AppSettings.IncludeBufferInIonicStrengthCalc;
        try
        {
            AppSettings.IncludeBufferInIonicStrengthCalc = true;
            var data = new ExperimentData("TAPSO reference")
            {
                TargetTemperature = 25,
            };
            var buffer = ExperimentAttribute.FromKey(AttributeKey.Buffer);
            buffer.IntValue = (int)Buffer.TAPSO;
            buffer.DoubleValue = 8.0;
            buffer.ParameterValue = new FloatWithError(0.1);
            data.Attributes.Add(buffer);

            // This is the independently calculated result from the NIST pKa,
            // the source Debye-Huckel correction used by the ionic-strength
            // model, and a neutral zwitterionic protonated TAPSO species.
            var ionicStrength = BufferAttribute.GetIonicStrength(data);
            Assert.InRange(ionicStrength, 0.073, 0.076);
        }
        finally
        {
            AppSettings.IncludeBufferInIonicStrengthCalc = previous;
        }
    }

    static AnalysisResult LoadResultWithBuffers()
    {
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc"));
        var containers = FTITCReader.ReadStream(source).GetAwaiter().GetResult();
        var result = containers.OfType<AnalysisResult>().Single();
        foreach (var solution in result.Solution.Solutions)
        {
            var buffer = ExperimentAttribute.FromKey(AttributeKey.Buffer);
            buffer.IntValue = (int)Buffer.Hepes;
            buffer.DoubleValue = 7.4;
            buffer.ParameterValue = new FloatWithError(0.05);
            solution.Data.Attributes.Add(buffer);
        }
        return result;
    }

    static AnalysisResult LoadSyntheticTemperatureResult()
    {
        var temperatures = new[] { 5.0, 25.0, 45.0 };
        var models = temperatures.Select((temperature, index) =>
        {
            var data = new ExperimentData($"temperature-reference-{index}.itc")
            {
                Name = $"temperature-reference-{index}",
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = temperature,
                TargetTemperature = temperature,
            };
            for (var injectionIndex = 0; injectionIndex < 2; injectionIndex++)
            {
                var injection = new InjectionData(data, injectionIndex, 2e-6, 2e-10, include: true)
                {
                    ActualCellConcentration = 10e-6,
                    ActualTitrantConcentration = (injectionIndex + 1) * 2e-6,
                    Ratio = injectionIndex + 1,
                };
                injection.SetPeakArea(new FloatWithError(-2e-6 * (injectionIndex + 1), 1e-8));
                data.Injections.Add(injection);
            }

            var model = new OneSetOfSites(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(
                ParameterType.Enthalpy1, -9_000.0 + 100.0 * (temperature - 25.0));
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6.0);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            return model;
        }).Cast<Model>().ToList();

        var globalModel = new GlobalModel(models)
        {
            ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
        };
        foreach (var model in models)
            globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        globalModel.Parameters.SetConstraintForParameter(
            ParameterType.Enthalpy1, VariableConstraint.TemperatureDependent);
        globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, -9_000.0);
        globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.HeatCapacity1, 100.0);
        globalModel.Parameters.SetConstraintForParameter(
            ParameterType.Affinity1, VariableConstraint.SameForAll);
        globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity1, 6.0);

        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        var globalSolver = new GlobalSolver
        {
            Model = globalModel,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
        };
        var globalSolution = new GlobalSolution(globalSolver, convergence);
        globalModel.Solution = globalSolution;
        return new AnalysisResult(globalSolution);
    }
}
