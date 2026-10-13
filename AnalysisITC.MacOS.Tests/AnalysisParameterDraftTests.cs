using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

static class AnalysisParameterDraftTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        var previous = AppSettings.ParameterLimitSetting;
        try
        {
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            var assembly = Assembly.LoadFrom(System.IO.Path.Combine(args[0], "AnalysisITC.MacOS/obj/Debug/FT-ITC.exe"));
            var draftType = assembly.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisParameterDraft", true);
            var optionDraftType = assembly.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisOptionDraft", true);
            var viewType = assembly.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisParameterItemView", true);

            ControllerRunFitClampsAcceptedGuess(assembly);

            var pendingText = FormatParameter(assembly, 60000);
            var pending = CreateDraft(draftType, pendingText, false, false);
            var parameter = new Parameter(ParameterType.Offset, 0);
            var view = CreateView(viewType, draftType, optionDraftType, parameter, pending);
            SetText(view, pendingText);
            Invoke(view, "InputChanged");
            Check(!IsValid(view), "free out-of-range typing was accepted");
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Extended;
            parameter.RefreshLimits();
            var rebuilt = CreateView(viewType, draftType, optionDraftType, parameter, pending);
            Check(!IsValid(rebuilt), "a pending invalid edit became valid after a passive rebuild");
            Check((string)Get(draftType, pending, "RawText") == pendingText, "the pending edit text was lost");

            var invalid = CreateDraft(draftType, "", false, false);
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            parameter = new Parameter(ParameterType.Offset, 0);
            view = CreateView(viewType, draftType, optionDraftType, parameter, invalid);
            SetText(view, "60000");
            Invoke(view, "InputChanged");
            var lockSwitch = (NSSwitch)viewType.GetField("lockSwitch", PrivateInstance).GetValue(view);
            lockSwitch.State = (int)NSCellStateValue.On;
            Invoke(view, "LockChanged");
            Check(!IsValid(view), "locking replaced an invalid unaccepted edit");
            Check((string)Get(draftType, invalid, "RawText") == "60000", "locking changed invalid text");
            SetText(view, "0");
            Invoke(view, "InputChanged");
            Check(IsValid(view), "correcting a pending invalid edit did not accept it");
            SetText(view, "");
            Invoke(view, "InputChanged");
            Check(IsValid(view) && !(bool)Get(draftType, invalid, "HasOverride"), "clearing an edit did not restore automatic initialization");

            var accepted = CreateDraft(draftType, FormatParameter(assembly, 60001.234), true, false);
            Set(draftType, accepted, "InternalValue", 60001.234d);
            view = CreateView(viewType, draftType, optionDraftType, parameter, accepted);
            Check(IsValid(view), "a restored accepted value was invalidated by passive validation");
            Check((double)Get(draftType, accepted, "InternalValue") == 60001.234, "the accepted value changed during rebuild");
            var visibleBeforeRebuild = ((NSTextField)viewType.GetField("input", PrivateInstance).GetValue(view)).StringValue;
            var acceptedRebuild = CreateView(viewType, draftType, optionDraftType, parameter, accepted);
            Check(((NSTextField)viewType.GetField("input", PrivateInstance).GetValue(acceptedRebuild)).StringValue == visibleBeforeRebuild,
                "passive rebuild changed the displayed accepted guess");

            var staleLock = new Parameter(ParameterType.Offset, 0, islocked: true);
            var unlockedDraft = CreateDraft(draftType, "", false, false);
            view = CreateView(viewType, draftType, optionDraftType, staleLock, unlockedDraft);
            SetText(view, "60000");
            Invoke(view, "InputChanged");
            Check(!IsValid(view), "an unlocked free draft accepted an out-of-range new value from a formerly locked parameter");

            Console.WriteLine("PASS: native parameter drafts preserve pending invalid edits and accepted restored values");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally
        {
            AppSettings.ParameterLimitSetting = previous;
        }
    }

    static void ControllerRunFitClampsAcceptedGuess(Assembly assembly)
    {
        DataManager.Init();
        var experiment = new ExperimentData("native-runfit.itc")
        {
            CellConcentration = new FloatWithError(1e-3),
            SyringeConcentration = new FloatWithError(10e-3),
            CellVolume = 1e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        for (var i = 0; i < 10; i++)
        {
            var injection = new InjectionData(experiment, i, 1e-6,
                experiment.SyringeConcentration * 1e-6, include: true)
            {
                ActualCellConcentration = experiment.CellConcentration,
                ActualTitrantConcentration = 0.25 * (i + 1) * experiment.CellConcentration,
                Ratio = 0.25 * (i + 1),
                Temperature = 25,
            };
            injection.SetPeakArea(new FloatWithError(2000 * injection.InjectionMass));
            experiment.Injections.Add(injection);
        }
        DataManager.AddData(experiment);
        var controllerType = assembly.GetType("AnalysisITC.DataAnalysisViewController", true);
        var controller = Runtime.GetNSObject(Send(Send(new Class(controllerType).Handle,
            Selector.GetHandle("alloc")), Selector.GetHandle("init")));
        var workspace = controllerType.GetProperty("Workspace").GetValue(controller);
        AppSettings.ParameterLimitSetting = ParameterLimitSetting.Extended;
        workspace.GetType().GetMethod("SetModelType").Invoke(workspace, new object[] { AnalysisModel.Offset });
        workspace.GetType().GetMethod("SetParameterOverride",
            new[] { typeof(ParameterType), typeof(double), typeof(bool), typeof(bool) })
            .Invoke(workspace, new object[] { ParameterType.Offset, 60000d, false, false });
        workspace.GetType().GetMethod("TryRebuild", Type.EmptyTypes).Invoke(workspace, null);
        var context = workspace.GetType().GetProperty("Context").GetValue(workspace);
        var session = workspace.GetType().GetProperty("Session").GetValue(workspace);
        var draftType = assembly.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisInspectorDraft", true);
        var draft = draftType.GetMethod("FromContext", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(null, new[] { context, session });
        controllerType.GetField("inspectorDraft", PrivateInstance).SetValue(controller, draft);

        AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
        workspace.GetType().GetMethod("TryRebuild", Type.EmptyTypes).Invoke(workspace, null);
        context = workspace.GetType().GetProperty("Context").GetValue(workspace);

        SetOutlet(controllerType, controller, "AnalysisModeControl", Create<NSSegmentedControl>());
        var mode = (NSSegmentedControl)GetOutlet(controllerType, controller, "AnalysisModeControl");
        mode.SegmentCount = 2;
        mode.SelectedSegment = 0;
        SetOutlet(controllerType, controller, "ErrorMethodControl", Popup("None"));
        SetOutlet(controllerType, controller, "SolverAlgorithmControl", Popup("Nelder-Mead", "Levenberg-Marquardt"));
        SetOutlet(controllerType, controller, "ErrorIterationsControl", Create<NSSlider>());
        SetOutlet(controllerType, controller, "IncludeConcErrorControl", Create<NSButton>());
        SetOutlet(controllerType, controller, "UseWeightedControl", Create<NSButton>());
        SetOutlet(controllerType, controller, "UnlockParametersForErrorEstimationControl", Create<NSButton>());
        SetOutlet(controllerType, controller, "FitSimplexButton", Create<NSButton>());
        SetOutlet(controllerType, controller, "GraphView", Create<AnalysisGraphView>());

        var parameters = draftType.GetProperty("Parameters").GetValue(draft) as IDictionary;
        var parameterDraft = parameters[ParameterType.Offset];
        var optionDraftType = assembly.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisOptionDraft", true);
        var optionsType = typeof(IDictionary<,>).MakeGenericType(typeof(AttributeKey), optionDraftType);
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(AttributeKey), optionDraftType);
        var options = Activator.CreateInstance(dictionaryType);
        var currentParameter = ((AnalysisContext)context).SingleModel.Parameters.Table[ParameterType.Offset];
        var itemType = assembly.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisParameterItemView", true);
        var itemCtor = itemType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(Parameter), parameterDraft.GetType(), optionsType, typeof(bool) }, null);
        var item = itemCtor.Invoke(new[] { currentParameter, parameterDraft, options, (object)false });
        ((IList)controllerType.GetField("parameterControls", PrivateInstance).GetValue(controller)).Add(item);

        var completion = new System.Threading.Tasks.TaskCompletionSource<SolverConvergence>();
        EventHandler<SolverConvergence> handler = (sender, result) => completion.TrySetResult(result);
        SolverInterface.AnalysisFinished += handler;
        try
        {
            controllerType.GetMethod("RunFit", PrivateInstance).Invoke(controller, null);
            var fitting = (bool)controllerType.GetField("isFitting", PrivateInstance).GetValue(controller);
            var solver = controllerType.GetField("activeSolver", PrivateInstance).GetValue(controller);
            var candidate = (Model)solver.GetType().GetProperty("Model").GetValue(solver);
            Check(fitting, "the actual native Run Fit pipeline entered its fitting state");
            Check(candidate.Parameters.Table[ParameterType.Offset].Value <= 50000,
                "native Run Fit prepared the clamped candidate: " + candidate.Parameters.Table[ParameterType.Offset].Value);
            Check(completion.Task.Wait(TimeSpan.FromSeconds(30)), "native Run Fit completed");
            Check(completion.Task.Result.Success, "native Run Fit completed successfully: " + completion.Task.Result.Message);
        }
        finally { SolverInterface.AnalysisFinished -= handler; }
        Console.WriteLine("PASS: native DataAnalysisViewController.RunFit launches with a clamped accepted guess");
    }

    static T Create<T>() where T : NSObject
    {
        var handle = Send(Send(new Class(typeof(T)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        return Runtime.GetNSObject<T>(handle);
    }

    static NSPopUpButton Popup(params string[] names)
    {
        var popup = Create<NSPopUpButton>();
        popup.AddItems(names);
        popup.SelectItem(0);
        return popup;
    }

    static void SetOutlet(Type type, object instance, string name, NSObject value)
        => type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);

    static object GetOutlet(Type type, object instance, string name)
        => type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(instance);

    static object CreateDraft(Type type, string text, bool hasOverride, bool locked)
    {
        var draft = Activator.CreateInstance(type, true);
        Set(type, draft, "Key", ParameterType.Offset);
        Set(type, draft, "RawText", text);
        Set(type, draft, "InternalValue", 60000d);
        Set(type, draft, "HasOverride", hasOverride);
        Set(type, draft, "Locked", locked);
        Set(type, draft, "IsValid", true);
        return draft;
    }

    static object CreateView(Type viewType, Type draftType, Type optionDraftType, Parameter parameter, object draft)
    {
        var optionType = typeof(IDictionary<,>).MakeGenericType(typeof(AttributeKey), optionDraftType);
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(AttributeKey), optionDraftType);
        var options = Activator.CreateInstance(dictionaryType);
        var constructor = viewType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(Parameter), draftType, optionType, typeof(bool) }, null);
        return constructor.Invoke(new[] { parameter, draft, options, (object)false });
    }

    static void SetText(object view, string text)
        => ((NSTextField)view.GetType().GetField("input", PrivateInstance).GetValue(view)).StringValue = text;

    static bool IsValid(object view)
        => (bool)view.GetType().GetProperty("HasValidInput").GetValue(view);

    static void Invoke(object view, string method)
        => view.GetType().GetMethod(method, PrivateInstance).Invoke(view, new object[] { null, EventArgs.Empty });

    static object Get(Type type, object instance, string property)
        => type.GetProperty(property).GetValue(instance);

    static void Set(Type type, object instance, string property, object value)
        => type.GetProperty(property).SetValue(instance, value);

    static string FormatParameter(Assembly assembly, double value)
    {
        var catalog = assembly.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisInspectorDisplayCatalog", true);
        return (string)catalog.GetMethod("FormatParameter", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(null, new object[] { ParameterType.Offset, value });
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
