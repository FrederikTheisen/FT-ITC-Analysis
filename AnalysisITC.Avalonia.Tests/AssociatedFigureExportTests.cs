using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using AnalysisITC.Platform.Avalonia;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AssociatedFigureExportTests
{
    [AvaloniaTheory]
    [InlineData("Cancel")]
    [InlineData("Close")]
    [InlineData("Overwrite")]
    public void ExistingLaterTargetRequiresDecisionBeforeAnyWrites(string action)
    {
        using var export = new ExportFixture("First", "Second");
        var existingPath = Path.Combine(export.ResultFolder, "Second.pdf");
        var unrelatedPath = Path.Combine(export.ResultFolder, "Keep.pdf");
        Directory.CreateDirectory(export.ResultFolder);
        File.WriteAllText(existingPath, "Retained second figure");
        File.WriteAllText(unrelatedPath, "Unrelated PDF");
        var owner = new Window();
        export.UsePromptService(new ExportPrompt(paths =>
        {
            Assert.Equal(new[] { "First.pdf", "Second.pdf" }, paths.Select(Path.GetFileName));
            return ConfirmationDialogWindow.ConfirmModal(owner, "File already exists",
                "This export will overwrite:\nSecond.pdf", "Cancel", "Overwrite");
        }));
        var promptShown = false;
        Exception? interactionFailure = null;
        try
        {
            owner.Show();
            Dispatcher.UIThread.Post(() =>
            {
                var dialog = owner.OwnedWindows.SingleOrDefault();
                if (dialog == null) return;
                promptShown = true;
                try
                {
                    Assert.Equal("File already exists", dialog.Title);
                    Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(),
                        text => text.Text?.Contains("Second.pdf") == true);
                    Assert.Equal(2, Directory.GetFiles(export.ResultFolder).Length);
                    Assert.Equal("Retained second figure", File.ReadAllText(existingPath));
                    Assert.Empty(export.StatusMessages);
                    if (action == "Close") dialog.Close();
                    else Assert.Single(dialog.GetLogicalDescendants().OfType<Button>(),
                        button => Equals(button.Content, action))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex)
                {
                    interactionFailure = ex;
                    dialog.Close(false);
                }
            });

            var count = export.Run();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(interactionFailure);
            Assert.True(promptShown);
            Assert.Equal("Unrelated PDF", File.ReadAllText(unrelatedPath));
            if (action == "Overwrite")
            {
                Assert.Equal(2, count);
                AssertPdf(Path.Combine(export.ResultFolder, "First.pdf"));
                AssertPdf(existingPath);
                Assert.Equal(3, Directory.GetFiles(export.ResultFolder).Length);
                Assert.Equal("2 final figures exported", Assert.Single(export.StatusMessages));
            }
            else
            {
                Assert.Equal(0, count);
                Assert.Equal("Retained second figure", File.ReadAllText(existingPath));
                Assert.Equal(2, Directory.GetFiles(export.ResultFolder).Length);
                Assert.Empty(export.StatusMessages);
            }
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close(false);
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionFreeExportNeedsNoConfirmationWindow(bool destinationExists)
    {
        using var export = new ExportFixture("First", "Second");
        if (!destinationExists) Directory.Delete(export.Folder);
        export.UsePromptService(new AvaloniaExportPromptService());

        Assert.Equal(2, export.Run());

        AssertPdf(Path.Combine(export.ResultFolder, "First.pdf"));
        AssertPdf(Path.Combine(export.ResultFolder, "Second.pdf"));
        Assert.Equal(2, Directory.GetFiles(export.ResultFolder).Length);
        Assert.Equal("2 final figures exported", Assert.Single(export.StatusMessages));
    }

    [AvaloniaFact]
    public void UnavailableConfirmationWindowPreservesTheWholeBatch()
    {
        using var export = new ExportFixture("First", "Second");
        var existingPath = Path.Combine(export.ResultFolder, "Second.pdf");
        Directory.CreateDirectory(export.ResultFolder);
        File.WriteAllText(existingPath, "Retained figure");
        export.UsePromptService(new AvaloniaExportPromptService());

        Assert.Equal(0, export.Run());

        Assert.Equal("Retained figure", File.ReadAllText(existingPath));
        Assert.Single(Directory.GetFiles(export.ResultFolder));
        Assert.Empty(export.StatusMessages);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmationUsesTheCompletePlanIncludingSanitizedAndSuffixedNames(bool approve)
    {
        using var export = new ExportFixture("Sample", "Sample", "sample", "Sample (2)", "Bad/Name", "Bad_Name");
        var expectedNames = new[]
        {
            "Sample.pdf", "Sample (3).pdf", "sample (4).pdf", "Sample (2).pdf", "Bad_Name.pdf", "Bad_Name (2).pdf"
        };
        var existingPath = Path.Combine(export.ResultFolder, expectedNames[^1]);
        Directory.CreateDirectory(export.ResultFolder);
        File.WriteAllText(existingPath, "Existing suffixed PDF");
        var calls = 0;
        export.UsePromptService(new ExportPrompt(paths =>
        {
            calls++;
            Assert.Equal(expectedNames.Select(name => Path.Combine(export.ResultFolder, name)), paths);
            Assert.Single(Directory.GetFiles(export.ResultFolder));
            Assert.Equal("Existing suffixed PDF", File.ReadAllText(existingPath));
            return approve;
        }));

        Assert.Equal(approve ? 6 : 0, export.Run());

        Assert.Equal(1, calls);
        Assert.Equal(approve ? 6 : 1, Directory.GetFiles(export.ResultFolder).Length);
        if (approve) foreach (var name in expectedNames) AssertPdf(Path.Combine(export.ResultFolder, name));
        else Assert.Equal("Existing suffixed PDF", File.ReadAllText(existingPath));
    }

    [AvaloniaFact]
    public void DecliningConfirmationDoesNotCreateTheDestinationDirectory()
    {
        using var export = new ExportFixture("First");
        Directory.Delete(export.Folder);
        export.UsePromptService(new ExportPrompt(paths =>
        {
            Assert.False(Directory.Exists(export.Folder));
            Assert.Equal(Path.Combine(export.ResultFolder, "First.pdf"), Assert.Single(paths));
            return false;
        }));

        Assert.Equal(0, export.Run());

        Assert.False(Directory.Exists(export.Folder));
        Assert.Empty(export.StatusMessages);
    }

    [AvaloniaFact]
    public void ApprovedExportUsesTheConfirmedNamesSavedMembersAndCapturedModelSelection()
    {
        using var export = new ExportFixture("First", "Second");
        Directory.CreateDirectory(export.ResultFolder);
        File.WriteAllText(Path.Combine(export.ResultFolder, "First.pdf"), "Existing figure");
        foreach (var data in export.Experiments)
        {
            var attached = new Offset(data);
            attached.InitializeParameters(data);
            attached.Solution = SolutionInterface.FromModel(attached, Converged());
            data.Model = attached;
        }
        export.UsePromptService(new ExportPrompt(paths =>
        {
            Assert.Equal(new[] { "First.pdf", "Second.pdf" }, paths.Select(Path.GetFileName));
            // A modal interaction must not change the plan or plotted model already captured.
            export.Experiments[0].Name = "Renamed while confirming";
            FinalFigureModelPresentation.Selection = PublicationFigureModelSelection.None;
            return true;
        }));

        Assert.Equal(2, export.Run());

        var first = PdfKeywords(Path.Combine(export.ResultFolder, "First.pdf"));
        var second = PdfKeywords(Path.Combine(export.ResultFolder, "Second.pdf"));
        Assert.Contains("ΔH = -25", first);
        Assert.Contains("ΔH = -35", second);
        Assert.Equal(2, Directory.GetFiles(export.ResultFolder).Length);
        Assert.All(export.Experiments, data => Assert.IsType<Offset>(data.Model));
    }

    [AvaloniaFact]
    public void ActiveAllStyleRepeatReplacesApprovedTargetsAndDeclinedRepeatPreservesThem()
    {
        using var export = new ExportFixture("First", "Second");
        var confirmations = new Queue<bool>(new[] { true, true, false });
        var promptCalls = 0;
        export.UsePromptService(new ExportPrompt(paths =>
        {
            promptCalls++;
            Assert.Equal(new[] { "First.pdf", "Second.pdf" }, paths.Select(Path.GetFileName));
            return confirmations.Dequeue();
        }));

        Assert.Equal(2, export.RunBatch());
        var firstPath = Path.Combine(export.Folder, "First.pdf");
        var secondPath = Path.Combine(export.Folder, "Second.pdf");
        Assert.Equal(2, export.RunBatch());
        Assert.Equal(2, Directory.GetFiles(export.Folder, "*.pdf").Length);
        Assert.DoesNotContain(Directory.GetFiles(export.Folder), path => path.Contains(" (2).pdf", StringComparison.Ordinal));

        var firstBytes = File.ReadAllBytes(firstPath);
        var secondBytes = File.ReadAllBytes(secondPath);
        Assert.Equal(0, export.RunBatch());
        Assert.Equal(firstBytes, File.ReadAllBytes(firstPath));
        Assert.Equal(secondBytes, File.ReadAllBytes(secondPath));
        Assert.Equal(3, promptCalls);
    }

    [AvaloniaFact]
    public void BatchPreparationFailureReportsOnceAndDoesNotReportSuccess()
    {
        using var export = new ExportFixture("First", "Second");
        var invalidParent = Path.Combine(export.Folder, "parent-is-a-file");
        File.WriteAllText(invalidParent, "block directory creation");
        export.UsePromptService(new ExportPrompt(_ => true));
        var notifications = new List<HandledException>();
        EventHandler<HandledException> handler = (_, message) => notifications.Add(message);
        AppEventHandler.ShowAppMessage += handler;
        try
        {
            var count = export.RunToFolder(invalidParent);

            Assert.Equal(0, count);
            var message = Assert.Single(notifications);
            Assert.Equal(HandledException.Severity.Error, message.Level);
            Assert.Contains("0 of 2 files were", message.Message);
            Assert.Contains("planned files were not written", message.Message);
            Assert.Single(export.StatusMessages);
            Assert.Contains("0 of 2 files were", export.StatusMessages[0]);
            Assert.DoesNotContain("exported", export.StatusMessages[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AppEventHandler.ShowAppMessage -= handler;
        }
    }

    [AvaloniaFact]
    public void FinalFigureDestinationWriteFailureIsHandledOnceWithoutSuccess()
    {
        using var export = new ExportFixture("First");
        var workspace = new FinalFigureWorkspaceControl();
        var statuses = new List<string>();
        workspace.StatusChanged += (_, status) => statuses.Add(status);
        var blockedPath = Path.Combine(export.Folder, "target-is-directory.pdf");
        Directory.CreateDirectory(blockedPath);
        var notifications = new List<HandledException>();
        EventHandler<HandledException> handler = (_, message) => notifications.Add(message);
        AppEventHandler.ShowAppMessage += handler;
        try
        {
            workspace.ExportSingleFigureToPathForTesting(export.Experiments[0], blockedPath,
                PublicationFigureModelSelection.BindingModel);

            var message = Assert.Single(notifications);
            Assert.Contains("Could not write", message.Message);
            Assert.Contains("target-is-directory.pdf", message.Message);
            Assert.Single(statuses);
            Assert.Contains("Could not write", statuses[0]);
            Assert.DoesNotContain("exported", statuses[0], StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFiles(blockedPath));
        }
        finally
        {
            AppEventHandler.ShowAppMessage -= handler;
        }
    }

    [AvaloniaFact]
    public void CompleteActionBoundaryReportsPickerFailureOnceWithoutSuccess()
    {
        var workspace = new FinalFigureWorkspaceControl();
        var statuses = new List<string>();
        workspace.StatusChanged += (_, status) => statuses.Add(status);
        var notifications = new List<HandledException>();
        EventHandler<HandledException> handler = (_, message) => notifications.Add(message);
        AppEventHandler.ShowAppMessage += handler;
        try
        {
            workspace.RunExportActionForTesting(() => Task.FromException(new IOException("picker unavailable")))
                .GetAwaiter().GetResult();

            var message = Assert.Single(notifications);
            Assert.Contains("picker unavailable", message.Message);
            Assert.Single(statuses);
            Assert.Contains("picker unavailable", statuses[0]);
            Assert.DoesNotContain("exported", statuses[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AppEventHandler.ShowAppMessage -= handler;
        }
    }

    [AvaloniaFact]
    public void SelectedFigurePickerFailureIsHandledByTheExportAction()
    {
        var export = new FinalFigureWorkspaceControl();
        var statuses = new List<string>();
        export.StatusChanged += (_, status) => statuses.Add(status);
        export.SelectedItem = new ExperimentData("selected.itc") { Name = "Selected" };
        export.SaveFilePickerForTesting = _ => Task.FromException<IStorageFile?>(new IOException("final picker unavailable"));
        var notifications = new List<HandledException>();
        EventHandler<HandledException> handler = (_, message) => notifications.Add(message);
        AppEventHandler.ShowAppMessage += handler;
        try
        {
            export.ExportCurrentPdfAsync().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            var message = Assert.Single(notifications);
            Assert.Contains("final picker unavailable", message.Message);
            Assert.Single(statuses);
            Assert.Contains("final picker unavailable", statuses[0]);
            Assert.DoesNotContain("exported", statuses[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AppEventHandler.ShowAppMessage -= handler;
        }
    }

    static void AssertPdf(string path) => Assert.StartsWith("%PDF", File.ReadAllText(path));

    static string PdfKeywords(string path)
    {
        var pdf = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var match = Regex.Match(pdf, @"/Keywords\s*<([0-9A-Fa-f]+)>");
        Assert.True(match.Success, "Expected Unicode PDF metadata keywords.");
        return Encoding.BigEndianUnicode.GetString(Convert.FromHexString(match.Groups[1].Value)).TrimStart('\uFEFF');
    }

    static SolverConvergence Converged() => SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
    {
        Termination = SolverTermination.Converged,
        Loss = 1e-6
    });

    sealed class ExportFixture : IDisposable
    {
        readonly IExportPromptService originalPrompt = PlatformServices.ExportPromptService;
        readonly PublicationFigureModelSelection originalSelection = FinalFigureModelPresentation.Selection;
        readonly EnergyUnitFamily originalEnergyFamily = FinalFigureModelPresentation.EnergyUnitFamily;
        readonly FinalFigureWorkspaceControl workspace;
        readonly AnalysisResult result;

        public string Folder { get; } = Directory.CreateTempSubdirectory("cr005-").FullName;
        public string ResultFolder => Path.Combine(Folder, FigureExportPlanner.SanitizeFileName(result.Name) ?? "Analysis Result");
        public ExperimentData[] Experiments { get; }
        public List<string> StatusMessages { get; } = new();

        public ExportFixture(params string[] names)
        {
            FinalFigureModelPresentation.EnergyUnitFamily = EnergyUnitFamily.Joules;
            Experiments = names.Select((name, index) => CreateExperiment(name, -25000 - index * 10000)).ToArray();
            var models = Experiments.Select(data => data.Model).ToList();
            var global = new GlobalModel(models)
            {
                Parameters = new GlobalModelParameters(),
                ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None }
            };
            foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
            global.Solution = new GlobalSolution(new GlobalSolver { Model = global },
                models.Select(model => model.Solution).ToList(), Converged(), reconstructBootstrap: false);
            result = new AnalysisResult(global.Solution);
            workspace = new FinalFigureWorkspaceControl();
            workspace.StatusChanged += (_, message) => StatusMessages.Add(message);
        }

        public void UsePromptService(IExportPromptService service) => PlatformServices.RegisterExportPromptService(service);

        public int Run() => workspace.ExportResultFiguresToFolder(result, Experiments, Folder,
            PublicationFigureModelSelection.BindingModel);

        public int RunBatch() => workspace.ExportFigureBatch(Experiments, Folder,
            PublicationFigureModelSelection.BindingModel);

        public int RunToFolder(string folderPath) => workspace.ExportFigureBatch(Experiments, folderPath,
            PublicationFigureModelSelection.BindingModel);

        public void Dispose()
        {
            PlatformServices.RegisterExportPromptService(originalPrompt);
            FinalFigureModelPresentation.Selection = originalSelection;
            FinalFigureModelPresentation.EnergyUnitFamily = originalEnergyFamily;
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }

        static ExperimentData CreateExperiment(string name, double enthalpy)
        {
            var data = new ExperimentData("member.itc")
            {
                Name = name,
                CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
                CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25
            };
            for (var index = 0; index < 12; index++)
            {
                var injection = new InjectionData(data, index, 2e-6, 8.4e-10, true)
                {
                    ActualCellConcentration = 35e-6, ActualTitrantConcentration = (index + 1) * 5e-6,
                    Ratio = (index + 1) / 7d
                };
                injection.SetPeakArea(new FloatWithError((-20000 + index * 1000) * 8.4e-10, 1e-8));
                data.Injections.Add(injection);
            }
            var model = new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(enthalpy);
            model.Parameters.Table[ParameterType.Affinity1].Update(6);
            model.Parameters.Table[ParameterType.Nvalue1].Update(1);
            model.Parameters.Table[ParameterType.Offset].Update(0);
            model.Solution = SolutionInterface.FromModel(model, Converged());
            data.Model = model;
            return data;
        }
    }

    sealed class ExportPrompt(Func<string[], bool> confirm) : IExportPromptService
    {
        public Task<string> ChooseExportFolderAsync(ExportAccessoryViewSettings settings) => throw new NotSupportedException();
        public bool ConfirmOverwrite(IEnumerable<string> outputPaths) => confirm(outputPaths.ToArray());
    }
}
