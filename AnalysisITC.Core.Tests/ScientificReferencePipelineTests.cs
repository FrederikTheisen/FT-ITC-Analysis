using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Processing;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection("Published model reproduction")]
public sealed class ScientificReferencePipelineTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ITestOutputHelper output;

    public ScientificReferencePipelineTests(ITestOutputHelper output)
    {
        this.output = output;
        PreferencesState.Defaults().ApplyToSettings();
    }

    public void Dispose() => original.ApplyToSettings();

    [Theory]
    [InlineData(BaselineInterpolatorTypes.Polynomial, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData(BaselineInterpolatorTypes.Polynomial, SolverAlgorithm.NelderMead)]
    [InlineData(BaselineInterpolatorTypes.Spline, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData(BaselineInterpolatorTypes.Spline, SolverAlgorithm.NelderMead)]
    [InlineData(BaselineInterpolatorTypes.Segmented, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData(BaselineInterpolatorTypes.Segmented, SolverAlgorithm.NelderMead)]
    public async Task AnalyticRawThermogramRecoversHeatsFitAndSavedProject(
        BaselineInterpolatorTypes baselineType, SolverAlgorithm algorithm)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ScientificValidation", "RawPipeline");
        var bytes = File.ReadAllBytes(Path.Combine(directory, "analytic-one-site.itc"));
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "reference.json")));
        Assert.Equal(expected.RootElement.GetProperty("raw_sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        using var raw = new MemoryStream(bytes);
        var experiment = MicroCalITC200Reader.ReadStream(raw, "analytic-one-site.itc");
        var references = expected.RootElement.GetProperty("injections").EnumerateArray().ToArray();
        Assert.Equal(32, experiment.Injections.Count);
        Assert.InRange(Math.Abs(experiment.CellVolume - 200e-6), 0, 2e-11);
        Assert.InRange(Math.Abs(experiment.CellConcentration.Value - 40e-6), 0, 1e-11);
        Assert.InRange(Math.Abs(experiment.SyringeConcentration.Value - 500e-6), 0, 1e-10);

        foreach (var injection in experiment.Injections)
        {
            var reference = references[injection.ID];
            Assert.Equal(reference.GetProperty("time_seconds").GetSingle(), injection.Time);
            Assert.InRange(Math.Abs(injection.ActualCellConcentration - reference.GetProperty("cell_molar").GetDouble()), 0, 1e-11);
            // The raw heat source uses the independently defined rational ligand balance.
            // This trajectory checks the current MicroCal concentration implementation.
            Assert.InRange(Math.Abs(injection.ActualTitrantConcentration
                - reference.GetProperty("implementation_ligand_molar").GetDouble()), 0, 1e-11);
            injection.SetIntegrationStartTime(0);
            injection.SetIntegrationLengthByTime(22);
            injection.Include = injection.ID != 0;
        }

        experiment.Processor.DiscardIntegratedPoints = true;
        experiment.Processor.InitializeBaseline(baselineType);
        if (experiment.Processor.Interpolator is PolynomialLeastSquaresInterpolator polynomial)
        {
            polynomial.Degree = 1;
            polynomial.ZLimit = 3;
        }
        if (experiment.Processor.Interpolator is SegmentedBaselineInterpolator segmented)
            segmented.Degree = 1;
        if (experiment.Processor.Interpolator is SplineInterpolator spline)
            spline.Algorithm = SplineInterpolator.SplineInterpolatorAlgorithm.Rigid;
        await experiment.Processor.ProcessData(showProgress: false);

        Assert.True(experiment.Processor.BaselineCompleted);
        Assert.True(experiment.Processor.IntegrationCompleted);
        var heatErrors = experiment.Injections.Select(injection => Math.Abs(injection.RawPeakArea.Value
            - references[injection.ID].GetProperty("heat_joules").GetDouble())).ToArray();
        // 0.2 nJ allows float-valued power/concentration import rounding. It is
        // much smaller than any injection heat in the independently fixed file.
        Assert.All(heatErrors, error => Assert.InRange(error, 0, 2e-10));

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, .85);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 5.7);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -24000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        experiment.Model = model;
        var convergence = new Solver
        {
            Model = model, SolverAlgorithm = algorithm,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false, MaxOptimizerIterations = 20000, Silent = true,
        }.Solve();
        Assert.True(convergence.Success, convergence.Message);
        AssertFit(model, expected.RootElement.GetProperty("implementation_fit_regression"));
        output.WriteLine($"{baselineType}/{algorithm}: maximum heat error={heatErrors.Max():G10} J; " +
            string.Join(", ", model.Parameters.Table.Select(item => $"{item.Key}={item.Value.Value:G12}")));

        using var project = new MemoryStream();
        await FTXTCWriter.WriteStream(project, new[] { experiment }, Array.Empty<AnalysisResult>());
        project.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(project)).OfType<ExperimentData>());
        Assert.True(restored.Processor.BaselineCompleted);
        Assert.True(restored.Processor.IntegrationCompleted);
        Assert.Equal(experiment.Injections.Select(i => i.RawPeakArea.Value), restored.Injections.Select(i => i.RawPeakArea.Value));
        AssertFit(Assert.IsType<OneSetOfSites>(restored.Model), expected.RootElement.GetProperty("implementation_fit_regression"));
    }

    static void AssertFit(OneSetOfSites model, JsonElement expected)
    {
        // These are implementation regression values from the current MicroCal
        // concentration curve and fit pipeline, not independent parameter truth.
        Assert.InRange(Math.Abs(model.Parameters.Table[ParameterType.Nvalue1].Value
            - expected.GetProperty("n").GetDouble()), 0, 2e-6);
        Assert.InRange(Math.Abs(model.Parameters.Table[ParameterType.Affinity1].Value
            - expected.GetProperty("log10_ka").GetDouble()), 0, 3e-6);
        Assert.InRange(Math.Abs(model.Parameters.Table[ParameterType.Enthalpy1].Value
            - expected.GetProperty("enthalpy_joules_per_mole").GetDouble()), 0, .01);
        Assert.InRange(Math.Abs(model.Parameters.Table[ParameterType.Offset].Value
            - expected.GetProperty("offset_joules_per_mole").GetDouble()), 0, .01);
    }
}
