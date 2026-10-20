// Exercises the archived Publish controls, saved defaults and native figure construction.
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

static class FinalFigureContentControlsTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const FinalFigureDisplayParameters Parameters = FinalFigureDisplayParameters.Thermodynamic
        | FinalFigureDisplayParameters.Offset | FinalFigureDisplayParameters.Derived;
    const FinalFigureDisplayParameters Details = FinalFigureDisplayParameters.Temperature
        | FinalFigureDisplayParameters.Concentrations | FinalFigureDisplayParameters.InjectionDelay
        | FinalFigureDisplayParameters.Instrument | FinalFigureDisplayParameters.Attributes;
    static int failures, passed;

    static int Main(string[] args)
    {
        NSApplication.Init();
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        PlatformServices.RegisterMainThreadDispatcher(new MacMainThreadDispatcher());
        MacSettingsApplier.Register();
        PreferencesState.Defaults().Apply();
        AppSettings.PerformOnlineChecksOnLaunch = false;
        new Class(typeof(FinalFigureGraphView));
        new Class(typeof(FinalFigureViewController));
        new Class(typeof(WorkspaceTabControl));
        new Class(typeof(HuggingTextView));
        using (var resources = NSBundle.FromPath(System.IO.Path.GetDirectoryName(args[1])))
        using (var storyboard = NSStoryboard.FromName("Main", resources))
        using (var controller = (FinalFigureViewController)storyboard.InstantiateControllerWithIdentifier("FinalFigureViewController"))
        {
            Check(controller.View != null, "Archived Publish view is missing");
            DataManager.Clear(DataClearMode.ResetSession);
            DataManager.AddData(Experiment());
            DataManager.SelectIndex(0);
            StateManager.SetProgramState(ProgramState.Publish);
            Check(StateManager.CurrentState == ProgramState.Publish, "Publish workflow is unavailable");
            for (int d = 0; d < 2; d++)
                for (int m = 0; m < 2; m++)
                    for (int p = 0; p < 2; p++)
                    {
                        var details = d != 0; var model = m != 0; var parameters = p != 0;
                        foreach (var load in new[] { false, true })
                            Run("defaults " + d + m + p + (load ? " reload" : " Apply"), () =>
                            {
                                Apply(details, model, parameters);
                                if (load) AppSettings.Load();
                                Check(AppSettings.FinalFigureShowDetailsAsDefault == details && AppSettings.FinalFigureShowModelInfoAsDefault == model
                                    && AppSettings.FinalFigureShowParameterBoxAsDefault == parameters, "Preference values changed");
                                Verify(controller, details, model, parameters, Parameters | Details);
                            });
                    }
            Run("independent archived switches and view refresh", () =>
            {
                foreach (var index in new[] { 0, 1, 2 })
                {
                    Apply(true, true, true);
                    var choices = new[] { true, true, true };
                    var control = Controls(controller)[index];
                    Click(control, false); choices[index] = false;
                    controller.ViewWillAppear();
                    Verify(controller, choices[0], choices[1], choices[2], Parameters | Details);
                    Click(control, true);
                    Verify(controller, true, true, true, Parameters | Details);
                }
            });
            Run("empty parameter selection and model-only box", () =>
            {
                Apply(true, false, true, Details);
                Verify(controller, true, false, true, Details);
                Click(Controls(controller)[1], true);
                Verify(controller, true, true, true, Details);
            });
            Run("content switches preserve selected subgroups", () =>
            {
                var selected = FinalFigureDisplayParameters.Affinity | FinalFigureDisplayParameters.Temperature;
                Apply(true, true, true, selected);
                foreach (var control in Controls(controller)) Click(control, false);
                Verify(controller, false, false, false, selected);
                foreach (var control in Controls(controller)) Click(control, true);
                Verify(controller, true, true, true, selected);
            });
            Run("Attributes menu retains detail and attribute actions", () =>
            {
                Apply(true, true, true);
                var menu = Outlet<NSMenu>(controller, "EmbeddedAttributeDisplayOptionsControl");
                Check(!menu.Items.Any(i => i.Title == "Model info"), "Model info remains duplicated in Attributes");
                var popup = Descendants(controller.View).OfType<NSPopUpButton>().Single(v => v.Menu.Handle == menu.Handle);
                foreach (var name in new[] { "Temperature", "Concentrations" })
                {
                    var flag = name == "Temperature" ? FinalFigureDisplayParameters.Temperature : FinalFigureDisplayParameters.Concentrations;
                    var before = AppSettings.FinalFigureParameterDisplay;
                    popup.SelectItem(menu.Items.Single(i => i.Title == name));
                    Check(popup.SendAction(popup.Action, popup.Target), "Attribute action did not dispatch");
                    Check(AppSettings.FinalFigureParameterDisplay == (before ^ flag), name + " toggled the wrong flag");
                    Check(Controls(controller)[1].State != 0, "Detail menu changed model visibility");
                }
                foreach (var pair in new[] {
                    Tuple.Create("Used in analysis", DisplayAttributeOptions.UsedInAnalysis),
                    Tuple.Create("Buffer", DisplayAttributeOptions.Buffer), Tuple.Create("Salt", DisplayAttributeOptions.Salt),
                    Tuple.Create("Ionic strength", DisplayAttributeOptions.IonicStrength),
                    Tuple.Create("Protonation enthalpy", DisplayAttributeOptions.ProtonationEnthalpy),
                    Tuple.Create("Competitor", DisplayAttributeOptions.Competitor) })
                {
                    var before = AppSettings.DisplayAttributeOptions;
                    popup.SelectItem(menu.Items.Single(i => i.Title == pair.Item1));
                    Check(popup.SendAction(popup.Action, popup.Target), "Attribute action did not dispatch");
                    Check(AppSettings.DisplayAttributeOptions == (before ^ pair.Item2), pair.Item1 + " toggled the wrong attribute");
                }
            });
            Run("Binding Null None retain model restrictions", () =>
            {
                Apply(true, true, true);
                var data = Experiment();
                foreach (var selection in new[] { PublicationFigureModelSelection.BindingModel, PublicationFigureModelSelection.NullModel, PublicationFigureModelSelection.None })
                {
                    var selector = Outlet<NSSegmentedControl>(controller, "EmbeddedPlottedModelControl");
                    selector.SelectedSegment = selection == PublicationFigureModelSelection.BindingModel ? 0
                        : selection == PublicationFigureModelSelection.NullModel ? 1 : 2;
                    Check(selector.SendAction(selector.Action, selector.Target), "Archived model action did not dispatch");
                    var controls = Controls(controller);
                    Check(controls[0].Enabled && controls[1].Enabled == (selection != PublicationFigureModelSelection.None)
                        && controls[2].Enabled == (selection != PublicationFigureModelSelection.None), "Wrong content-control model eligibility");
                    Check(controls.All(control => control.State != 0), "Model selection discarded content choices");
                    var figure = FinalFigureGraphView.SetupForExport(data, null, null, selection);
                    var fit = (DataFittingGraph)typeof(FinalFigure).GetField("IntegrationGraph", Instance).GetValue(figure);
                    Check(figure.DrawExpDetails, "Model selection suppressed experiment details");
                    Check(fit.ShowParameterBox == (selection != PublicationFigureModelSelection.None), "Wrong model box eligibility");
                    if (selection != PublicationFigureModelSelection.BindingModel)
                        Check(!fit.ShowParameterGuides && !fit.DrawConfidenceBands, "Null/None acquired binding guides or confidence");
                    if (selection == PublicationFigureModelSelection.None)
                        Check(!figure.ShowResiduals, "None acquired residuals");
                }
                var bindingSelector = Outlet<NSSegmentedControl>(controller, "EmbeddedPlottedModelControl");
                bindingSelector.SelectedSegment = 0;
                Check(bindingSelector.SendAction(bindingSelector.Action, bindingSelector.Target), "Binding selection did not dispatch");
            });
            Run("content rows have usable native layout", () =>
            {
                Apply(true, true, true);
                using (var window = new NSWindow(new CoreGraphics.CGRect(0, 0, 893, 646), NSWindowStyle.Titled, NSBackingStore.Buffered, false))
                {
                    window.ContentView = controller.View;
                    controller.View.LayoutSubtreeIfNeeded();
                    var controls = Controls(controller);
                    foreach (var control in controls)
                        Check(control.Frame.Width > 0 && control.Frame.Height > 0, "Content switch has no layout");
                    var positions = controls.Select(control => control.ConvertRectToView(control.Bounds, controller.View)).ToArray();
                    Check(positions.Select(rect => rect.Y).Distinct().Count() == 3, "Content switches overlap");
                    var directory = Environment.GetEnvironmentVariable("FTITC_FINAL_FIGURE_QA_DIRECTORY");
                    if (!string.IsNullOrEmpty(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory);
                        using (var bitmap = controller.View.BitmapImageRepForCachingDisplayInRect(controller.View.Bounds))
                        {
                            controller.View.CacheDisplay(controller.View.Bounds, bitmap);
                            bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
                                .Save(System.IO.Path.Combine(directory, "native-content-controls.png"), true);
                        }
                    }
                }
            });
        }
        DataManager.Clear(DataClearMode.ResetSession);
        Console.WriteLine(passed + " native content groups passed; " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    static void Apply(bool details, bool model, bool parameters, FinalFigureDisplayParameters selected = Parameters | Details)
    {
        var state = PreferencesState.FromSettings();
        state.FinalFigureShowDetailsAsDefault = details;
        state.FinalFigureShowModelInfoAsDefault = model;
        state.FinalFigureShowParameterBoxAsDefault = parameters;
        state.FinalFigureParameterDisplay = selected;
        state.Apply();
    }

    static void Verify(FinalFigureViewController controller, bool details, bool model, bool parameters, FinalFigureDisplayParameters selected)
    {
        var expected = (details ? selected & Details : 0) | (model ? FinalFigureDisplayParameters.Model : 0)
            | (parameters ? selected & Parameters : 0);
        Check(FinalFigureGraphView.DrawExpDetails == details, "Experiment details do not match their preference");
        Check(FinalFigureGraphView.VisibleFinalFigureDisplayParameters == expected, "Visible flags do not match independent choices");
        var controls = Controls(controller);
        Check((controls[0].State != 0) == details && (controls[1].State != 0) == model && (controls[2].State != 0) == parameters,
            "Archived switches do not reflect independent choices");
        foreach (var control in controls)
        {
            for (NSView view = control; view != null; view = view.Superview)
                Check(!view.Hidden, "Content switch has a hidden ancestor");
            Check(!string.IsNullOrWhiteSpace(control.ToolTip), "Content switch has no tooltip");
        }
        Check(controls[0].ToolTip == FinalFigureModelPresentation.ExperimentDetailsTooltip
            && controls[1].ToolTip == FinalFigureModelPresentation.ModelInfoTooltip
            && controls[2].ToolTip == FinalFigureModelPresentation.FitParametersTooltip, "Native controls do not use the shared tooltips");
        Check(AppSettings.FinalFigureParameterDisplay == selected, "Hiding a group discarded its selected flags");
        var box = model || (parameters && (selected & Parameters) != 0);
        var figure = FinalFigureGraphView.SetupForExport(Experiment(), null, null, PublicationFigureModelSelection.BindingModel);
        Check(figure.DrawExpDetails == details && figure.FinalFigureDisplayParameters == expected, "Native figure lost content choices");
        var fit = (DataFittingGraph)typeof(FinalFigure).GetField("IntegrationGraph", Instance).GetValue(figure);
        Check(fit.ShowParameterBox == box, "Native model/parameter box incorrectly depends on experiment details");
        FinalFigureGraphView.Invalidate();
        var graphView = Outlet<FinalFigureGraphView>(controller, "FinalFigureGraph");
        var preview = (FinalFigure)typeof(FinalFigureGraphView).GetField("graph", Instance).GetValue(graphView);
        Check(preview != null && preview.DrawExpDetails == details && preview.FinalFigureDisplayParameters == expected,
            "Live native preview lost content choices");
        var previewFit = (DataFittingGraph)typeof(FinalFigure).GetField("IntegrationGraph", Instance).GetValue(preview);
        Check(previewFit.ShowParameterBox == box, "Live native preview has wrong model/parameter box visibility");
        var capture = (PublicationFigureOptions)typeof(FinalFigureViewController).Assembly.GetType("AnalysisITC.SupportingFigureCanvasWindowController", true)
            .GetMethod("SnapshotFinalFigureOptions", Static).Invoke(null, null);
        Check(capture.ShowExperimentDetails == details && capture.ShowFitParameters == box && capture.DisplayParameters == expected,
            "Supporting Figure capture lost independent content choices");
    }

    static NSSwitch[] Controls(FinalFigureViewController controller) => new[] {
        Outlet<NSSwitch>(controller, "EmbeddedShowParametersControl"),
        Outlet<NSSwitch>(controller, "EmbeddedModelInfoControl"),
        Outlet<NSSwitch>(controller, "EmbeddedFitParametersControl") };
    static T Outlet<T>(FinalFigureViewController controller, string name)
    {
        var property = typeof(FinalFigureViewController).GetProperty(name, Instance);
        Check(property != null, "Missing archived outlet " + name);
        var value = (T)property.GetValue(controller);
        Check(value != null, "Unconnected archived outlet " + name);
        return value;
    }
    static System.Collections.Generic.IEnumerable<NSView> Descendants(NSView root)
    {
        foreach (var child in root.Subviews) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    static void Click(NSSwitch control, bool enabled)
    {
        control.State = enabled ? 1 : 0;
        Check(control.SendAction(control.Action, control.Target), "Archived switch action did not dispatch");
    }
    static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL: " + name + ": " + error.Message); }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

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
        var offset = new Offset(data); offset.InitializeParameters(data);
        offset.Parameters.Table[ParameterType.Offset].Update(1500);
        offset.Solution = SolutionInterface.FromModel(offset, converged);
        var comparison = new NullModelComparison { NullModelId = "offset", NullFitSucceeded = true, BindingFitSucceeded = true };
        comparison.NullSolutions.Add(offset.Solution);
        comparison.Members.Add(new NullModelComparisonMember { ExperimentId = data.UniqueID });
        typeof(SolutionInterface).GetProperty("NullComparison").GetSetMethod(true).Invoke(model.Solution, new object[] { comparison });
        return data;
    }
}
