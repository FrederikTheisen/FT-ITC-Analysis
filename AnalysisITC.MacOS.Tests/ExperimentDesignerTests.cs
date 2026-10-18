using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.UI.MacOS.CustomViews;

// Drives the real designer controller with code-created outlets and its compiled storyboard.
static class ExperimentDesignerTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        // Matches the culture the app sets at startup in Main.cs.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            AutomaticVolumeMatchesSchedule(ITCInstrument.MicroCalVPITC, 10, true, 34.3);
            AutomaticVolumeMatchesSchedule(ITCInstrument.MicroCalVPITC, 10, false, 31.0);
            AutomaticVolumeMatchesSchedule(ITCInstrument.MicroCalVPITC, 2, true, 309.5);
            AutomaticVolumeMatchesSchedule(ITCInstrument.TAInstrumentsITCStandard, 10, true, 27.7);
            AutomaticVolumeMatchesSchedule(ITCInstrument.TAInstrumentsITCLowVolume, 10, false, 25.0);
            AutomaticVolumeMatchesSchedule(ITCInstrument.MicroCalITC200, 2, true, 39.0);
            AutomaticVolumeMatchesSchedule(ITCInstrument.MalvernITC200, 2, false, 19.5);
            ManualVolumeHasNoUpperCeiling();
            ArchivedVolumeControlsMatchSchedule(args[1]);
            SetupAndModelChangesKeepEnteredValues(autoRun: true);
            SetupAndModelChangesKeepEnteredValues(autoRun: false);
            FitLocksControlsAndRestoresTheirStates();
            BusyOperationBlocksDesignerFit();
            Console.WriteLine("PASS: native designer volumes match the schedule, retain entered values and restore control states after fitting");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    static void AutomaticVolumeMatchesSchedule(ITCInstrument instrument, int count, bool smallFirst, double expectedMicroliters)
    {
        using (var controller = CreateController())
        {
            var instruments = Outlet<NSPopUpButton>(controller, "InstrumentControl");
            instruments.SelectItemWithTag((int)instrument);
            Invoke(controller, "InstrumentControlAction", instruments);
            Outlet<NSButton>(controller, "SmallInitialInjCheckmark").State = smallFirst ? NSCellStateValue.On : NSCellStateValue.Off;
            SetInjectionCount(controller, count);

            var field = Outlet<NSTextField>(controller, "InjectionVolumeField");
            var stepper = Outlet<NSStepper>(controller, "InjectionVolumeStepper");
            var context = instrument + ", count=" + count + ", small first=" + smallFirst;
            CheckSchedule(controller, count, smallFirst, expectedMicroliters);
            Check(Math.Abs(field.DoubleValue - expectedMicroliters) < 1e-10,
                "automatic field matches the simulated volume: " + context + ", displayed=" + field.DoubleValue);
            Check(Math.Abs(stepper.DoubleValue - expectedMicroliters) < 1e-10,
                "automatic stepper matches the simulated volume: " + context);
            Check(!field.Enabled && !stepper.Enabled, "automatic controls stay disabled");
            Check(Outlet<NSTextField>(controller, "InjectionInfoField").StringValue.Contains(expectedMicroliters.ToString("F1")),
                "schedule summary shows the effective volume");

            var auto = Outlet<NSButton>(controller, "AutoVolume");
            auto.State = NSCellStateValue.Off;
            Invoke(controller, "InjectionInputChanged", auto);
            Check(field.Enabled && stepper.Enabled, "manual controls become enabled");
            CheckSchedule(controller, count, smallFirst, expectedMicroliters);
            Check(Math.Abs(field.DoubleValue - expectedMicroliters) < 1e-10,
                "turning Auto off keeps the effective volume: " + context);
        }
    }

    static void ManualVolumeHasNoUpperCeiling()
    {
        using (var controller = CreateController())
        {
            var instruments = Outlet<NSPopUpButton>(controller, "InstrumentControl");
            instruments.SelectItemWithTag((int)ITCInstrument.MicroCalVPITC);
            Invoke(controller, "InstrumentControlAction", instruments);
            var auto = Outlet<NSButton>(controller, "AutoVolume");
            auto.State = NSCellStateValue.Off;
            Invoke(controller, "InjectionInputChanged", auto);
            SetInjectionCount(controller, 10);
            var field = Outlet<NSTextField>(controller, "InjectionVolumeField");
            var stepper = Outlet<NSStepper>(controller, "InjectionVolumeStepper");
            field.DoubleValue = 34.3;
            Invoke(controller, "InjectionVolumeField_Changed", field, EventArgs.Empty);
            CheckSchedule(controller, 10, true, 34.3);
            Check(Math.Abs(stepper.DoubleValue - 34.3) < 1e-10, "manual field synchronizes the stepper above 20 µL");

            stepper.DoubleValue = 500;
            Invoke(controller, "InjectionVolumeStepper_Activated", stepper, EventArgs.Empty);
            CheckSchedule(controller, 10, true, 500);
            Check(field.DoubleValue == 500, "stepper permits values above every instrument's syringe capacity");

            field.DoubleValue = 0.05;
            Invoke(controller, "InjectionVolumeField_Changed", field, EventArgs.Empty);
            CheckSchedule(controller, 10, true, 0.1);
            Check(field.DoubleValue == 0.1 && stepper.DoubleValue == 0.1, "manual minimum remains 0.1 µL");
        }
    }

    static void CheckSchedule(ExperimentDesignerViewController2 controller, int count, bool smallFirst, double expectedMicroliters)
    {
        var injections = Factory(controller).Model.Data.Injections;
        Check(injections.Count == count, "schedule has the requested injection count");
        for (int i = 0; i < count; i++)
        {
            var expected = (i == 0 && smallFirst ? 0.5 : expectedMicroliters) / 1e6;
            Check(Math.Abs(injections[i].Volume - expected) < 1e-16, "simulated volume matches the expected schedule, injection=" + i);
        }
    }

    static void ArchivedVolumeControlsMatchSchedule(string storyboardPath)
    {
        using (var resources = NSBundle.FromPath(Path.GetDirectoryName(storyboardPath)))
        using (var storyboard = NSStoryboard.FromName("Main", resources))
        using (var window = (NSWindowController)storyboard.InstantiateControllerWithIdentifier("NSWindowController-z8n-GP-VNe"))
        {
            var controller = (ExperimentDesignerViewController2)window.ContentViewController;
            Check(controller.View != null, "archived designer view loads");
            try
            {
                var instruments = Outlet<NSPopUpButton>(controller, "InstrumentControl");
                instruments.SelectItemWithTag((int)ITCInstrument.MicroCalVPITC);
                Dispatch(instruments);
                SetInjectionCount(controller, 10);
                var field = Outlet<NSTextField>(controller, "InjectionVolumeField");
                var stepper = Outlet<NSStepper>(controller, "InjectionVolumeStepper");
                Check(Math.Abs(field.DoubleValue - 34.3) < 1e-10, "archived primary field shows 34.3 µL");
                CheckSchedule(controller, 10, true, 34.3);

                Outlet<NSButton>(controller, "AutoVolume").PerformClick(controller);
                Check(field.Enabled && stepper.Enabled, "archived Auto action enables manual volume");
                CheckSchedule(controller, 10, true, 34.3);

                stepper.DoubleValue = 34.55;
                Dispatch(stepper);
                Check(Math.Abs(field.DoubleValue - 34.55) < 1e-10, "archived stepper action updates the primary field above 20 µL");
                CheckSchedule(controller, 10, true, 34.55);

                field.DoubleValue = 500;
                Dispatch(field);
                Invoke(controller, "InjectionVolumeField_Changed", field, EventArgs.Empty);
                Check(stepper.DoubleValue == 500, "archived field action retains a manual value above syringe capacity");
                CheckSchedule(controller, 10, true, 500);
            }
            finally
            {
                Invoke(controller, "ReleaseSubscriptions");
                controller.Dispose();
            }
        }
    }

    static void Dispatch(NSControl control) =>
        Check(NSApplication.SharedApplication.SendAction(control.Action, control.Target, control), "archived control action dispatches");

    static void BusyOperationBlocksDesignerFit()
    {
        using (var controller = CreateController())
        {
            var factory = Factory(controller);
            var fit = Outlet<NSButton>(controller, "ApplyModelButton");
            var concentration = Outlet<NSTextField>(controller, "CellConcField");
            var label = Outlet<NSTextField>(controller, "InstrumentDescriptionField");
            concentration.ToolTip = "Designer concentration draft";
            label.ToolTip = "Instrument description";
            var concentrationEnabled = concentration.Enabled;
            var labelEnabled = label.Enabled;
            using (var activity = ProcessingActivity.TryBegin())
            using (activity.Enter())
            {
                Check(!fit.Enabled && fit.ToolTip == FittingStatusPresentation.BusyTooltip,
                    "designer fit was not disabled for another calculation");
                Check(concentration.Enabled == concentrationEnabled && label.Enabled == labelEnabled
                    && concentration.ToolTip == "Designer concentration draft" && label.ToolTip == "Instrument description",
                    "unrelated work locked designer drafts or rewrote label tooltips");
                Invoke(controller, "ApplyModelSettings", fit);
                Check(ReferenceEquals(factory, Factory(controller))
                    && controller.GetType().GetField("FittingSolver", Instance).GetValue(controller) == null,
                    "refused designer fit prepared or started a solver");
            }
            Check(fit.Enabled && fit.ToolTip != FittingStatusPresentation.BusyTooltip,
                "designer fit did not restore its idle state");
        }
    }

    static void SetupAndModelChangesKeepEnteredValues(bool autoRun)
    {
        var previousAutoRun = ExperimentDesignerViewController2.AutoRunExperimentSimulation;
        ExperimentDesignerViewController2.AutoRunExperimentSimulation = autoRun;
        try
        {
            var controller = CreateController();
            Enter(controller, ParameterType.Nvalue1, "1.7");
            var offset = Enter(controller, ParameterType.Offset, "1.234");
            Check((Value(Factory(controller), ParameterType.Offset) == offset) == autoRun,
                "entered offset is simulated only with automatic simulation, autoRun=" + autoRun);

            SetInjectionCount(controller, 21);

            var factory = Factory(controller);
            Check(factory.Model.Data.Injections.Count == 21, "setup change rebuilt the experiment");
            Check(Value(factory, ParameterType.Nvalue1) == 1.7, "N kept after setup change, autoRun=" + autoRun);
            Check(Value(factory, ParameterType.Offset) == offset, "offset kept after setup change, autoRun=" + autoRun);
            Check(Value(factory, ParameterType.Enthalpy1) == -30000, "untouched enthalpy uses the designer default");

            foreach (var control in ParameterControls(controller))
                Check(ReferenceEquals(control.Parameter, factory.Model.Parameters.Table[control.Key]),
                    "control bound to the current model parameter: " + control.Key);
            Check(InputField(Control(controller, ParameterType.Nvalue1)).StringValue == "1.700", "entered N shown as field text");
            Check(InputField(Control(controller, ParameterType.Enthalpy1)).StringValue == "", "default enthalpy stays a placeholder");

            SelectModel(controller, AnalysisModel.Dissociation);
            Check(Value(Factory(controller), ParameterType.Offset) == offset, "offset shared with the dissociation model");
            SelectModel(controller, AnalysisModel.OneSetOfSites);
            Check(Value(Factory(controller), ParameterType.Nvalue1) == 1.7, "N restored after returning to one set of sites");
        }
        finally
        {
            ExperimentDesignerViewController2.AutoRunExperimentSimulation = previousAutoRun;
        }
    }

    static void FitLocksControlsAndRestoresTheirStates()
    {
        var controller = CreateController();
        Enter(controller, ParameterType.Nvalue1, "1.7");
        var factory = Factory(controller);
        var injectionVolume = Outlet<NSTextField>(controller, "InjectionVolumeField");
        var noiseLevel = Outlet<NSSlider>(controller, "NoiseLevelSlider");
        var cell = Outlet<NSTextField>(controller, "CellConcField");
        var nInput = InputField(Control(controller, ParameterType.Nvalue1));
        Check(!injectionVolume.Enabled && !noiseLevel.Enabled, "automatic volume and noise disable their controls");
        Check(cell.Enabled && nInput.Enabled, "inputs enabled before fitting");

        var solver = new Solver();
        Invoke(controller, "BeginFit", solver);

        Check(!cell.Enabled && !nInput.Enabled && !Outlet<NSButton>(controller, "ApplyModelButton").Enabled, "inputs disabled while fitting");
        SetInjectionCount(controller, 25);
        Check(ReferenceEquals(factory, Factory(controller)) && factory.Model.Data.Injections.Count == 20, "setup unchanged while fitting");

        // The fit writes its estimates to the generation model.
        factory.UpdateParameter(ParameterType.Nvalue1, 3.3, false);
        factory.UpdateParameter(ParameterType.Affinity1, 9.5, false);

        new Solver().ReportAnalysisFinished(SolverConvergence.ReportStopped(DateTime.Now));
        Check(!cell.Enabled, "an unrelated fit does not unlock the designer");

        solver.ReportAnalysisFinished(SolverConvergence.ReportStopped(DateTime.Now));
        Check(cell.Enabled && nInput.Enabled && Outlet<NSButton>(controller, "ApplyModelButton").Enabled, "inputs enabled after fitting");
        Check(!injectionVolume.Enabled && !noiseLevel.Enabled, "controls disabled before the fit stay disabled");

        Invoke(controller, "UpdateSyntheticData");
        Check(Value(factory, ParameterType.Nvalue1) == 1.7, "fitted N is not used for the next simulation");
        Check(Value(factory, ParameterType.Affinity1) != 9.5, "fitted affinity is not used for the next simulation");
    }

    static ExperimentDesignerViewController2 CreateController()
    {
        var controller = Create<ExperimentDesignerViewController2>();
        controller.View = new NSView(new CGRect(0, 0, 800, 600));

        var outlets = typeof(ExperimentDesignerViewController2).GetProperties(Instance)
            .Where(property => property.GetCustomAttribute<OutletAttribute>() != null)
            .ToList();
        foreach (var outlet in outlets.Where(property => property.PropertyType != typeof(NSMenu)))
        {
            var value = outlet.PropertyType == typeof(ExperimentDesignerGraphView)
                ? Create<ExperimentDesignerGraphView>()
                : (NSObject)Activator.CreateInstance(outlet.PropertyType);
            outlet.SetValue(controller, value);
            if (value is NSView view) controller.View.AddSubview(view);
        }
        Outlet<NSMenu>(controller, "InstrumentMenu", Outlet<NSPopUpButton>(controller, "InstrumentControl").Menu);
        Outlet<NSMenu>(controller, "ModelMenu", Outlet<NSPopUpButton>(controller, "ModelControl").Menu);

        Outlet<NSTextField>(controller, "CellConcField").StringValue = "10";
        Outlet<NSTextField>(controller, "SyringeConcField").StringValue = "100";
        Outlet<NSTextField>(controller, "InjectionCountField").IntValue = 20;
        Outlet<NSButton>(controller, "AutoVolume").State = NSCellStateValue.On;
        Outlet<NSButton>(controller, "SmallInitialInjCheckmark").State = NSCellStateValue.On;
        var injectionVolumeStepper = Outlet<NSStepper>(controller, "InjectionVolumeStepper");
        injectionVolumeStepper.MinValue = 0.1;
        injectionVolumeStepper.MaxValue = 20;
        var noiseLevel = Outlet<NSSlider>(controller, "NoiseLevelSlider");
        noiseLevel.MinValue = 0.1;
        noiseLevel.MaxValue = 5;
        noiseLevel.DoubleValue = 1;

        controller.ViewDidLoad();
        Check(Factory(controller) != null, "designer created its generation model");
        return controller;
    }

    static T Create<T>() where T : NSObject
    {
        var handle = Send(Send(new Class(typeof(T)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        return Runtime.GetNSObject<T>(handle);
    }

    // Returns the internal value the control reads from the text, in the display unit.
    static double Enter(ExperimentDesignerViewController2 controller, ParameterType key, string text)
    {
        var control = Control(controller, key);
        var input = InputField(control);
        input.StringValue = text;
        Invoke(control, "Input_Changed", input, EventArgs.Empty);
        Check(control.TryGetInputValue(out var value), "entered text is a number");
        return value;
    }

    static void SetInjectionCount(ExperimentDesignerViewController2 controller, int count)
    {
        var field = Outlet<NSTextField>(controller, "InjectionCountField");
        field.IntValue = count;
        Invoke(controller, "InjectionCountField_Changed", field, EventArgs.Empty);
    }

    static void SelectModel(ExperimentDesignerViewController2 controller, AnalysisModel model)
    {
        var popup = Outlet<NSPopUpButton>(controller, "ModelControl");
        popup.SelectItemWithTag((int)model);
        Invoke(controller, "ModelControlAction", popup);
        Check(Factory(controller).ModelType == model, "model switched to " + model);
    }

    static SingleModelFactory Factory(ExperimentDesignerViewController2 controller) =>
        (SingleModelFactory)typeof(ExperimentDesignerViewController2).GetProperty("Factory", Instance).GetValue(controller);

    static List<ParameterValueAdjustmentView> ParameterControls(ExperimentDesignerViewController2 controller) =>
        (List<ParameterValueAdjustmentView>)typeof(ExperimentDesignerViewController2).GetField("ParameterControls", Instance).GetValue(controller);

    static ParameterValueAdjustmentView Control(ExperimentDesignerViewController2 controller, ParameterType key) =>
        ParameterControls(controller).Single(control => control.Key == key);

    static NSTextField InputField(ParameterValueAdjustmentView control) =>
        (NSTextField)typeof(ParameterValueAdjustmentView).GetField("Input", Instance).GetValue(control);

    static double Value(SingleModelFactory factory, ParameterType key) => factory.Model.Parameters.Table[key].Value;

    static T Outlet<T>(ExperimentDesignerViewController2 controller, string name, T value = null) where T : NSObject
    {
        var property = typeof(ExperimentDesignerViewController2).GetProperty(name, Instance);
        if (value != null) property.SetValue(controller, value);
        return (T)property.GetValue(controller);
    }

    static void Invoke(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, Instance).Invoke(target, arguments);

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Failed: " + message);
    }
}
