using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.UI.MacOS;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Platform;
using AnalysisITC.Platform.MacOS;

static class SettingsUpdateTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        var dispatcher = PlatformServices.MainThreadDispatcher;
        var store = PlatformServices.SettingsStore;
        var prompt = PlatformServices.FileSavePromptService;
        var context = SynchronizationContext.Current;
        var directory = Path.Combine(Path.GetTempPath(), "ftitc-native-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
            PlatformServices.RegisterMainThreadDispatcher(new MacMainThreadDispatcher());
            if (SynchronizationContext.Current == null)
            {
                var cocoa = typeof(NSApplication).Assembly.GetType("AppKit.AppKitSynchronizationContext", true);
                SynchronizationContext.SetSynchronizationContext((SynchronizationContext)Activator.CreateInstance(cocoa, true));
            }
            PreferencesState.Defaults().Apply();
            AppSettings.DefaultSolverAlgorithm = SolverAlgorithm.NelderMead;
            AppSettings.DefaultErrorEstimationMethod = ErrorEstimationMethod.None;
            AppSettings.DefaultBootstrapIterations = 10;
            AppSettings.IncludeConcentrationErrorsInBootstrap = false;
            AppSettings.UseInjectionErrorWeightedFitting = false;
            AppSettings.CreateSingleAnalysisResult = false;
            AppSettings.SkipUncertaintyEstimationForNoBinding = false;
            DocumentDirtyTracker.Initialize();
            DataManager.Clear(DataClearMode.ResetSession);
            var data = Experiment();
            DataManager.AddData(data);
            DataManager.SelectIndex(0);
            MacSettingsApplier.Register();
            FittingOptionsController.BootstrapIterations = 500;
            FittingOptionsController.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            FittingOptionsController.Algorithm = SolverAlgorithm.LevenbergMarquardt;
            var countFailures = new List<string>();
            using (var sidebar = CreateController<SideBarViewController>())
            using (var analysis = CreateController<DataAnalysisViewController>())
            {
                analysis.Workspace.SetModelType(AnalysisModel.OneSetOfSites);
                var menu = Outlet<NSPopUpButton>(analysis, "ModelTypeControl");
                menu.SelectItem(menu.Items().Single(item => item.Tag == (int)AnalysisModel.OneSetOfSites));
                CountCase(countFailures, "initial controls", () => CheckCountAndFit(analysis, 500));
                foreach (var applyPreferences in new[] { false, true })
                {
                    SetLiveOptions();
                    analysis.ViewWillAppear();
                    var path = Path.Combine(directory, applyPreferences ? "preferences.ftxtc" : "save-as.ftxtc");
                    PlatformServices.RegisterFileSavePromptService(new FixedPrompt(path));
                    var saved = ProjectWriter.SaveAsync();
                    Wait(saved);
                    Check(saved.Result && File.Exists(path), "Save As did not write a project");
                    Check(AppSettings.LastDocumentPath == path, "Save As did not persist the selected path");
                    if (applyPreferences)
                    {
                        var staged = PreferencesState.FromSettings();
                        staged.DefaultErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
                        staged.DefaultBootstrapIterations = 1000;
                        staged.Apply();
                    }
                    analysis.ViewWillDisappear();
                    analysis.ViewWillAppear();
                    CheckControls(analysis);
                    SolverInterface started = null;
                    EventHandler<TerminationFlag> onStarted = (sender, _) => started = (SolverInterface)sender;
                    SolverInterface.AnalysisStarted += onStarted;
                    try
                    {
                        typeof(DataAnalysisViewController).GetMethod("RunFit", Instance).Invoke(analysis, null);
                        Check(started != null, "The next native fit did not start");
                        Check(started.SolverAlgorithm == SolverAlgorithm.LevenbergMarquardt, "The next fit lost its optimizer");
                        Check(started.ErrorEstimationMethod == ErrorEstimationMethod.LeaveOneOut, "The next fit lost its uncertainty method");
                        Check(started.BootstrapIterations == 100 && started.UseErrorWeightedFitting,
                            "The next fit lost its iteration count or weighting");
                    }
                    finally
                    {
                        SolverInterface.AnalysisStarted -= onStarted;
                        WaitForIdle();
                    }
                    Console.WriteLine("PASS: native Save As, view reentry and next fit; Preferences Apply=" + applyPreferences);
                }

                CheckBackgroundUpdates();
                AppSettings.ShowResidualGraph = !AppSettings.ShowResidualGraph;
                AppSettings.Save();
                Check(FinalFigureGraphView.ShowResiduals == AppSettings.ShowResidualGraph, "A normal display preference stopped applying");
                FittingOptionsController.ResetToPreferenceDefaults();
                ModelFactory.ResetStoredAnalysisState();
                DataAnalysisViewController.ResetStoredAnalysisState();
                Check(Outlet<NSPopUpButton>(analysis, "ErrorMethodControl").IndexOfSelectedItem == 1,
                    "Restore defaults did not load the newly saved uncertainty method");
                Check(FittingOptionsController.BootstrapIterations == 1000
                    && Outlet<NSTextField>(analysis, "ErrorIterationLabel").IntValue == 1000,
                    "Restore defaults did not load the newly saved iteration count");
                Check(Outlet<NSButton>(analysis, "UnlockParametersForErrorEstimationControl").State == NSCellStateValue.Off,
                    "Restore defaults did not clear parameter unlocking");
                Console.WriteLine("PASS: native display updates and explicit Analysis restore defaults");
                CheckBootstrapCounts(analysis, countFailures);
            }
            Check(countFailures.Count == 0, countFailures.Count + " bootstrap-count regression(s) failed:\n" + string.Join("\n", countFailures));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
            DocumentDirtyTracker.MarkClean();
            PlatformServices.RegisterMainThreadDispatcher(dispatcher);
            PlatformServices.RegisterSettingsStore(store);
            PlatformServices.RegisterFileSavePromptService(prompt);
            SynchronizationContext.SetSynchronizationContext(context);
            Directory.Delete(directory, true);
        }
    }

    static void CheckBootstrapCounts(DataAnalysisViewController controller, List<string> failures)
    {
        AppSettings.DefaultErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
        AppSettings.DefaultSolverAlgorithm = SolverAlgorithm.LevenbergMarquardt;
        foreach (var count in FittingOptionsController.BootstrapIterationPresets.Concat(new[] { 137, 1234, 0, 1, 25000 }))
        foreach (var scenario in new[] { "untouched", "view reentry", "restore defaults" })
        {
            CountCase(failures, scenario + ", count=" + count, () =>
            {
                SetCount(controller, count);
                if (scenario == "view reentry")
                {
                    controller.ViewWillDisappear();
                    controller.ViewWillAppear();
                }
                else if (scenario == "restore defaults")
                {
                    AppSettings.DefaultBootstrapIterations = count;
                    AppSettings.Save();
                    FittingOptionsController.BootstrapIterations = 100;
                    RestoreAnalysisDefaults();
                }
                CheckCountAndFit(controller, count);
            });
        }

        // These positions represent an exact midpoint, fractional counts, and the endpoints.
        var positions = new[] { 1.0, 4.0, 1.0211892990699381, Math.Log10(137.25), Math.Log10(137.75), Math.Log10(500) };
        var expectedCounts = new[] { 10, 10000, 11, 137, 138, 500 };
        for (var index = 0; index < positions.Length; index++)
        {
            var position = positions[index];
            var expected = expectedCounts[index];
            CountCase(failures, "slider edit, count=" + expected, () =>
            {
                SetCount(controller, 1234);
                MoveCountSlider(controller, position);
                Check(FittingOptionsController.BootstrapIterations == 1234, "Dragging committed the draft before Run Fit");
                CheckCountAndFit(controller, expected, checkPosition: false);
                Check(FittingOptionsController.BootstrapIterations == expected, "Run Fit did not commit the slider draft");
            });
        }

        CountCase(failures, "uncommitted edit and view reentry", () =>
        {
            SetCount(controller, 500);
            MoveCountSlider(controller, Math.Log10(137.75));
            controller.ViewWillDisappear();
            controller.ViewWillAppear();
            CheckCountAndFit(controller, 500);
        });
        CountCase(failures, "save and Preferences Apply retain the draft", () =>
        {
            SetCount(controller, 500);
            MoveCountSlider(controller, Math.Log10(137.75));
            AppSettings.Save();
            var preferences = PreferencesState.FromSettings();
            preferences.DefaultBootstrapIterations = 2000;
            preferences.Apply();
            Check(FittingOptionsController.BootstrapIterations == 500, "Preferences Apply replaced the live count");
            CheckCountAndFit(controller, 138, checkPosition: false);
            Check(AppSettings.DefaultBootstrapIterations == 2000, "Run Fit overwrote the saved default");
        });
        CountCase(failures, "restore defaults discards a slider draft", () =>
        {
            SetCount(controller, 137);
            MoveCountSlider(controller, Math.Log10(500));
            AppSettings.DefaultBootstrapIterations = 1234;
            AppSettings.Save();
            RestoreAnalysisDefaults();
            CheckCountAndFit(controller, 1234);
        });

        foreach (var method in new[] { ErrorEstimationMethod.None, ErrorEstimationMethod.LeaveOneOut,
            ErrorEstimationMethod.ProfileLikelihood, ErrorEstimationMethod.BootstrapResiduals })
        {
            CountCase(failures, "uncertainty method=" + method, () =>
            {
                SetCount(controller, 500);
                var popup = Outlet<NSPopUpButton>(controller, "ErrorMethodControl");
                popup.SelectItem((int)method);
                Check(popup.SendAction(popup.Action, popup.Target), "The uncertainty-method action was not dispatched");
                Check(Outlet<NSSlider>(controller, "ErrorIterationsControl").Enabled
                    == (method == ErrorEstimationMethod.BootstrapResiduals), "Iteration availability changed incorrectly");
                CheckCountAndFit(controller, 500, method: method);
            });
        }

        var second = Experiment();
        second.Name = "Second count experiment";
        DataManager.AddData(second);
        var mode = Outlet<NSSegmentedControl>(controller, "AnalysisModeControl");
        mode.SelectedSegment = 1;
        typeof(DataAnalysisViewController).GetMethod("AnalysisModeClicked", Instance).Invoke(controller, new object[] { mode });
        foreach (var count in new[] { 500, 1234 })
        {
            CountCase(failures, "global fit, count=" + count, () =>
            {
                SetCount(controller, count);
                CheckCountAndFit(controller, count, global: true);
            });
        }
    }

    static void SetCount(DataAnalysisViewController controller, int count)
    {
        FittingOptionsController.BootstrapIterations = count;
        FittingOptionsController.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
        FittingOptionsController.Algorithm = SolverAlgorithm.LevenbergMarquardt;
        FittingOptionsController.UseErrorWeightedFitting = false;
        FittingOptionsController.IncludeConcentrationVariance = false;
        FittingOptionsController.UnlockBootstrapParameters = false;
        controller.ViewWillAppear();
    }

    static void RestoreAnalysisDefaults()
    {
        FittingOptionsController.ResetToPreferenceDefaults();
        ModelFactory.ResetStoredAnalysisState();
        DataAnalysisViewController.ResetStoredAnalysisState();
    }

    static void MoveCountSlider(DataAnalysisViewController controller, double position)
    {
        var slider = Outlet<NSSlider>(controller, "ErrorIterationsControl");
        slider.DoubleValue = position;
        Check(slider.SendAction(slider.Action, slider.Target), "The iteration slider action was not dispatched");
    }

    static void CheckCountAndFit(DataAnalysisViewController controller, int expected,
        bool checkPosition = true, ErrorEstimationMethod method = ErrorEstimationMethod.BootstrapResiduals, bool global = false)
    {
        var displayed = Outlet<NSTextField>(controller, "ErrorIterationLabel").IntValue;
        var position = Outlet<NSSlider>(controller, "ErrorIterationsControl").DoubleValue;
        SolverInterface started = null;
        EventHandler<TerminationFlag> capture = (sender, stop) => { started = (SolverInterface)sender; stop.Raise(); };
        SolverInterface.AnalysisStarted += capture;
        try
        {
            typeof(DataAnalysisViewController).GetMethod("RunFit", Instance).Invoke(controller, null);
            Check(started != null, "The next count-validation fit did not start");
        }
        finally
        {
            SolverInterface.AnalysisStarted -= capture;
            WaitForIdle();
        }
        Console.WriteLine("COUNT: expected=" + expected + ", displayed=" + displayed + ", solver=" + started.BootstrapIterations);
        Check(started.BootstrapIterations == expected, "Started solver received " + started.BootstrapIterations + " instead of " + expected);
        Check(displayed == expected, "Displayed count was " + displayed + " instead of " + expected);
        Check(started.ErrorEstimationMethod == method, "The fit used a different uncertainty method");
        Check((started is GlobalSolver) == global, "The fit used a different analysis mode");
        if (checkPosition)
        {
            var expectedPosition = expected <= 10 ? 1 : expected >= 10000 ? 4 : Math.Log10(expected);
            Check(Math.Abs(position - expectedPosition) < 1e-12, "The count's slider position was incorrect");
        }
    }

    static void CountCase(List<string> failures, string name, Action test)
    {
        try { test(); Console.WriteLine("PASS: bootstrap count " + name); }
        catch (Exception error)
        {
            failures.Add(name + ": " + error.Message);
            Console.Error.WriteLine("FAIL: bootstrap count " + name + ": " + error.Message);
        }
    }

    static void CheckBackgroundUpdates()
    {
        SetLiveOptions();
        var events = new List<string>();
        EventHandler applied = (_, __) => { Check(NSThread.IsMain, "SettingsApplied ran outside the main thread"); events.Add("applied"); };
        EventHandler updated = (_, __) => { Check(NSThread.IsMain, "SettingsDidUpdate ran outside the main thread"); events.Add("updated"); };
        AppSettings.SettingsApplied += applied;
        AppSettings.SettingsDidUpdate += updated;
        try
        {
            AppSettings.InterpretationPublicClientCode = "";
            using (var http = new HttpClient(new EnrollmentHandler()))
            {
                var client = new FtItcInterpretationClient(http, new Uri("https://example.invalid"));
                Wait(Task.Run(() => client.GetInterpretationOptionsAsync("")));
            }
            Check(events.SequenceEqual(new[] { "applied", "updated" }), "Enrollment notification order changed");
            events.Clear();
            Wait(Task.Run(() => AppSettings.PersistInterpretationAccessVerification("test-code",
                new InterpretationOperatorOptionsResponse())));
            Wait(Task.Run(() => AppSettings.PersistInterpretationAccount("test-code", new InterpretationAccountResponse())));
            Check(events.SequenceEqual(new[] { "updated", "updated" }), "Credential/account notifications were lost");
            Check(FittingOptionsController.ErrorEstimationMethod == ErrorEstimationMethod.LeaveOneOut
                && FittingOptionsController.BootstrapIterations == 100 && FittingOptionsController.UnlockBootstrapParameters,
                "Background updates changed live fitting options");
            Console.WriteLine("PASS: background enrollment and account updates with native sidebar and Analysis subscribers");
        }
        finally
        {
            AppSettings.SettingsApplied -= applied;
            AppSettings.SettingsDidUpdate -= updated;
        }
    }

    static void SetLiveOptions()
    {
        FittingOptionsController.Algorithm = SolverAlgorithm.LevenbergMarquardt;
        FittingOptionsController.ErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut;
        FittingOptionsController.BootstrapIterations = 100;
        FittingOptionsController.IncludeConcentrationVariance = true;
        FittingOptionsController.UseErrorWeightedFitting = true;
        FittingOptionsController.UnlockBootstrapParameters = true;
    }

    static void CheckControls(DataAnalysisViewController controller)
    {
        Check(Outlet<NSPopUpButton>(controller, "SolverAlgorithmControl").IndexOfSelectedItem == 1, "Reentry lost the optimizer");
        Check(Outlet<NSPopUpButton>(controller, "ErrorMethodControl").IndexOfSelectedItem == 2, "Reentry lost the uncertainty method");
        Check(Outlet<NSTextField>(controller, "ErrorIterationLabel").IntValue == 100, "Reentry lost the iteration count");
        foreach (var name in new[] { "IncludeConcErrorControl", "UseWeightedControl", "UnlockParametersForErrorEstimationControl" })
            Check(Outlet<NSButton>(controller, name).State == NSCellStateValue.On, "Reentry cleared " + name);
    }

    static T CreateController<T>() where T : NSViewController
    {
        var controller = (T)Create(typeof(T));
        controller.View = new NSView(new CGRect(0, 0, 800, 600));
        foreach (var outlet in typeof(T).GetProperties(Instance))
            if (outlet.GetCustomAttribute<OutletAttribute>() != null)
            {
                var control = Create(outlet.PropertyType);
                outlet.SetValue(controller, control);
                var view = control as NSView;
                if (view != null) controller.View.AddSubview(view);
                var segmented = control as NSSegmentedControl;
                if (segmented != null) { segmented.SegmentCount = 2; segmented.SelectedSegment = 0; }
                var popup = control as NSPopUpButton;
                if (popup != null) popup.Menu = new NSMenu();
                var tabs = control as NSTabView;
                if (tabs != null) for (var i = 0; i < 4; i++) tabs.Add(new NSTabViewItem());
            }
        if (controller is DataAnalysisViewController)
        {
            Outlet<NSPopUpButton>(controller, "ErrorMethodControl").AddItems(new[] { "None", "Bootstrap residuals", "Leave-one-out", "Profile likelihood" });
            Outlet<NSPopUpButton>(controller, "SolverAlgorithmControl").AddItems(new[] { "Nelder-Mead", "Levenberg-Marquardt" });
            var slider = Outlet<NSSlider>(controller, "ErrorIterationsControl");
            slider.MinValue = 1;
            slider.MaxValue = 4;
            slider.Target = controller;
            slider.Action = new Selector("ErrorIterationSliderChanged:");
        }
        controller.ViewDidLoad();
        return controller;
    }

    static NSObject Create(Type type) => Runtime.GetNSObject(
        Send(Send(new Class(type).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init")));
    static T Outlet<T>(NSViewController controller, string name) => (T)controller.GetType().GetProperty(name, Instance).GetValue(controller);
    static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
        Check(task.IsCompleted, "Timed out waiting for native operation");
        task.GetAwaiter().GetResult();
    }
    static void WaitForIdle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (ProcessingActivity.IsBusy && DateTime.UtcNow < deadline)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
        Check(!ProcessingActivity.IsBusy, "Native fitting did not release admission");
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    static ExperimentData Experiment()
    {
        var data = new ExperimentData("settings.itc")
        {
            CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25
        };
        for (var i = 0; i < 5; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, new FloatWithError(420e-6 * 2e-6), true)
            {
                ActualCellConcentration = data.CellConcentration * 0.99,
                ActualTitrantConcentration = (i + 1) * 5e-6,
                Ratio = (i + 1) * 5e-6 / (35e-6 * 0.99)
            };
            injection.SetPeakArea(new FloatWithError(2000 * injection.InjectionMass, 1e-8));
            data.Injections.Add(injection);
        }
        return data;
    }
    sealed class FixedPrompt : IFileSavePromptService
    {
        readonly string path;
        public FixedPrompt(string path) { this.path = path; }
        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes) => Task.FromResult(path);
    }
    sealed class EnrollmentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Post
                    ? "{\"publicAccessCode\":\"ftitc_pub_native_test\"}"
                    : "{\"mode\":\"presets\",\"presets\":[{\"id\":\"instant\",\"name\":\"Fast\"}]}")
            };
        }
    }
}
