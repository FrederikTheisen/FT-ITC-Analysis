using System;
using System.Collections.Generic;
using System.Globalization;
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
using AnalysisITC.UI.MacOS.CustomViews;

// Drives the real designer controller with code-created outlets; no storyboard or app bundle.
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
            SetupAndModelChangesKeepEnteredValues(autoRun: true);
            SetupAndModelChangesKeepEnteredValues(autoRun: false);
            FitLocksControlsAndRestoresTheirStates();
            Console.WriteLine("PASS: native designer keeps entered values, rebinds controls and restores control states after fitting");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
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
