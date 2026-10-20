using System;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(SolverEventCollectionDefinition.Name)]
public sealed class AnalysisResultExperimentAvailabilityTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();

    public AnalysisResultExperimentAvailabilityTests()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        AppSettings.MinimumTemperatureSpanForFitting = 3;
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        original.ApplyToSettings();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RemovingSourcesInvalidatesWholeResultWithoutChangingStoredFitOrFigureComposition(int count)
    {
        var result = LoadResult();
        var solution = result.Solution;
        var members = solution.Solutions.ToArray();
        var parameters = members.Select(member => member.Parameters.ToArray()).ToArray();
        var snapshot = result.ValiditySnapshot.ToJson();
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);

        for (var index = 0; index < count; index++) DataManager.RemoveSourceItemAt(0);

        Assert.Equal(AnalysisResultValidity.Invalid, result.ValidityReport.Status);
        Assert.Equal(AnalysisResultHealth.Invalid, result.Health);
        Assert.False(result.IsValidForCurrentData);
        for (var index = 0; index < count; index++)
            Assert.Contains($"Experiment missing: {members[index].Data.Name}.", result.ValidityReport.Reasons);
        Assert.Same(solution, result.Solution);
        Assert.Equal(members, solution.Solutions);
        for (var index = 0; index < members.Length; index++)
            Assert.Equal(parameters[index], members[index].Parameters.ToArray());
        Assert.Equal(snapshot, result.ValiditySnapshot.ToJson());
        Assert.True(solution.IsValid);

        // Availability changes status; it does not add a new composition restriction.
        var canvas = PublicationFigureCanvasBuilder.Build(new ITCDataContainer[] { result },
            new PublicationFigureOptions(), new PublicationFigureCanvasOptions());
        Assert.True(canvas.IsValid, canvas.ValidationError);
        Assert.Equal(2, canvas.Cells.Count);
    }

    [Theory]
    [InlineData(AnalysisResultValidity.Valid)]
    [InlineData(AnalysisResultValidity.PartialInvalid)]
    [InlineData(AnalysisResultValidity.Invalid)]
    [InlineData(AnalysisResultValidity.Unknown)]
    public void UndoRestoresPreviousValidityAndReasons(AnalysisResultValidity previous)
    {
        var result = LoadResult();
        if (previous == AnalysisResultValidity.Unknown) result.SetValiditySnapshot(null);
        if (previous == AnalysisResultValidity.PartialInvalid || previous == AnalysisResultValidity.Invalid)
            result.Solution.Solutions[0].Data.MeasuredTemperature = 26;
        if (previous == AnalysisResultValidity.Invalid)
            result.Solution.Solutions[1].Data.MeasuredTemperature = 26;
        Assert.Equal(previous, result.ValidityReport.Status);
        var reasons = result.ValidityReport.Reasons.ToArray();
        var experiment = result.Solution.Solutions[0].Data;

        DataManager.RemoveSourceItemAt(0);
        Assert.Equal(AnalysisResultValidity.Invalid, result.ValidityReport.Status);
        Assert.Contains($"Experiment missing: {experiment.Name}.", result.ValidityReport.Reasons);
        Assert.All(reasons, reason => Assert.Contains(reason, result.ValidityReport.Reasons));

        DataManager.UndoDeleteData();
        Assert.Contains(experiment, DataManager.Data);
        Assert.Equal(previous, result.ValidityReport.Status);
        Assert.Equal(reasons, result.ValidityReport.Reasons);
    }

    [Fact]
    public void RemovalAndUndoNotificationsExposeTheUpdatedValidity()
    {
        var result = LoadResult();
        AnalysisResultValidity? observed = null;
        EventHandler<ExperimentData> changed = (_, _) => observed = result.ValidityReport.Status;
        DataManager.DataDidChange += changed;
        try
        {
            DataManager.RemoveSourceItemAt(0);
            Assert.Equal(AnalysisResultValidity.Invalid, observed);
            DataManager.UndoDeleteData();
            Assert.Equal(AnalysisResultValidity.Valid, observed);
        }
        finally { DataManager.DataDidChange -= changed; }
    }

    [Fact]
    public void SameIdReplacementDoesNotMakeTheRetainedHiddenExperimentAvailable()
    {
        var result = LoadResult();
        var source = result.Solution.Solutions[0].Data;
        DataManager.RemoveSourceItemAt(0);
        var replacement = new ExperimentData("replacement.itc");
        replacement.SetID(source.UniqueID);
        DataManager.AddData(replacement);

        Assert.Equal(AnalysisResultValidity.Invalid, result.ValidityReport.Status);
        Assert.Contains($"Experiment missing: {source.Name}.", result.ValidityReport.Reasons);
        Assert.Same(source, result.Solution.Solutions[0].Data);
    }

    [Fact]
    public void RemovingAnUnrelatedExperimentDoesNotInvalidateTheResult()
    {
        var result = LoadResult();
        DataManager.AddData(new ExperimentData("unrelated.itc"));
        DataManager.RemoveSourceItemAt(DataManager.SourceItems.Count - 1);
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
    }

    [Fact]
    public void UnnamedMissingExperimentUsesItsId()
    {
        var result = LoadResult();
        var source = result.Solution.Solutions[0].Data;
        source.Name = "";
        source.SetFileName("");
        DataManager.RemoveSourceItemAt(0);
        Assert.Contains($"Experiment missing: {source.UniqueID}.", result.ValidityReport.Reasons);
    }

    [Fact]
    public void DetachedResultIsNotComparedWithAnUnrelatedActiveDocument()
    {
        var result = AnalysisResultTemperatureValidityTests.Create(25, 25);
        DataManager.AddData(new ExperimentData("other-document.itc"));
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
    }

    static AnalysisResult LoadResult()
    {
        var result = AnalysisResultTemperatureValidityTests.Create(25, 25);
        DataManager.AddData(result.Solution.Solutions.Select(member => (ITCDataContainer)member.Data)
            .Append(result).ToArray());
        return result;
    }
}
