using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

/// <summary>
/// ITC-169: buffer-subtraction changes alter a target's corrected heats, so they must go through the
/// target processing lifecycle and invalidate every solution referencing the target. Expected heats are
/// computed from the constructed raw and blank heats, not from the subtraction calculator.
/// </summary>
[Collection(DataManagerUndoOrderCollectionDefinition.Name)]
public sealed class BufferSubtractionProcessingInvalidationTests : IDisposable
{
    const double HeatSd = 1e-8;
    static readonly double[] TargetHeats = { -10e-6, -9e-6, -7e-6, -4e-6, -2e-6 };
    // Not linear, so matched and fitted subtraction differ.
    static readonly double[] BlankHeats = { 2e-7, 3e-7, 5e-7, 4e-7, 3.5e-7 };
    static readonly double[] OtherBlankHeats = { 1e-7, 1.5e-7, 1e-7, 1.5e-7, 1e-7 };

    public BufferSubtractionProcessingInvalidationTests()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
    }

    public void Dispose() => DataManager.Clear(DataClearMode.ResetSession);

    [Theory]
    [InlineData("reference-heat")]
    [InlineData("reference-include")]
    [InlineData("below-snapshot-tolerance")]
    [InlineData("apply")]
    [InlineData("change-reference")]
    [InlineData("change-method")]
    [InlineData("clear")]
    [InlineData("stored-reference")]
    [InlineData("copy-attributes")]
    [InlineData("clear-attributes")]
    public void CorrectedHeatChangeInvalidatesAttachedAndSavedSolutionsOnce(string change)
    {
        var f = Setup(subtracted: change != "apply");
        double[] expected;

        switch (change)
        {
            case "reference-heat":
                f.Reference.Injections[2].SetPeakArea(new FloatWithError(9e-7, HeatSd));
                f.Reference.UpdateProcessing();
                expected = Subtract(TargetHeats, BlankHeats.Select((heat, i) => i == 2 ? 9e-7 : heat));
                break;
            case "reference-include":
                Assert.True(f.Reference.Injections[2].TryToggleDataPointActive());
                // Matched subtraction averages the included neighbours of an excluded buffer injection.
                expected = Subtract(TargetHeats, BlankHeats.Select((heat, i) => i == 2 ? (BlankHeats[1] + BlankHeats[3]) / 2 : heat));
                break;
            case "below-snapshot-tolerance":
                // 1e-13 J is far below the saved-result comparison tolerance (~1e-9 at this scale).
                f.Reference.Injections[2].SetPeakArea(new FloatWithError(BlankHeats[2] + 1e-13, HeatSd));
                f.Reference.UpdateProcessing();
                expected = Subtract(TargetHeats, BlankHeats.Select((heat, i) => i == 2 ? heat + 1e-13 : heat));
                break;
            case "apply":
                f.Target.SetBufferSubtraction(f.Reference, BufferSubtractionMethod.MatchedInjection);
                expected = Subtract(TargetHeats, BlankHeats);
                break;
            case "change-reference":
                f.Target.SetBufferSubtraction(f.OtherReference, BufferSubtractionMethod.MatchedInjection);
                expected = Subtract(TargetHeats, OtherBlankHeats);
                break;
            case "change-method":
                f.Target.SetBufferSubtraction(f.Reference, BufferSubtractionMethod.Linear);
                expected = Subtract(TargetHeats, LeastSquaresLine(BlankHeats));
                break;
            case "clear":
                f.Target.ClearBufferSubtraction();
                expected = TargetHeats;
                break;
            case "stored-reference":
                // Reference not loaded: its stored values define the correction.
                var stored = new BufferSubtractionSettings("unloaded-blank", BufferSubtractionMethod.MatchedInjection,
                    BufferSubtractionReferenceSnapshot.Capture(f.OtherReference)).ToAttribute();
                f.Target.AddOrUpdateAttribute(stored);
                expected = Subtract(TargetHeats, OtherBlankHeats);
                break;
            case "copy-attributes":
                var other = new BufferSubtractionSettings(f.OtherReference.UniqueID, BufferSubtractionMethod.MatchedInjection).ToAttribute();
                Assert.True(f.Target.CopyAttributesFrom(new[] { other }, clear: true));
                expected = Subtract(TargetHeats, OtherBlankHeats);
                break;
            case "clear-attributes":
                Assert.True(f.Target.ClearAttributes());
                expected = TargetHeats;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        AssertHeats(f.Target, expected);
        Assert.Equal(f.Revision + 1, f.Target.ProcessingRevision);
        Assert.Equal(1, f.ProcessingUpdated);
        Assert.Equal(1, f.SolutionChanged);
        AssertInvalidatedButRetained(f);

        if (change == "below-snapshot-tolerance")
            Assert.True(f.Result.IsValidForCurrentData); // On-demand comparison keeps its tolerance.
    }

    [Theory]
    [InlineData("reapply")]
    [InlineData("apply-options")]
    [InlineData("reference-processing-unchanged")]
    [InlineData("copy-same-attributes")]
    [InlineData("detail-attributes-unchanged")]
    public void UnchangedCorrectionLeavesProcessingAndSolutionsAlone(string change)
    {
        var f = Setup(subtracted: true);
        DocumentDirtyTracker.MarkClean();

        switch (change)
        {
            case "reapply":
                Assert.False(f.Target.SetBufferSubtraction(f.Reference, BufferSubtractionMethod.MatchedInjection));
                break;
            case "apply-options":
                DataManager.ApplyOptions();
                break;
            case "reference-processing-unchanged":
                f.Reference.UpdateProcessing();
                break;
            case "copy-same-attributes":
                f.Target.CopyAttributesFrom(f.Target.Attributes.Select(attribute => attribute.Copy()).ToList(), clear: true);
                break;
            case "detail-attributes-unchanged":
                Assert.False(f.Target.UpdateDetailAttributes(f.Target.Attributes.Select(attribute => attribute.Copy()).ToList()));
                break;
        }

        AssertHeats(f.Target, Subtract(TargetHeats, BlankHeats));
        Assert.Equal(f.Revision, f.Target.ProcessingRevision);
        Assert.Equal(0, f.ProcessingUpdated);
        Assert.Equal(0, f.SolutionChanged);
        Assert.True(f.Attached.IsValid);
        Assert.True(f.Saved.IsValid);
        Assert.True(f.Result.Solution.IsValid);
    }

    [Fact]
    public void DetailEditorBufferChangeIsUpdatedOnceByTheEditor()
    {
        var f = Setup(subtracted: true);
        var staged = new[] { new BufferSubtractionSettings(f.OtherReference.UniqueID, BufferSubtractionMethod.MatchedInjection).ToAttribute() };

        // Both details editors call UpdateProcessing whenever UpdateDetailAttributes reports a change.
        Assert.True(f.Target.UpdateDetailAttributes(staged));
        Assert.Equal(0, f.ProcessingUpdated);
        f.Target.UpdateProcessing();

        AssertHeats(f.Target, Subtract(TargetHeats, OtherBlankHeats));
        Assert.Equal(f.Revision + 1, f.Target.ProcessingRevision);
        Assert.Equal(1, f.ProcessingUpdated);
        Assert.Equal(1, f.SolutionChanged);
        AssertInvalidatedButRetained(f);
    }

    [Fact]
    public void ReattachingLoadedCorrectionUpdatesHeatsWithoutInvalidatingSavedFits()
    {
        // Legacy project loading restores the attribute and fits before the reference is available,
        // then ApplyOptions attaches the reference. The fits were made from the corrected heats.
        var f = Setup(subtracted: false);
        f.Target.Attributes.Add(new BufferSubtractionSettings(f.Reference.UniqueID, BufferSubtractionMethod.MatchedInjection).ToAttribute());
        AssertHeats(f.Target, TargetHeats);

        DataManager.ApplyOptions();

        AssertHeats(f.Target, Subtract(TargetHeats, BlankHeats));
        Assert.Equal(f.Revision + 1, f.Target.ProcessingRevision);
        Assert.Equal(1, f.ProcessingUpdated);
        Assert.Equal(0, f.SolutionChanged);
        Assert.True(f.Attached.IsValid);
        Assert.True(f.Saved.IsValid);
    }

    [Fact]
    public async Task ReopeningProjectKeepsCorrectedFitsValid()
    {
        var f = Setup(subtracted: true);
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, DataManager.Data, DataManager.Results, DataManager.SourceItems);
        stream.Position = 0;
        var read = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        Assert.Empty(read.Issues);

        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(read.Containers);
        var target = DataManager.Data.Single(data => data.UniqueID == f.Target.UniqueID);
        var result = Assert.Single(DataManager.Results);
        var revision = target.ProcessingRevision;
        var processingUpdated = 0;
        target.ProcessingUpdated += (_, _) => processingUpdated++;

        DataManager.ApplyOptions();

        AssertHeats(target, Subtract(TargetHeats, BlankHeats));
        Assert.Equal(revision, target.ProcessingRevision);
        Assert.Equal(0, processingUpdated);
        Assert.True(target.Solution.IsValid);
        Assert.True(Assert.Single(result.Solution.Solutions).IsValid);
        Assert.True(result.IsValidForCurrentData);
    }

    sealed class Fixture
    {
        public ExperimentData Target, Reference, OtherReference;
        public SolutionInterface Attached, Saved;
        public AnalysisResult Result;
        public int Revision, ProcessingUpdated, SolutionChanged;
    }

    static Fixture Setup(bool subtracted)
    {
        var f = new Fixture
        {
            Reference = Experiment("blank", BlankHeats),
            OtherReference = Experiment("other blank", OtherBlankHeats),
            Target = Experiment("target", TargetHeats),
        };
        DataManager.AddData(new ITCDataContainer[] { f.Reference, f.OtherReference, f.Target });
        if (subtracted) f.Target.SetBufferSubtraction(f.Reference, BufferSubtractionMethod.MatchedInjection, notify: false);

        // The saved result keeps its own solution; a later fit is attached to the experiment.
        var savedModel = Fit(f.Target);
        f.Target.UpdateSolution(savedModel);
        f.Result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver
        { Model = savedModel, ErrorEstimationMethod = ErrorEstimationMethod.None }));
        DataManager.AddData(f.Result);
        f.Target.UpdateSolution(Fit(f.Target));

        f.Attached = f.Target.Solution;
        f.Saved = Assert.Single(f.Result.Solution.Solutions);
        Assert.NotSame(f.Attached, f.Saved);
        Assert.Same(f.Target, f.Saved.Data);
        Assert.True(f.Attached.IsValid);
        Assert.True(f.Saved.IsValid);
        Assert.True(f.Result.IsValidForCurrentData);

        f.Revision = f.Target.ProcessingRevision;
        f.Target.ProcessingUpdated += (_, _) => f.ProcessingUpdated++;
        f.Target.SolutionChanged += (_, _) => f.SolutionChanged++;
        return f;
    }

    static void AssertInvalidatedButRetained(Fixture f)
    {
        Assert.Same(f.Attached, f.Target.Solution);
        Assert.Same(f.Saved, Assert.Single(f.Result.Solution.Solutions));
        Assert.False(f.Attached.IsValid);
        Assert.False(f.Saved.IsValid);
        Assert.False(f.Result.Solution.IsValid);
    }

    static void AssertHeats(ExperimentData target, double[] corrected)
    {
        for (var i = 0; i < TargetHeats.Length; i++)
        {
            Assert.Equal(TargetHeats[i], target.Injections[i].RawPeakArea.Value);
            Assert.Equal(corrected[i], target.Injections[i].PeakArea.Value, tolerance: 1e-18);
        }
    }

    static double[] Subtract(double[] heats, System.Collections.Generic.IEnumerable<double> blank)
        => heats.Zip(blank, (heat, buffer) => heat - buffer).ToArray();

    static double[] LeastSquaresLine(double[] heats)
    {
        // Ordinary least squares through (injection number, heat), injection numbers 1..n.
        var x = Enumerable.Range(1, heats.Length).Select(i => (double)i).ToArray();
        var xMean = x.Average();
        var yMean = heats.Average();
        var slope = x.Zip(heats, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum() / x.Sum(xi => (xi - xMean) * (xi - xMean));
        var intercept = yMean - slope * xMean;
        return x.Select(xi => intercept + slope * xi).ToArray();
    }

    static ExperimentData Experiment(string name, double[] heats)
    {
        var data = new ExperimentData(name + ".itc")
        {
            CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25,
            DataSourceFormat = ITCDataFormat.IntegratedHeats,
        };
        for (var index = 0; index < heats.Length; index++)
        {
            var injection = InjectionData.FromPEAQFile(data, index, true, index * 20, 2e-6, 20, 2, 25);
            injection.ActualCellConcentration = 35e-6;
            injection.ActualTitrantConcentration = (index + 1) * 5e-6;
            injection.Ratio = (index + 1) / 7d;
            injection.InitializeIntegrationTimes();
            injection.SetPeakArea(new FloatWithError(heats[index], HeatSd));
            data.Injections.Add(injection);
        }
        return data;
    }

    static Model Fit(ExperimentData data)
    {
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None };
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Algorithm = SolverAlgorithm.LevenbergMarquardt }));
        return model;
    }
}
