using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

/// <summary>
/// ITC-074: Save Selected on a result also writes the loaded Analysis Results its experiments use as
/// competitor sources, with their members and buffer references, following nested references once each.
/// </summary>
[Collection(ProjectWriterSaveSelectedCollectionDefinition.Name)]
public sealed class CompetitorSourceSaveSelectedTests : IDisposable
{
    const string CompetitiveResultId = "738b45e0-9a9c-4f61-871c-4577be7471b2";
    const string SourceResultId = "199b7c48-a06c-4c66-bb64-eb9c735e46e9";

    readonly string directory = Path.Combine(Path.GetTempPath(), "CompetitorSourceSave-" + Guid.NewGuid().ToString("N"));
    readonly IFileSavePromptService originalPrompt = PlatformServices.FileSavePromptService;

    public CompetitorSourceSaveSelectedTests()
    {
        Directory.CreateDirectory(directory);
        DataManager.Clear(DataClearMode.ResetSession);
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        PlatformServices.RegisterFileSavePromptService(originalPrompt);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task ResultSaveIncludesCompetitorSourceAndItsMembers()
    {
        var loaded = await Load();
        var competitive = Result(loaded, CompetitiveResultId);
        var source = Result(loaded, SourceResultId);

        var saved = await ReadStrict(await SaveSelected(competitive));

        Assert.Equal(new[] { CompetitiveResultId, SourceResultId }.OrderBy(id => id),
            saved.OfType<AnalysisResult>().Select(result => result.UniqueID).OrderBy(id => id));
        Assert.Equal(ExpectedExperimentIds(competitive, source),
            saved.OfType<ExperimentData>().Select(experiment => experiment.UniqueID).OrderBy(id => id));

        // Reopened alone, the experiment's competitor link resolves to the saved source.
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(saved);
        var experiment = saved.OfType<ExperimentData>().Single(item => item.Attributes.Any(attribute => attribute.Key == AttributeKey.CompetitorResult));
        var preview = CompetitorResultPreviewBuilder.Build(
            experiment.Attributes.Single(attribute => attribute.Key == AttributeKey.CompetitorResult), experiment);
        Assert.NotEqual("Missing", preview.Status);
        Assert.Equal(source.Name, preview.SourceName);
    }

    [Fact]
    public async Task NestedSourcesAndReferenceCyclesAreWrittenOnce()
    {
        var loaded = await Load();
        var competitive = Result(loaded, CompetitiveResultId);
        var source = Result(loaded, SourceResultId);
        var nested = loaded.OfType<AnalysisResult>().First(result => result.UniqueID != CompetitiveResultId
            && result.UniqueID != SourceResultId
            && result.Solution.Solutions.All(member => !source.Solution.Solutions.Any(other => other.Data == member.Data)));
        // The source's member uses a third result, whose member points back to the selected result.
        AddCompetitorReference(source.Solution.Solutions[0].Data, nested.UniqueID);
        AddCompetitorReference(nested.Solution.Solutions[0].Data, CompetitiveResultId);

        var saved = await ReadStrict(await SaveSelected(competitive));

        Assert.Equal(new[] { CompetitiveResultId, SourceResultId, nested.UniqueID }.OrderBy(id => id),
            saved.OfType<AnalysisResult>().Select(result => result.UniqueID).OrderBy(id => id));
        Assert.Equal(ExpectedExperimentIds(competitive, source, nested),
            saved.OfType<ExperimentData>().Select(experiment => experiment.UniqueID).OrderBy(id => id));
    }

    [Fact]
    public async Task UnloadedSourceIsNotWritten()
    {
        var loaded = await Load(except: SourceResultId);
        var competitive = Result(loaded, CompetitiveResultId);

        var saved = await ReadStrict(await SaveSelected(competitive));

        Assert.Equal(new[] { CompetitiveResultId }, saved.OfType<AnalysisResult>().Select(result => result.UniqueID));
        Assert.Equal(ExpectedExperimentIds(competitive),
            saved.OfType<ExperimentData>().Select(experiment => experiment.UniqueID).OrderBy(id => id));
    }

    [Fact]
    public async Task ExperimentSaveStaysOneExperiment()
    {
        var loaded = await Load();
        var experiment = Result(loaded, CompetitiveResultId).Solution.Solutions.Select(member => member.Data)
            .First(item => item.Attributes.Any(attribute => attribute.Key == AttributeKey.CompetitorResult));

        var saved = await ReadStrict(await SaveSelected(experiment));

        Assert.Empty(saved.OfType<AnalysisResult>());
        Assert.Equal(experiment.UniqueID, Assert.Single(saved.OfType<ExperimentData>()).UniqueID);
    }

    static async Task<ITCDataContainer[]> Load(string except = null)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "report_test.ftxtc"));
        var containers = (await FTXTCReader.ReadStream(stream))
            .Where(item => item.UniqueID != except).ToArray();
        DataManager.AddData(containers);
        return containers;
    }

    static AnalysisResult Result(IEnumerable<ITCDataContainer> containers, string id) =>
        containers.OfType<AnalysisResult>().Single(result => result.UniqueID == id);

    // Members of each written result plus the buffer reference experiments they use.
    static IEnumerable<string> ExpectedExperimentIds(params AnalysisResult[] results) =>
        results.SelectMany(result => result.Solution.Solutions.Select(member => member.Data))
            .SelectMany(experiment => new[] { experiment, experiment.ReferenceExperiment })
            .Where(experiment => experiment != null)
            .Select(experiment => experiment.UniqueID).Distinct().OrderBy(id => id);

    static void AddCompetitorReference(ExperimentData experiment, string resultId)
    {
        experiment.Attributes.RemoveAll(attribute => attribute.Key == AttributeKey.CompetitorResult);
        experiment.Attributes.Add(ExperimentAttribute.CompetitorResultReference(resultId));
    }

    async Task<string> SaveSelected(ITCDataContainer data)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".ftxtc");
        PlatformServices.RegisterFileSavePromptService(new FixedFileSavePrompt(path));
        Assert.True(await ProjectWriter.SaveSelectedAsync(data));
        return path;
    }

    static async Task<ITCDataContainer[]> ReadStrict(string path)
    {
        using var stream = File.OpenRead(path);
        return await FTXTCReader.ReadStream(stream);
    }

    sealed class FixedFileSavePrompt : IFileSavePromptService
    {
        readonly string path;

        public FixedFileSavePrompt(string path) => this.path = path;

        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes) => Task.FromResult(path);
    }
}
