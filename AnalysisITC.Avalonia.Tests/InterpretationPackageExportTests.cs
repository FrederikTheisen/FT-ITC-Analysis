using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class InterpretationPackageExportTests
{
    public InterpretationPackageExportTests() => AvaloniaTestBootstrap.EnsureInitialized();

    // Keep this headless UI test on its owning thread. Tasks are verified complete
    // before reading their results, with dispatcher work explicitly pumped below.
#pragma warning disable xUnit1031
    [Theory]
    [InlineData("save")]
    [InlineData("cancel")]
    [InlineData("write-error")]
    public void PackageExportUsesAgreedNamesAndStaysLocal(string outcome)
    {
        var result = CreateResult();
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        report.SetManualInterpretation("Keep the approved interpretation.");
        var approved = report.ApprovedInterpretation;
        using var handler = new NoNetworkHandler();
        using var client = new HttpClient(handler);
        var registrations = 0;
        var dialog = new AnalysisInterpretationDialog(report, result, client, () => registrations++);
        var directory = Directory.CreateTempSubdirectory("ftitc-interpretation-export-");
        var path = System.IO.Path.Combine(directory.FullName, "local-export.zip");
        File.WriteAllText(path, "Existing local file contents");
        FilePickerSaveOptions? options = null;
        var originalContext = SynchronizationContext.Current;
        try
        {
            var lookup = dialog.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
            Assert.True(lookup.IsCompleted);
            using var file = lookup.GetAwaiter().GetResult();
            Assert.NotNull(file);
            if (outcome == "write-error")
            {
                File.Delete(path);
                directory.Delete(); // A selected destination that disappears before writing.
            }
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            var operation = dialog.SavePackageAsync(value =>
            {
                options = value;
                return Task.FromResult<IStorageFile?>(outcome == "cancel" ? null : file);
            });
            var timer = Stopwatch.StartNew();
            while (!operation.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Yield();
            }
            Assert.True(operation.IsCompleted, "Local export should finish without external work.");
            operation.GetAwaiter().GetResult();

            Assert.NotNull(options);
            Assert.Equal("Save interpretation package", options.Title);
            Assert.Equal("ftitc-interpretation-package.zip", options.SuggestedFileName);
            var fileType = Assert.Single(options.FileTypeChoices!);
            Assert.Equal("Interpretation package archive", fileType.Name);
            Assert.Equal("*.zip", Assert.Single(fileType.Patterns!));
            Assert.Equal(0, handler.Requests);
            Assert.Equal(approved.InterpretationMarkdown, report.ApprovedInterpretation.InterpretationMarkdown);
            Assert.Equal(approved.Origin, report.ApprovedInterpretation.Origin);
            var controls = dialog.GetLogicalDescendants().OfType<Control>().ToList();
            var save = Assert.Single(controls.OfType<Button>(), control =>
                AutomationProperties.GetName(control) == "Save interpretation package locally without generation");
            Assert.Equal("Save package", save.Content);
            Assert.True(save.IsEnabled);
            if (outcome == "cancel")
            {
                Assert.Equal(0, registrations);
                Assert.Equal("Existing local file contents", File.ReadAllText(path));
                Assert.DoesNotContain(controls.OfType<TextBlock>(), control =>
                    control.Text?.Contains("package saved", StringComparison.Ordinal) == true);
            }
            else if (outcome == "write-error")
            {
                Assert.Contains(controls.OfType<TextBlock>(), control =>
                    control.Text?.StartsWith("Could not save interpretation package. ", StringComparison.Ordinal) == true);
            }
            else
            {
                Assert.Contains(controls.OfType<TextBlock>(), control =>
                    control.Text == "Interpretation package saved locally. Nothing was sent to the server.");
                using var archive = ZipFile.OpenRead(path);
                Assert.Equal(6, archive.Entries.Count);
                Assert.NotNull(archive.GetEntry("canonical-package.json"));
                Assert.NotNull(archive.GetEntry("model-package.json"));
                using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
                Assert.False(manifest.RootElement.GetProperty("sentToServer").GetBoolean());
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
            if (Directory.Exists(directory.FullName))
            {
                File.Delete(path);
                directory.Delete();
            }
        }
    }
#pragma warning restore xUnit1031

    static AnalysisResult CreateResult()
    {
        var experiment = new ExperimentData("local-export.itc")
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25,
        };
        experiment.Injections.Add(new InjectionData(experiment, volume: 1e-6) { IsIntegrated = true, Ratio = 1 });
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
    }

    sealed class NoNetworkHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new InvalidOperationException("Local export must not make an HTTP request.");
        }
    }
}
