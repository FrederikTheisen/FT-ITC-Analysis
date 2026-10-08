using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class NullModelContractTests
{
    [Theory]
    [InlineData("", "Null (model unknown)")]
    [InlineData("unregistered", "Null (model unknown)")]
    [InlineData("offset", "Null (Offset)")]
    [InlineData("one-set-of-sites", "Null (One-Set-Of-Sites)")]
    public void LabelsFollowSavedIdentity(string id, string label)
    {
        var comparison = new NullModelComparison { NullModelId = id, NullFitSucceeded = true };
        Assert.Equal(label, NullModelComparisonPresentation.NullModel(comparison));
        Assert.Equal(label + ", per experiment", NullModelComparisonPresentation.NullModel(comparison, true));
        comparison.NullFitSucceeded = false;
        Assert.Equal(label + " (failed)", NullModelComparisonPresentation.NullModel(comparison));
        Assert.Equal("The Null model fit failed.", NullModelComparisonPresentation.NullFitReason(comparison));
        comparison.NullFitReason = "Saved detailed reason";
        Assert.Equal("Saved detailed reason", NullModelComparisonPresentation.NullFitReason(comparison));
        Assert.Equal("", new NullModelComparison().NullModelId);
        Assert.Equal("Null (not calculated)", NullModelComparisonPresentation.NullModel(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullRoleExcludesComparisonRegardlessOfModelType(bool global)
    {
        var data = InjectionProcessingMethodTests.FittedModel(bootstrap: false).Data;
        var model = new RoleModel(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromFixedFit(0, 0));
        Assert.Equal(AnalysisModel.OneSetOfSites, model.ModelType);
        if (global)
        {
            var models = new GlobalModel(new List<Model> { model }) { Parameters = new GlobalModelParameters() };
            models.Parameters.AddIndivdualParameter(model.Parameters);
            var solution = new GlobalSolution(new GlobalSolver { Model = models }, new List<SolutionInterface> { model.Solution },
                model.Solution.Convergence, reconstructBootstrap: false);
            NullModelComparisonCalculator.Calculate(solution, false, SolverAlgorithm.LevenbergMarquardt, 100, 1);
            Assert.Null(solution.NullComparison);
        }
        else
        {
            NullModelComparisonCalculator.Calculate(model.Solution, false, SolverAlgorithm.LevenbergMarquardt, 100, 1);
            Assert.Null(model.Solution.NullComparison);
        }
        Assert.True(new Offset(data).IsNullModel);
        Assert.False(new OneSetOfSites(data).IsNullModel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FigureUsesSavedVaryingPredictionsAndIgnoresCorrection(bool corrected)
    {
        var result = Result(out var member);
        var comparison = Comparison(member, AnalysisModel.Offset);
        // The obsolete scalar deliberately disagrees with the saved solution.
        comparison.Members[0].Offset = -99999;
        SetComparison(result, comparison);
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        var figure = PublicationFigureBuilder.Build(new PublicationFigureSource(member.Data, member, result),
            new PublicationFigureOptions { EnergyUnitOverride = EnergyUnit.Joule, DrawFitOffsetCorrected = corrected,
                DisplayParameters = FinalFigureDisplayParameters.Model });
        var predictions = figure.FitPanel.Series.Single().Points;
        for (var i = 0; i < member.Data.Injections.Count; i++)
        {
            // Presentation contract, specified independently of the test model.
            var injection = member.Data.Injections[i];
            var expectedMolarPrediction = 1500 + 125 * injection.ID;
            Assert.Equal(expectedMolarPrediction, predictions[i].Y, 9);
            Assert.Equal(injection.PeakArea.Value / injection.InjectionMass, figure.FitPanel.Points[i].Y, 8);
            Assert.Equal(injection.PeakArea.Value / injection.InjectionMass - expectedMolarPrediction,
                figure.ResidualPanel.Points[i].Y, 8);
        }
        Assert.StartsWith("Null (Offset)", figure.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines).First());
        Assert.Same(comparison.NullSolutions[0], Resolve(result, member));
    }

    [Theory]
    [InlineData(AnalysisModel.Offset, "offset", true)]
    [InlineData(AnalysisModel.OneSetOfSites, "one-set-of-sites", false)]
    public void NamedAiParametersCarryNullIdentityUnitsConstraintsAndNoInventedUncertainty(
        AnalysisModel type, string id, bool legacyOffset)
    {
        var result = Result(out var member);
        var comparison = Comparison(member, type);
        SetComparison(result, comparison);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        var evidence = Assert.Single(package.Result.BindingAssessment.NullMembers);
        Assert.Equal(id, evidence.NullModelId);
        Assert.Equal(legacyOffset, evidence.OffsetJoulesPerMole.HasValue);
        var offset = Assert.Single(evidence.NullParameters, value => value.Name == "Offset");
        Assert.Equal(1500, offset.BestFitValue);
        Assert.Equal("J/mol", offset.SiUnit);
        Assert.Equal("None", offset.Constraint);
        Assert.Equal(-20000, offset.FittedLowerBound);
        Assert.Null(offset.StandardDeviation);
        Assert.Null(offset.Confidence95Lower);
        Assert.Null(offset.Confidence95Upper);
        Assert.Null(offset.UncertaintyMethod);
        var fraction = Assert.Single(evidence.NullParameters, value => value.QuantityId == "stoichiometry-1");
        Assert.Equal(2, fraction.BestFitValue);
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        using var compact = JsonDocument.Parse(prompt.ModelPackageJson);
        var compactMember = compact.RootElement.GetProperty("results")[0].GetProperty("bindingAssessment").GetProperty("nullMembers")[0];
        Assert.Equal(id, compactMember.GetProperty("nullModelId").GetString());
        Assert.Equal(2, compactMember.GetProperty("nullParameters").GetArrayLength());
        var compactOffset = compactMember.GetProperty("nullParameters")[0];
        Assert.False(compactOffset.TryGetProperty("standardDeviation", out _));
        Assert.Equal("2.2", package.PackageSchemaVersion);
        var text = NullModelComparisonPresentation.FormatNullParameters(result, comparison, EnergyUnitFamily.Joules, EnergyUnit.KiloJoule);
        Assert.Contains("Offset = " + 1.5.ToString(System.Globalization.CultureInfo.CurrentCulture) + " kJ/mol", text);
        Assert.Contains(", ", text);
        var export = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            FileFormat = AnalysisResultExportFileFormat.TSV, RowMode = AnalysisResultExportRowMode.AllRows,
            EnergyUnitOverride = EnergyUnit.KiloJoule,
        });
        Assert.Contains("Null model parameters", export);
        Assert.Contains(text, export);
        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        {
            OutputPurpose = ResultOutputPurpose.Diagnostic, EnergyUnitOverride = EnergyUnit.KiloJoule,
        });
        var reportParameters = Assert.Single(document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .SelectMany(block => block.Items), item => item.Label == "Null model parameters");
        // Reports deliberately use invariant numeric culture.
        Assert.Contains("Offset = 1.5 kJ/mol", reportParameters.Value);
        Assert.Contains("N-value = 2", reportParameters.Value);
        comparison.NullSolutions.Clear();
        Assert.Equal(legacyOffset, NullModelComparisonPresentation.FormatNullParameters(result, comparison,
            EnergyUnitFamily.Joules).Contains("Offset = "));
    }

    [Fact]
    public void IndependentAssessmentAlsoCarriesNamedNullEvidenceAndComputedUncertainty()
    {
        var result = IndependentAssessmentTests.CreateIndependentResult(out var members);
        foreach (var member in members)
        {
            var comparison = Comparison(member, AnalysisModel.Offset);
            var solution = comparison.NullSolutions.Single();
            solution.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
            solution.BootstrapSolutions.Add(solution);
            solution.Parameters[ParameterType.Offset] = new FloatWithError(1500, 12);
            result.RestoreMemberComparisonAndAssessment(member.Guid, comparison, BindingAssessmentState.FromComparison(comparison));
        }
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.All(package.Result.BindingAssessment.Members, evidence =>
        {
            Assert.Equal("offset", evidence.NullModelId);
            Assert.Equal("Null (Offset)", evidence.NullModel);
            Assert.Equal(12, evidence.NullParameters.Single(parameter => parameter.Name == "Offset").StandardDeviation);
            Assert.Null(evidence.NullParameters.Single(parameter => parameter.QuantityId == "stoichiometry-1").StandardDeviation);
        });
    }

    [Theory]
    [InlineData("single")]
    [InlineData("pooled")]
    [InlineData("independent")]
    public async Task CalculatorIdentitySurvivesReopenSaveReopen(string scope)
    {
        AnalysisResult result;
        if (scope == "single") result = Result(out _);
        else
        {
            result = IndependentAssessmentTests.CreateIndependentResult(out _);
            if (scope == "pooled")
            {
                result.Solution.Model.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, VariableConstraint.SameForAll);
                result.Solution.Model.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, -40000);
            }
        }
        NullModelComparisonCalculator.Calculate(result.Solution, false, SolverAlgorithm.LevenbergMarquardt, 3000, 1);
        Assert.Equal("offset", result.Solution.NullComparison.NullModelId);
        result = new AnalysisResult(result.Solution);
        for (var pass = 0; pass < 2; pass++)
        {
            using var stream = new MemoryStream();
            await FTXTCWriter.WriteStream(stream, result.Solution.Solutions.Select(value => value.Data), new[] { result });
            stream.Position = 0;
            result = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>());
            Assert.Equal("offset", result.Solution.NullComparison.NullModelId);
            Assert.All(result.MemberAssessments, member => Assert.Equal("offset", member.Comparison.NullModelId));
            Assert.True(result.Solution.NullComparison.NullFitSucceeded, result.Solution.NullComparison.NullFitReason);
            var report = new AnalysisReport();
            report.SetResultIds(new[] { result.UniqueID });
            var evidence = AnalysisInterpretationPackageBuilder.Build(report, result).Result.BindingAssessment;
            Assert.All(evidence.NullMembers.SelectMany(member => member.NullParameters), parameter =>
            {
                Assert.Null(parameter.StandardDeviation);
                Assert.Null(parameter.Confidence95Lower);
                Assert.Null(parameter.UncertaintyMethod);
            });
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task EmptyIdentitySavesReadableStatusOnlyEvidence(string id)
    {
        var result = Result(out var member);
        var comparison = Comparison(member, AnalysisModel.Offset);
        comparison.NullModelId = id;
        SetComparison(result, comparison);
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, new[] { member.Data }, new[] { result });
        stream.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>());
        Assert.NotNull(restored.NullComparison);
        Assert.Equal("offset", restored.NullComparison.NullModelId);
        Assert.False(restored.NullComparison.NullFitSucceeded);
        Assert.Equal("Saved Null model identity was missing.", restored.NullComparison.NullFitReason);
        Assert.Empty(restored.NullComparison.Members);
    }

    static AnalysisResult Result(out SolutionInterface member)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        member = result.Solution.Solutions[0];
        return result;
    }

    static NullModelComparison Comparison(SolutionInterface member, AnalysisModel type)
    {
        var data = member.Data.GetSynthClone(ModelCloneOptions.DefaultOptions, new Random(37));
        data.SetID(member.Data.UniqueID);
        var model = new VaryingModel(data, type);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 1500);
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { -20000d, 20000d });
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 2);
        var captured = SolutionInterface.FromModel(model, SolverConvergence.FromFixedFit(0, 0));
        model.Solution = new NamedSolution(model);
        foreach (var item in captured.Parameters) model.Solution.Parameters.Add(item.Key, item.Value);
        typeof(SolutionInterface).GetProperty(nameof(SolutionInterface.Convergence)).GetSetMethod(true)
            .Invoke(model.Solution, new object[] { captured.Convergence });
        return new NullModelComparison
        {
            NullModelId = type == AnalysisModel.Offset ? "offset" : "one-set-of-sites",
            BindingFitSucceeded = true, NullFitSucceeded = true,
            Members = new() { new() { ExperimentId = data.UniqueID, Offset = 1500, Scope = "local" } },
            NullSolutions = new() { model.Solution },
        };
    }

    static void SetComparison(AnalysisResult result, NullModelComparison comparison) => result.RestoreNullComparison(comparison);
    static SolutionInterface Resolve(AnalysisResult result, SolutionInterface member)
    {
        Assert.True(PublicationFigureBuilder.TryResolveNullFit(new PublicationFigureSource(member.Data, member, result), out var solution));
        return solution;
    }

    sealed class NamedSolution : SolutionInterface
    {
        public NamedSolution(Model model) { Model = model; BootstrapSolutions = new List<SolutionInterface>(); }
        public override Dictionary<ParameterType, FloatWithError> ReportParameters => new(Parameters);
    }

    sealed class RoleModel : Model
    {
        public RoleModel(ExperimentData data) : base(data) { }
        public override bool IsNullModel => true;
    }

    sealed class VaryingModel : Model
    {
        readonly AnalysisModel type;
        public VaryingModel(ExperimentData data, AnalysisModel type) : base(data) { this.type = type; }
        public override AnalysisModel ModelType => type;
        public override double Evaluate(int index, bool withoffset = true)
            => (1500 + 125 * index) * Data.Injections.Single(injection => injection.ID == index).InjectionMass;
    }
}
