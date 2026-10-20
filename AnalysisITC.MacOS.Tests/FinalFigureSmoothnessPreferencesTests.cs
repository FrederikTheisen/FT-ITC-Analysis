// Exercises real Preferences/Publish controls and native rendering; startup cases run in fresh processes.
using System;
using System.Linq;
using System.Reflection;
using AppKit;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using AnalysisITC.Platform.MacOS;
using AnalysisITC.UI.MacOS;
using AnalysisITC.UI.MacOS.Drawing;


using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using AnalysisITC.Core.Export;

static class FinalFigureSmoothnessPreferencesTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static FinalFigureViewController controller;
    static ExperimentData data;
    static string resourcesPath;
    static int passed, failed;

    static int Main(string[] args)
    {
        NSApplication.Init();
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        PlatformServices.RegisterMainThreadDispatcher(new MacMainThreadDispatcher());
        MacSettingsApplier.Register();
        if (args[0] == "--startup") return Startup(int.Parse(args[1]));
        PreferencesState.Defaults().Apply();
        AppSettings.PerformOnlineChecksOnLaunch = false;
        DocumentDirtyTracker.Initialize();
        resourcesPath = Path.GetDirectoryName(args[1]);
        new Class(typeof(FinalFigureGraphView)); new Class(typeof(FinalFigureViewController));
        new Class(typeof(WorkspaceTabControl)); new Class(typeof(HuggingTextView));
        var assembly = typeof(FinalFigureViewController).Assembly;
        foreach (var type in assembly.GetTypes().Where(type => !type.IsAbstract && typeof(NSObject).IsAssignableFrom(type)
            && (type.Name.Contains("Preferences") || type.Name == "FlippedPreferencesDocumentView"))) new Class(type);
        var directory = Path.Combine(Path.GetTempPath(), "ftitc-smoothness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var prompt = PlatformServices.FileSavePromptService;
        try
        {
            using (var resources = NSBundle.FromPath(resourcesPath))
            using (var storyboard = NSStoryboard.FromName("Main", resources))
            using (controller = (FinalFigureViewController)storyboard.InstantiateControllerWithIdentifier("FinalFigureViewController"))
            {
                Check(controller.View != null, "Publish view did not load");
                data = Experiment(); DataManager.Clear(DataClearMode.ResetSession); DataManager.AddData(data); DataManager.SelectIndex(0);
                StateManager.SetProgramState(ProgramState.Publish); FinalFigureGraphView.Invalidate();
                foreach (var mode in new[] { LineSmoothness.Smooth, LineSmoothness.Spline, LineSmoothness.Linear })
                {
                    var other = (LineSmoothness)(((int)mode + 1) % 3);
                    Run("Preferences Apply " + mode + " updates control, preview, export and capture", () =>
                    {
                        SaveDefault(other); SelectFigure(other);
                        var before = Pixels(FinalFigureGraphView.SetupForExport(data));
                        var oldCapture = Capture();
                        using (var editor = new PreferencesEditor()) { editor.Select(mode); editor.Apply(); }
                        Verify(mode, mode);
                        Check(Pixels(FinalFigureGraphView.SetupForExport(data)) != before, "Rendered export did not change");
                        Check(oldCapture.FitLineSmoothness == other, "Existing Supporting Figure capture changed");
                    });
                    Run("unchanged preference " + mode + " replaces a manual override", () =>
                    {
                        SaveDefault(mode); SelectFigure(other);
                        using (var editor = new PreferencesEditor()) { Check(editor.Selected == mode, "Preferences did not show saved mode"); editor.Apply(); }
                        Verify(mode, mode);
                    });
                    Run("opening and cancelling Preferences preserves " + other, () =>
                    {
                        SaveDefault(mode); SelectFigure(other);
                        var before = Pixels(FinalFigureGraphView.SetupForExport(data));
                        using (var editor = new PreferencesEditor()) { Check(editor.Selected == mode, "Preferences showed session choice instead of saved default"); editor.Select(other); editor.Cancel(); }
                        Verify(other, mode);
                        Check(Pixels(FinalFigureGraphView.SetupForExport(data)) == before, "Cancel changed rendered export");
                    });
                    Run("rejected Preferences Apply preserves " + other, () =>
                    {
                        SaveDefault(mode); SelectFigure(other);
                        using (var editor = new PreferencesEditor()) { editor.Select(other); editor.RejectWidth(); editor.Apply(); }
                        Verify(other, mode);
                    });
                    Run("ordinary settings save and view reentry preserve " + other, () =>
                    {
                        SaveDefault(mode); SelectFigure(other); AppSettings.Save(); controller.ViewWillAppear();
                        Verify(other, mode);
                    });
                    Run("document Save As preserves " + other, () =>
                    {
                        SaveDefault(mode); SelectFigure(other);
                        var path = Path.Combine(directory, mode + ".ftxtc");
                        PlatformServices.RegisterFileSavePromptService(new FixedPrompt(path));
                        var save = ProjectWriter.SaveAsync(); Wait(save);
                        Check(save.Result && File.Exists(path), "Save As did not write the project");
                        Check(AppSettings.LastDocumentPath == path, "Save As did not persist the path");
                        Verify(other, mode);
                    });
                }
                Run("invalidation synchronizes only the smoothness selector", () =>
                {
                    SelectFigure(LineSmoothness.Spline);
                    var draft = Outlet<NSTextField>("EmbeddedFitXMinField"); draft.StringValue = "unfinished axis input";
                    try
                    {
                        FinalFigureGraphView.FitLineSmoothness = GraphBase.LineSmoothness.Linear;
                        FinalFigureGraphView.Invalidate();
                        Check(Outlet<NSSegmentedControl>("EmbeddedSplineInterpolationControl").SelectedSegment == 2, "Smoothness selector stayed stale after invalidation");
                        Check(draft.StringValue == "unfinished axis input", "Smoothness refresh discarded another draft");
                    }
                    finally { controller.ViewWillAppear(); }
                });
            }
        }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
            PlatformServices.RegisterFileSavePromptService(prompt);
            Directory.Delete(directory, true);
        }
        Console.WriteLine(passed + " native smoothness groups passed; " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    static int Startup(int mode)
    {
        PlatformServices.SettingsStore.SetBool("IsSaved", true);
        PlatformServices.SettingsStore.SetInt("FitLineSmoothness", mode);
        AppSettings.Load();
        Check((int)AppSettings.FitLineSmoothness == mode && (int)ReadNativeMode() == mode, "Startup did not load saved smoothness");
        Console.WriteLine("PASS: fresh-process startup " + AppSettings.FitLineSmoothness);
        return 0;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static GraphBase.LineSmoothness ReadNativeMode() => FinalFigureGraphView.FitLineSmoothness;

    static void SaveDefault(LineSmoothness mode)
    {
        var state = PreferencesState.FromSettings(); state.FitLineSmoothness = mode; state.Apply();
    }
    static void SelectFigure(LineSmoothness mode)
    {
        var control = Outlet<NSSegmentedControl>("EmbeddedSplineInterpolationControl");
        control.SelectedSegment = (int)mode;
        Check(control.SendAction(control.Action, control.Target), "Archived figure-control action did not dispatch");
        Check((int)FinalFigureGraphView.FitLineSmoothness == (int)mode, "Figure control did not change session mode");
    }
    static T Outlet<T>(string name) => (T)typeof(FinalFigureViewController).GetProperty(name, Instance).GetValue(controller);
    static PublicationFigureOptions Capture() => (PublicationFigureOptions)typeof(FinalFigureViewController).Assembly
        .GetType("AnalysisITC.SupportingFigureCanvasWindowController", true).GetMethod("SnapshotFinalFigureOptions", Static).Invoke(null, null);
    static void Verify(LineSmoothness session, LineSmoothness saved)
    {
        Check(AppSettings.FitLineSmoothness == saved && PreferencesState.FromSettings().FitLineSmoothness == saved, "Saved preference changed");
        Check((int)FinalFigureGraphView.FitLineSmoothness == (int)session, "Native session mode did not update");
        Check(Outlet<NSSegmentedControl>("EmbeddedSplineInterpolationControl").SelectedSegment == (int)session, "Figure selector did not update immediately");
        var graphView = Outlet<FinalFigureGraphView>("FinalFigureGraph");
        var preview = (FinalFigure)typeof(FinalFigureGraphView).GetField("graph", Instance).GetValue(graphView);
        var exported = FinalFigureGraphView.SetupForExport(data);
        Check((int)preview.LineSmoothness == (int)session && (int)exported.LineSmoothness == (int)session, "Preview or export retained stale smoothness");
        Check(Capture().FitLineSmoothness == session, "Supporting Figure captured stale smoothness");
    }
    static string Pixels(FinalFigure figure)
    {
        using (var space = CoreGraphics.CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CoreGraphics.CGBitmapContext(IntPtr.Zero, 1000, 1100, 8, 4000, space, CoreGraphics.CGImageAlphaInfo.PremultipliedLast))
        {
            bitmap.SetFillColor(NSColor.White.CGColor); bitmap.FillRect(new CoreGraphics.CGRect(0, 0, 1000, 1100));
            var scale = Math.Min(950 / figure.PrintBox.Width, 1050 / figure.PrintBox.Height);
            bitmap.ScaleCTM((nfloat)scale, (nfloat)scale);
            figure.Draw(bitmap, new CoreGraphics.CGPoint(500 / scale, 550 / scale));
            using (var image = bitmap.ToImage())
            using (var representation = new NSBitmapImageRep(image))
            using (var png = representation.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary()))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(png.ToArray()));
        }
    }
    static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
        Check(task.IsCompleted, "Save timed out"); task.GetAwaiter().GetResult();
    }
    static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL: " + name + ": " + error); }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    sealed class PreferencesEditor : IDisposable
    {
        readonly NSBundle resources;
        readonly NSStoryboard storyboard;
        readonly NSWindowController window;
        readonly object exportPane;
        readonly NSPopUpButton popup;
        public PreferencesEditor()
        {
            resources = NSBundle.FromPath(resourcesPath); storyboard = NSStoryboard.FromName("Preferences", resources);
            window = (NSWindowController)storyboard.InstantiateControllerWithIdentifier("PreferencesWindowController");
            Check(window.Window != null, "Preferences window did not load");
            window.GetType().GetMethod("LoadState", Instance).Invoke(window, new object[] { PreferencesState.FromSettings() });
            var panes = (Array)window.GetType().GetField("panes", Instance).GetValue(window);
            exportPane = panes.Cast<object>().Single(pane => pane.GetType().GetProperty("FitLinePopup", Instance) != null);
            popup = (NSPopUpButton)exportPane.GetType().GetProperty("FitLinePopup", Instance).GetValue(exportPane);
        }
        public LineSmoothness Selected => (LineSmoothness)(int)popup.SelectedItem.Tag;
        public void Select(LineSmoothness mode) => popup.SelectItemWithTag((int)mode);
        public void Apply() => window.GetType().GetMethod("ApplyPreferences", Instance).Invoke(window, null);
        public void Cancel() => window.GetType().GetMethod("CancelPreferences", Instance).Invoke(window, null);
        public void RejectWidth() => ((NSTextField)exportPane.GetType().GetProperty("FigureWidthField", Instance).GetValue(exportPane)).StringValue = "invalid width";
        public void Dispose() { window.Dispose(); storyboard.Dispose(); resources.Dispose(); }
    }
    sealed class FixedPrompt : IFileSavePromptService
    {
        readonly string path;
        public FixedPrompt(string path) { this.path = path; }
        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes) => Task.FromResult(path);
    }
    static ExperimentData Experiment()
    {
        var data = new ExperimentData("content-controls.itc") { CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6), CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25 };
        data.DataPoints.Add(new DataPoint(0, 1e-6f)); data.DataPoints.Add(new DataPoint(40, 1e-6f));
        for (int i = 0; i < 5; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, new FloatWithError(8.4e-10), true) {
                ActualCellConcentration = 35e-6, ActualTitrantConcentration = (i + 1) * 5e-6, Ratio = (i + 1) / 7d };
            injection.SetPeakArea(new FloatWithError(-1e-5, 1e-8)); data.Injections.Add(injection);
        }
        var model = new OneSetOfSites(data); model.InitializeParameters(data);
        model.Parameters.Table[ParameterType.Enthalpy1].Update(-35000);
        model.Parameters.Table[ParameterType.Affinity1].Update(6);
        model.Parameters.Table[ParameterType.Nvalue1].Update(1);
        model.Parameters.Table[ParameterType.Offset].Update(2000);
        var converged = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { Termination = SolverTermination.Converged, Loss = 1e-6 });
        model.Solution = SolutionInterface.FromModel(model, converged); data.Model = model;
        return data;
    }

}
