using System;
using System.Collections;
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
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.CustomViews;

static class SyringeCorrectionPresentationTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Native = typeof(ParameterValueAdjustmentView).Assembly;
    const string SyringeTooltip = "Scales delivered titrant concentration during model evaluation.";
    const string CellTooltip = "Apparent binding sites per macromolecule in the cell.";

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main()
    {
        NSApplication.Init();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var autoRun = ExperimentDesignerViewController2.AutoRunExperimentSimulation;
        try
        {
            ExperimentDesignerViewController2.AutoRunExperimentSimulation = false;
            foreach (var model in new[] { AnalysisModel.OneSetOfSites, AnalysisModel.TwoSetsOfSites, AnalysisModel.CompetitiveBinding })
            {
                Analysis(model);
                Designer(model);
                LockedResult(model);
                LockedResult(model, global: true);
            }
            Console.WriteLine("PASS: native syringe labels and tooltips follow options in Analysis, designer and locked results for all three models");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { ExperimentDesignerViewController2.AutoRunExperimentSimulation = autoRun; }
    }

    static void Analysis(AnalysisModel model)
    {
        var draftType = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisParameterDraft", true);
        var optionType = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisOptionDraft", true);
        var viewType = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisParameterItemView", true);
        var draft = Activator.CreateInstance(draftType, true);
        Set(draft, "Key", ParameterType.Nvalue1);
        Set(draft, "InternalValue", .8d);
        Set(draft, "RawText", "0.8");
        Set(draft, "HasOverride", true);
        Set(draft, "Locked", true);
        Set(draft, "IsValid", true);
        var parameter = new Parameter(ParameterType.Nvalue1, .8, islocked: true);
        var options = ModelOptionCatalog.CreateOptions(model);
        foreach (var correction in new[] { false, true, false, true })
        {
            options[AttributeKey.UseSyringeActiveFraction].BoolValue = correction;
            var drafts = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(AttributeKey), optionType));
            foreach (var option in options)
            {
                var optionDraft = Activator.CreateInstance(optionType, true);
                Set(optionDraft, "Option", option.Value);
                drafts.Add(option.Key, optionDraft);
            }
            var ownerType = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisInspectorDraft", true);
            var owner = Activator.CreateInstance(ownerType, true);
            var ownerOptions = (IDictionary)Get(owner, "Options");
            foreach (DictionaryEntry option in drafts) ownerOptions.Add(option.Key, option.Value);
            var constraintType = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisConstraintItemView", true);
            var descriptor = (GlobalConstraintFamilyDescriptor)Activator.CreateInstance(
                typeof(GlobalConstraintFamilyDescriptor), Instance, null,
                new object[] { ParameterType.Nvalue1, new[] { ParameterType.Nvalue1 },
                    new[] { VariableConstraint.None, VariableConstraint.SameForAll } }, CultureInfo.InvariantCulture);
            using (var constraint = (NSView)Activator.CreateInstance(constraintType, Instance, null,
                new object[] { descriptor, owner, false, false }, CultureInfo.InvariantCulture))
                Check(Fields(constraint).Any(field => field.StringValue == Title(correction)
                    && field.ToolTip == Tooltip(correction)), "global constraint label and tooltip: " + model);
            using (var view = (NSView)Activator.CreateInstance(viewType, Instance, null,
                new object[] { parameter, draft, drafts, model == AnalysisModel.TwoSetsOfSites }, CultureInfo.InvariantCulture))
            {
                AssertLabel(view, correction);
                var input = Field<NSTextField>(view, "input");
                Check(input.ToolTip == Tooltip(correction), "analysis input tooltip: " + model);
                Check(input.StringValue == "0.8" && (double)Get(draft, "InternalValue") == .8
                    && (bool)Get(draft, "Locked"), "analysis override changed: " + model);
            }
        }
    }

    static void Designer(AnalysisModel model)
    {
        using (var controller = DesignerController())
        {
            var popup = Outlet<NSPopUpButton>(controller, "ModelControl");
            popup.SelectItemWithTag((int)model);
            Invoke(controller, "ModelControlAction", popup);
            var parameter = ParameterControl(controller);
            var input = Field<NSTextField>(parameter, "Input");
            input.StringValue = "0.8";
            Invoke(parameter, "Input_Changed", input, EventArgs.Empty);
            foreach (var correction in new[] { true, false, true })
            {
                var option = Field<List<OptionAdjustmentView>>(controller, "OptionControls")
                    .Single(control => control.Key == AttributeKey.UseSyringeActiveFraction);
                Field<NSButton>(option, "InputSwitch").State = correction ? NSCellStateValue.On : NSCellStateValue.Off;
                Invoke(controller, "OptionControl_ValueChanged", option, EventArgs.Empty);
                parameter = ParameterControl(controller);
                AssertLabel(parameter, correction);
                Check(Field<NSTextField>(parameter, "Input").ToolTip == Tooltip(correction), "designer input tooltip: " + model);
                Check(Field<NSSlider>(parameter, "Slider").ToolTip == Tooltip(correction), "designer slider tooltip: " + model);
                Check(parameter.Value == .8, "designer entered value changed: " + model);
            }
        }
    }

    static void LockedResult(AnalysisModel type, bool global = false)
    {
        using (var controller = Create<AnalysisResultTabViewController>())
        using (var stack = new NSStackView())
        {
            controller.GetType().GetField("modelStack", Instance).SetValue(controller, stack);
            foreach (var correction in new[] { true, false, true })
            {
                var data = Data();
                Model model;
                switch (type)
                {
                    case AnalysisModel.TwoSetsOfSites: model = new TwoSetsOfSites(data); break;
                    case AnalysisModel.CompetitiveBinding: model = new CompetitiveBinding(data); break;
                    default: model = new OneSetOfSites(data); break;
                }
                model.InitializeParameters(data);
                model.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = correction;
                model.ApplyModelOptions();
                model.Parameters.Table[ParameterType.Nvalue1].Update(.8, true);
                model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
                model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(
                    new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }));
                var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
                if (global)
                {
                    var second = new SingleModelFactory(type, useStoredOptions: false);
                    second.InitializeModel(Data());
                    second.SetModelOption(model.ModelOptions[AttributeKey.UseSyringeActiveFraction].Copy());
                    second.Model.Parameters.Table[ParameterType.Nvalue1].Update(.8, true);
                    second.Model.Solution = SolutionInterface.FromModel(second.Model, SolverConvergence.FromSnapshot(
                        new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }));
                    var models = new List<Model> { model, second.Model };
                    var combined = new GlobalModel(models)
                    {
                        Parameters = new GlobalModelParameters(),
                        ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
                    };
                    combined.Parameters.SetConstraintForParameter(ParameterType.Nvalue1, VariableConstraint.SameForAll);
                    combined.Parameters.AddorUpdateGlobalParameter(ParameterType.Nvalue1, .8, true);
                    foreach (var member in models)
                    {
                        member.Parameters.Table[ParameterType.Nvalue1].SetGlobal(.8);
                        combined.Parameters.AddIndivdualParameter(member.Parameters);
                    }
                    var solution = new GlobalSolution(new GlobalSolver { Model = combined },
                        models.Select(member => member.Solution).ToList(), SolverConvergence.FromSnapshot(
                            new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }));
                    combined.Solution = solution;
                    result = new AnalysisResult(solution);
                }
                controller.GetType().GetField("analysisResult", Instance).SetValue(controller, result);
                Invoke(controller, "RefreshModel");
                var section = stack.ArrangedSubviews.Single(view => Fields(view).First().StringValue == "Locked parameters");
                var label = Fields(section).Single(field => field.StringValue == Title(correction));
                Check(label.ToolTip == Tooltip(correction), "locked saved parameter tooltip: " + type);
                Check(Fields(section).Any(field => field.StringValue == "0.8"), "locked value changed: " + type);
                Check(!Fields(section).Any(field => field.StringValue == Title(!correction)), "locked parameter retained stale label: " + type);
                if (global)
                {
                    var constraints = stack.ArrangedSubviews.Single(view => Fields(view).First().StringValue == "Constraints");
                    var constraintTitle = Fields(constraints).Single(field => field.StringValue == Title(correction));
                    Check(constraintTitle.ToolTip == Tooltip(correction), "saved global constraint tooltip: " + type);
                }
            }
        }
    }

    static void AssertLabel(NSView view, bool correction)
    {
        var label = Fields(view).Single(field => field.StringValue.StartsWith(Title(correction), StringComparison.Ordinal));
        Check(label.ToolTip == Tooltip(correction), "parameter label tooltip");
        Check(Fields(view).Any(field => field.StringValue.Contains(correction ? "α" : "N")), "parameter symbol");
        Check(!Fields(view).Any(field => field.StringValue.StartsWith(Title(!correction), StringComparison.Ordinal)), "stale parameter label");
    }

    static string Title(bool correction) { return correction ? "Correction factor" : "N-value"; }
    static string Tooltip(bool correction) { return correction ? SyringeTooltip : CellTooltip; }
    static IEnumerable<NSTextField> Fields(NSView view)
    {
        var field = view as NSTextField;
        if (field != null) yield return field;
        foreach (var child in view.Subviews)
            foreach (var nested in Fields(child)) yield return nested;
    }
    static object Get(object target, string name) { return target.GetType().GetProperty(name, Instance).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetProperty(name, Instance).SetValue(target, value); }
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Instance).GetValue(target); }
    static void Invoke(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name, Instance);
        if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
        method.Invoke(target, args);
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static T Create<T>() where T : NSObject
    {
        return Runtime.GetNSObject<T>(Send(Send(new Class(typeof(T)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init")));
    }
    static ParameterValueAdjustmentView ParameterControl(ExperimentDesignerViewController2 controller)
    {
        return Field<List<ParameterValueAdjustmentView>>(controller, "ParameterControls").Single(control => control.Key == ParameterType.Nvalue1);
    }
    static T Outlet<T>(object target, string name) { return (T)Get(target, name); }
    static ExperimentDesignerViewController2 DesignerController()
    {
        var controller = Create<ExperimentDesignerViewController2>();
        controller.View = new NSView(new CGRect(0, 0, 800, 600));
        foreach (var outlet in typeof(ExperimentDesignerViewController2).GetProperties(Instance)
            .Where(property => property.GetCustomAttribute<OutletAttribute>() != null && property.PropertyType != typeof(NSMenu)))
        {
            var value = outlet.PropertyType == typeof(ExperimentDesignerGraphView)
                ? Create<ExperimentDesignerGraphView>() : (NSObject)Activator.CreateInstance(outlet.PropertyType);
            outlet.SetValue(controller, value);
            var view = value as NSView;
            if (view != null) controller.View.AddSubview(view);
        }
        Set(controller, "InstrumentMenu", Outlet<NSPopUpButton>(controller, "InstrumentControl").Menu);
        Set(controller, "ModelMenu", Outlet<NSPopUpButton>(controller, "ModelControl").Menu);
        Outlet<NSTextField>(controller, "CellConcField").StringValue = "10";
        Outlet<NSTextField>(controller, "SyringeConcField").StringValue = "100";
        Outlet<NSTextField>(controller, "InjectionCountField").IntValue = 20;
        Outlet<NSButton>(controller, "AutoVolume").State = NSCellStateValue.On;
        Outlet<NSButton>(controller, "SmallInitialInjCheckmark").State = NSCellStateValue.On;
        Outlet<NSStepper>(controller, "InjectionVolumeStepper").MinValue = .1;
        Outlet<NSStepper>(controller, "InjectionVolumeStepper").MaxValue = 20;
        Outlet<NSSlider>(controller, "NoiseLevelSlider").MinValue = .1;
        Outlet<NSSlider>(controller, "NoiseLevelSlider").MaxValue = 5;
        Outlet<NSSlider>(controller, "NoiseLevelSlider").DoubleValue = 1;
        controller.ViewDidLoad();
        return controller;
    }
    static ExperimentData Data()
    {
        var data = new ExperimentData("native-syringe-label.itc")
        {
            CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25,
        };
        for (var index = 0; index < 5; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, data.SyringeConcentration * 2e-6, true)
            {
                ActualCellConcentration = data.CellConcentration * .99,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) * 5e-6 / (data.CellConcentration * .99),
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7));
            data.Injections.Add(injection);
        }
        return data;
    }
}
