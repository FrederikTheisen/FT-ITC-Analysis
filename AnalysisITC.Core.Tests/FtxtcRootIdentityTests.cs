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
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class FtxtcRootIdentityTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftxtc-root-ids-" + Guid.NewGuid().ToString("N"));
    readonly ExperimentData experiment;
    readonly AnalysisResult result;
    readonly AnalysisReport report;

    public FtxtcRootIdentityTests()
    {
        Directory.CreateDirectory(directory);
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
        var model = InjectionProcessingMethodTests.FittedModel();
        experiment = model.Data;
        experiment.UpdateSolution(model);
        var global = new GlobalModel(new List<Model> { model })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        global.Parameters.AddIndivdualParameter(model.Parameters);
        global.Solution = new GlobalSolution(new GlobalSolver { Model = global }, new List<SolutionInterface> { model.Solution },
            SolverConvergence.FromFixedFit(1, 1e-12), reconstructBootstrap: false);
        result = new AnalysisResult(global.Solution);
        report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        DataManager.AddData(new ITCDataContainer[] { experiment, result });
        DataManager.AddReport(report);
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        Directory.Delete(directory, true);
    }

    [Theory]
    [InlineData("experiment", null)]
    [InlineData("experiment", "")]
    [InlineData("experiment", " ")]
    [InlineData("solution", null)]
    [InlineData("solution", "")]
    [InlineData("solution", " ")]
    [InlineData("result", null)]
    [InlineData("result", "")]
    [InlineData("result", " ")]
    [InlineData("report", null)]
    [InlineData("report", "")]
    [InlineData("report", " ")]
    public async Task EmptyRootIdsRejectSaveAndPreserveExistingDestination(string kind, string id)
    {
        var error = await RejectSave(() => SetId(kind, id));
        Assert.Contains(kind, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("empty", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("experiment", "experiment")]
    [InlineData("result", "result")]
    [InlineData("report", "report")]
    [InlineData("experiment", "solution")]
    [InlineData("experiment", "result")]
    [InlineData("experiment", "report")]
    [InlineData("solution", "result")]
    [InlineData("solution", "report")]
    [InlineData("result", "report")]
    public async Task DuplicateRootIdsRejectSaveAndPreserveExistingDestination(string first, string second)
    {
        var id = Id(first);
        var error = await RejectSave(() =>
        {
            if (first != second) SetId(second, id);
            else if (first == "experiment")
            {
                var duplicate = new ExperimentData("duplicate.itc");
                duplicate.SetID(id);
                DataManager.AddData(duplicate);
            }
            else if (first == "result")
            {
                var duplicate = new AnalysisResult(result.Solution);
                duplicate.SetID(id);
                DataManager.AddData(duplicate);
            }
            else
            {
                var duplicate = new AnalysisReport();
                duplicate.SetID(id);
                DataManager.AddReport(duplicate);
            }
        });
        Assert.Contains(id, error.Message);
        Assert.Contains("Import Copies", error.Message);
    }

    async Task<InvalidDataException> RejectSave(Action corrupt)
    {
        var path = Path.Combine(directory, "existing.ftxtc");
        ProjectDocumentState.Path = path;
        Assert.True(await ProjectWriter.SaveWithPathAsync());
        var bytes = File.ReadAllBytes(path);
        var savedIds = Ids();
        corrupt();
        DocumentDirtyTracker.MarkDirty();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();
        var invalidIds = Ids();
        var items = DataManager.SourceItems.ToArray();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCWriter.WriteFileAsync(
            path, DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports));
        Assert.False(await ProjectWriter.SaveWithPathAsync());

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(path, ProjectDocumentState.Path);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.True(DocumentDirtyTracker.MatchesSaveStamp(stamp));
        Assert.Equal(invalidIds, Ids());
        Assert.Equal(items, DataManager.SourceItems);
        Assert.False(ProjectWriter.IsWriteInProgress);
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
        using var stream = File.OpenRead(path);
        var reopened = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        Assert.Empty(reopened.Issues);
        Assert.Equal(savedIds, reopened.Containers.OfType<ExperimentData>().Select(data => data.UniqueID)
            .Concat(reopened.Containers.OfType<ExperimentData>().Select(data => data.Solution.Guid))
            .Concat(reopened.Containers.OfType<AnalysisResult>().Select(data => data.UniqueID))
            .Concat(reopened.Reports.Select(data => data.UniqueID)));
        return error;
    }

    static string[] Ids() => DataManager.Data.Select(data => data.UniqueID)
        .Concat(DataManager.Data.Where(data => data.Solution != null).Select(data => data.Solution.Guid))
        .Concat(DataManager.Results.Select(data => data.UniqueID))
        .Concat(DataManager.Reports.Select(data => data.UniqueID)).ToArray();

    string Id(string kind) => kind switch
    {
        "experiment" => experiment.UniqueID,
        "solution" => experiment.Solution.Guid,
        "result" => result.UniqueID,
        "report" => report.UniqueID,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    void SetId(string kind, string id)
    {
        switch (kind)
        {
            case "experiment": experiment.SetID(id); break;
            case "solution": experiment.Solution.SetID(id); break;
            case "result": result.SetID(id); break;
            case "report": report.SetID(id); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
