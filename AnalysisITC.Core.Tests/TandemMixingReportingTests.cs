using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using AnalysisITC.Core.Viewer;
using AnalysisITC.Core.Export;
using System.Text.Json.Nodes;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests;

/// <summary>
/// The report and the interpretation package state how a residual bootstrap treated tandem
/// back-mixing. Expected texts and values are written out here, not derived from the formatter.
/// </summary>
[Collection("Solver events")]
public sealed class TandemMixingReportingTests
{
    [Theory]
    [InlineData(false, 0, "Fixed fractions", "Not included")]
    [InlineData(true, 3, "Included in bootstrap", "Included")]
    [InlineData(true, 0, "Unavailable", "Not included")]
    public void BootstrapMixingTreatmentAppearsInInspectorAndReport(bool requested, int retained,
        string reportValue, string inspectorValue)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var result = Result(Tandem(automatic: true), ErrorEstimationMethod.BootstrapResiduals, requested, retained);

        var items = Details(AnalysisReportBuilder.Build(result)).ToList();

        var row = Assert.Single(items, item => item.Label == "Tandem mixing uncertainty");
        Assert.Equal(reportValue, row.Value);
        Assert.Equal("Tandem merge origin", items[items.IndexOf(row) - 1].Label);
        var inspectorRow = Assert.Single(UncertaintyEstimationPresentation.Section(result.Solution).Rows,
            item => item.Label == "Tandem mixing uncertainty");
        Assert.Equal(inspectorValue, inspectorRow.Value);
    }

    [Fact]
    public void FixedOrLegacyTandemBootstrapReportsFixedFractions()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var result = Result(Tandem(automatic: false), ErrorEstimationMethod.BootstrapResiduals);

        var row = Assert.Single(Details(AnalysisReportBuilder.Build(result)), item => item.Label == "Tandem mixing uncertainty");

        Assert.Equal("Fixed fractions", row.Value);
    }

    [Fact]
    public void ReportOmitsMixingRowWithoutBootstrapOrTandem()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var profile = Result(Tandem(automatic: true), ErrorEstimationMethod.ProfileLikelihood);
        var none = Result(Tandem(automatic: true), ErrorEstimationMethod.None);
        var ordinary = Result(Source("ordinary"), ErrorEstimationMethod.BootstrapResiduals);

        foreach (var result in new[] { profile, none, ordinary })
            Assert.DoesNotContain(Details(AnalysisReportBuilder.Build(result)), item => item.Label == "Tandem mixing uncertainty");
    }

    [Theory]
    [InlineData(false, 0, "fixed-fractions")]
    [InlineData(true, 3, "included")]
    [InlineData(true, 0, "unavailable")]
    public void InterpretationPackageCarriesSavedMixingAndBootstrapTreatment(bool requested, int retained, string expected)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var tandem = Tandem(automatic: true);
        var result = Result(tandem, ErrorEstimationMethod.BootstrapResiduals, requested, retained);

        var package = Package(result, tandem);
        var experiment = package.Result.Experiments.Single();

        Assert.Equal("model-free", experiment.TandemMixing.Criterion);
        Assert.Equal("monte-carlo-95", experiment.TandemMixing.LimitMethod);
        Assert.Equal(.95, experiment.TandemMixing.ConfidenceLevel);
        Assert.Equal(200, experiment.TandemMixing.MonteCarloDraws);
        var refill = Assert.Single(experiment.TandemMixing.Refills);
        Assert.Equal(1, refill.Refill);
        Assert.Equal(.17, refill.MixingFraction);
        Assert.Equal(0, refill.LowerLimit);
        Assert.Equal("bound-reached", refill.LowerStatus);
        Assert.Equal(.38, refill.UpperLimit);
        Assert.Equal("crossing", refill.UpperStatus);
        Assert.Equal(expected, experiment.Solver.TandemMixingInBootstrap);

        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        var model = JsonNode.Parse(prompt.ModelPackageJson);
        Assert.Contains("\"tandemMixing\"", prompt.ModelPackageJson);
        Assert.Contains("\"tandemMixingInBootstrap\":\"" + expected + "\"", prompt.ModelPackageJson);
        Assert.NotNull(model["modelInputDefinitions"]["tandemMixing"]);
    }

    [Fact]
    public void InterpretationPackageOmitsTreatmentWithoutBootstrap()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var tandem = Tandem(automatic: true);

        var experiment = Package(Result(tandem, ErrorEstimationMethod.None), tandem).Result.Experiments.Single();

        Assert.NotNull(experiment.TandemMixing);
        Assert.Null(experiment.Solver.TandemMixingInBootstrap);
    }

    [Fact]
    public void TandemsWithoutSavedMergeEvidenceKeepTheirInterpretationPackage()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var tandem = Tandem(automatic: false);
        var prompt = AnalysisInterpretationPromptBuilder.Build(
            Package(Result(tandem, ErrorEstimationMethod.BootstrapResiduals), tandem));

        Assert.DoesNotContain("tandemMixing", prompt.CanonicalPackageJson);
        Assert.DoesNotContain("tandemMixing", prompt.ModelPackageJson);
    }

    [Theory]
    [InlineData(false, 0, "Fixed fractions", false)]
    [InlineData(false, 0, "Fixed fractions", true)]
    [InlineData(true, 3, "Included in bootstrap", false)]
    [InlineData(true, 3, "Included in bootstrap", true)]
    [InlineData(true, 0, "Unavailable", false)]
    [InlineData(true, 0, "Unavailable", true)]
    public async Task ViewerProjectsSavedTandemDetailsAndFitTreatment(bool requested, int retained, string expected, bool embedded)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var tandem = Tandem(automatic: true);
        var result = Result(tandem, ErrorEstimationMethod.BootstrapResiduals, requested, retained);
        tandem.Model = result.Solution.Solutions.Single().Model;
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, new[] { tandem }, embedded ? Array.Empty<AnalysisResult>() : new[] { result });
        stream.Position = 0;

        var document = await new ViewerDocumentReader().ReadAsync(stream, "tandem.ftxtc", ViewerFileFormat.Ftxtc);

        var experiment = Assert.Single(document.Experiments);
        Assert.NotNull(experiment.TandemDetails);
        var refill = Assert.Single(experiment.TandemDetails.Refills);
        Assert.Equal("17%", refill.Fraction);
        Assert.Contains("38%", refill.Limits);
        Assert.NotEmpty(experiment.Fits);
        Assert.All(experiment.Fits, fit => Assert.Equal(expected, fit.TandemMixingStatus));
    }

    [Theory]
    [InlineData(false, 0, TandemMixingBootstrapStatus.FixedFractions)]
    [InlineData(true, 3, TandemMixingBootstrapStatus.Included)]
    [InlineData(true, 0, TandemMixingBootstrapStatus.Unavailable)]
    public void EmbeddedFitUsesItsSavedErrorMethod(bool requested, int retained, TandemMixingBootstrapStatus expected)
    {
        var result = Result(Tandem(true), ErrorEstimationMethod.BootstrapResiduals, requested, retained);
        var member = result.Solution.Solutions.Single();
        Assert.Equal(expected, TandemOriginPresentation.BootstrapStatus(null, member));
        member.ErrorMethod = ErrorEstimationMethod.None;
        Assert.Null(TandemOriginPresentation.BootstrapStatus(null, member));
    }

    static AnalysisInterpretationPackage Package(AnalysisResult result, ExperimentData tandem)
    {
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        return AnalysisInterpretationPackageBuilder.Build(report, _ => result,
            id => id == tandem.UniqueID ? tandem : null);
    }

    static IEnumerable<AnalysisReportKeyValueItem> Details(AnalysisReportDocument document) => document.Sections
        .SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
        .Where(block => block.Title == "Experiment details").SelectMany(block => block.Items);

    static ExperimentData Tandem(bool automatic)
    {
        var data = Source("tandem");
        data.TandemMergeDescription = "Recorded merge";
        data.AddSegment(new TandemExperimentSegment(0, data.CellConcentration, 0));
        data.AddSegment(new TandemExperimentSegment(2, data.CellConcentration, 0));
        if (automatic)
            data.TandemMergeInfo = new TandemMergeInfo(new[] { .17 }, TandemMixingCriterion.ModelFree,
                DilutionMethod.DiscreteDisplacement, 80e-6, true, 0, TandemContinuityScanner.PointsPerSide,
                new TandemMixingUncertainty(MixingUncertaintyMethod.MonteCarlo95,
                    new[] { new TandemMixingInterval(0, .38, MixingLimitStatus.BoundReached, MixingLimitStatus.Crossing) },
                    monteCarloDraws: 200));
        return data;
    }

    static ExperimentData Source(string id)
    {
        var data = new ExperimentData(id + ".itc")
        {
            CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 200e-6, TargetTemperature = 25, MeasuredTemperature = 25,
        };
        data.SetID(id);
        data.DataPoints.Add(new DataPoint(0, 0, 25)); data.DataPoints.Add(new DataPoint(120, 0, 25));
        data.BaseLineCorrectedDataPoints = data.DataPoints.ToList();
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 2e-10, include: true)
                { Time = 30 + index * 30, Ratio = index + 1, ActualCellConcentration = 10e-6, ActualTitrantConcentration = (index + 1) * 1e-6 };
            injection.SetPeakArea(new FloatWithError(-2e-6, 1e-8));
            injection.IsIntegrated = true;
            data.Injections.Add(injection);
        }
        return data;
    }

    static OneSetOfSites Fitted(ExperimentData data, double enthalpy, ModelCloneOptions options, SolverConvergence convergence)
    {
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, enthalpy);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.ModelCloneOptions = options;
        model.Solution = SolutionInterface.FromModel(model, convergence);
        model.Solution.ErrorMethod = options.ErrorEstimationMethod;
        return model;
    }

    static AnalysisResult Result(ExperimentData data, ErrorEstimationMethod method, bool requested = false, int retained = 0)
    {
        ModelCloneOptions Options() => new ModelCloneOptions { ErrorEstimationMethod = method, ReestimateTandemMixing = requested };
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            { Algorithm = SolverAlgorithm.LevenbergMarquardt, Termination = SolverTermination.Converged, Loss = 1 });
        var model = Fitted(data, -25_000, Options(), convergence);
        if (retained > 0)
            model.Solution.SetBootstrapSolutions(Enumerable.Range(0, retained)
                .Select(index => Fitted(data.GetSynthClone(new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None }, new Random(index)),
                    -25_000 - 100 * index, Options(), convergence).Solution).ToList());
        var globalModel = new GlobalModel(new List<Model> { model })
            { Parameters = new GlobalModelParameters(), ModelCloneOptions = Options() };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, new List<SolutionInterface> { model.Solution }, convergence,
            reconstructBootstrap: false);
        globalModel.Solution = global;
        return new AnalysisResult(global);
    }
}
