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
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

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
            ModelSwitchResetsCachedConstraints(assembly);
            ModelSwitchUsesPhysicalStoichiometry();
            CompetitiveAffinityInitializationUsesActiveControls();

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
        var modelMenu = Popup("Offset");
        modelMenu.ItemAtIndex(0).Tag = (int)AnalysisModel.Offset;
        SetOutlet(controllerType, controller, "ModelTypeControl", modelMenu);

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
            Check(!mode.IsEnabled(0) && !mode.IsEnabled(1) && !modelMenu.ItemAtIndex(0).Enabled,
                "native Run Fit kept its own model or fitting-mode editor active");
            Check(candidate.Parameters.Table[ParameterType.Offset].Value <= 50000,
                "native Run Fit prepared the clamped candidate: " + candidate.Parameters.Table[ParameterType.Offset].Value);
            Check(completion.Task.Wait(TimeSpan.FromSeconds(30)), "native Run Fit completed");
            Check(completion.Task.Result.Success, "native Run Fit completed successfully: " + completion.Task.Result.Message);
            Check(WaitForIdle(),
                "first native fit released processing admission");
            var retained = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver((Solver)solver));
            var prediction = candidate.Evaluate(0);
            var reported = candidate.Solution.Parameters[ParameterType.Offset].Value;
            // This managed harness constructs outlets directly, without ViewDidLoad's
            // completion subscription. Reset its local UI flags before the next click.
            controllerType.GetField("isFitting", PrivateInstance).SetValue(controller, false);
            controllerType.GetField("activeSolver", PrivateInstance).SetValue(controller, null);
            completion = new System.Threading.Tasks.TaskCompletionSource<SolverConvergence>();
            controllerType.GetMethod("RunFit", PrivateInstance).Invoke(controller, null);
            var second = (Solver)controllerType.GetField("activeSolver", PrivateInstance).GetValue(controller);
            Check(!ReferenceEquals(candidate, second.Model), "repeat native fit owns a fresh model");
            Check(completion.Task.Wait(TimeSpan.FromSeconds(30)), "repeat native Run Fit completed");
            Check(WaitForIdle(),
                "repeat native fit released processing admission");
            second.Model.LossFunction(new[] { 3000.0 }, false);
            Check(retained.Model.Models[0].Evaluate(0) == prediction, "repeat native fit preserved retained predictions");
            Check(candidate.Parameters.Table[ParameterType.Offset].Value == reported,
                "repeat native fit preserved retained parameters");
            Console.WriteLine("PASS: consecutive native Run Fit calls preserve retained result models and predictions");
        }
        finally { SolverInterface.AnalysisFinished -= handler; }
        Console.WriteLine("PASS: native DataAnalysisViewController.RunFit launches with a clamped accepted guess");
    }

    static void ModelSwitchResetsCachedConstraints(Assembly assembly)
    {
        DataManager.Init();
        var first = CreateConstraintExperiment("native-switch-first.itc", 20);
        DataManager.AddData(new[] { first, CreateConstraintExperiment("native-switch-second.itc", 30) });
        var type = assembly.GetType("AnalysisITC.DataAnalysisViewController", true);
        var controller = Create<DataAnalysisViewController>();
        var workspace = controller.Workspace;
        var mode = Create<NSSegmentedControl>();
        mode.SegmentCount = 2;
        mode.SelectedSegment = 1;
        SetOutlet(type, controller, "AnalysisModeControl", mode);
        SetOutlet(type, controller, "ParameterStackView", Create<NSStackView>());
        SetOutlet(type, controller, "ConstraintStackView", Create<NSStackView>());
        SetOutlet(type, controller, "ModelOptionsStackView", Create<NSStackView>());
        SetOutlet(type, controller, "FitSimplexButton", Create<NSButton>());
        SetOutlet(type, controller, "ErrorMethodControl", Popup("None"));
        var menu = Popup("One Site", "Sequential");
        menu.ItemAtIndex(0).Tag = (int)AnalysisModel.OneSetOfSites;
        menu.ItemAtIndex(1).Tag = (int)AnalysisModel.SequentialBindingSites;
        SetOutlet(type, controller, "ModelTypeControl", menu);
        try
        {
            workspace.SetGlobalMode(true);
            workspace.SetConstraint(ParameterType.Affinity1, VariableConstraint.SameForAll);
            type.GetMethod("RebuildInspectorEditorViews", PrivateInstance).Invoke(controller, null);
            var draft = type.GetField("inspectorDraft", PrivateInstance).GetValue(controller);
            var draftType = draft.GetType();
            var constraints = (IDictionary)draftType.GetProperty("Constraints").GetValue(draft);
            var parameters = (IDictionary)draftType.GetProperty("Parameters").GetValue(draft);
            var options = (IDictionary)draftType.GetProperty("Options").GetValue(draft);
            var parameter = parameters[ParameterType.Affinity1];
            var parameterCount = parameters.Count;
            var optionCount = options.Count;
            Check((VariableConstraint)constraints[ParameterType.Affinity1] == VariableConstraint.SameForAll,
                "native precondition: the cached One Site draft contains shared affinity");

            menu.SelectItem(1);
            type.GetMethod("AnalysisModelClicked", PrivateInstance).Invoke(controller, new object[] { menu });
            Check(workspace.IsReady && workspace.Session.Global.Constraints.Count == 0,
                "native switch to Sequential leaves a ready independent context");
            Check(constraints.Count == 0, "native model switch clears cached sharing");
            Check(parameters.Count == parameterCount && ReferenceEquals(parameter, parameters[ParameterType.Affinity1])
                && options.Count == optionCount, "native model switch preserves cached parameter and option drafts");
            Check(((IList)type.GetField("constraintControls", PrivateInstance).GetValue(controller)).Count > 0,
                "native Sequential constraint controls remain available");

            menu.SelectItem(0);
            type.GetMethod("AnalysisModelClicked", PrivateInstance).Invoke(controller, new object[] { menu });
            Check(workspace.IsReady, "native return to One Site remains ready");
            var restored = type.GetField("inspectorDraft", PrivateInstance).GetValue(controller);
            Check(ReferenceEquals(draft, restored), "native revisit reuses the parameter draft cache");
            constraints = (IDictionary)draftType.GetProperty("Constraints").GetValue(restored);
            foreach (VariableConstraint constraint in constraints.Values)
                Check(constraint == VariableConstraint.None, "native revisit must not restore old sharing");

            // A pending, unapplied sharing edit is retained on a no-op selection,
            // but discarded when the model actually changes.
            constraints[ParameterType.Affinity1] = VariableConstraint.SameForAll;
            type.GetMethod("AnalysisModelClicked", PrivateInstance).Invoke(controller, new object[] { menu });
            Check((VariableConstraint)constraints[ParameterType.Affinity1] == VariableConstraint.SameForAll,
                "native same-model selection preserves the sharing draft");
            menu.SelectItem(1);
            type.GetMethod("AnalysisModelClicked", PrivateInstance).Invoke(controller, new object[] { menu });
            Check(workspace.IsReady && constraints.Count == 0,
                "native subsequent model change discards pending sharing and remains ready");
            Console.WriteLine("PASS: native model switches reset sharing without losing parameter or option drafts");
        }
        finally
        {
            type.GetMethod("ClearInspectorRows", PrivateInstance).Invoke(controller, null);
            controller.Dispose();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    sealed class SwitchDetailsController : ExperimentDetailsPopoverController
    {
        public bool Applied;
        public override void DismissViewController(NSViewController controller) { Applied = true; }
    }

    static void ModelSwitchUsesPhysicalStoichiometry()
    {
        var culture = CultureInfo.CurrentCulture;
        var failures = new List<string>();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            foreach (var target in new[] { AnalysisModel.OneSetOfSites, AnalysisModel.CompetitiveBinding })
                foreach (var global in new[] { false, true })
                {
                    try { ModelSwitchUsesPhysicalStoichiometry(target, global); }
                    catch (Exception error) { failures.Add(target + ", global=" + global + ": " + error.Message); }
                    finally { DataManager.Clear(DataClearMode.ResetSession); }
                }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Check(failures.Count == 0, string.Join("\n", failures));
        Console.WriteLine("PASS: four native model-switch workflows display dimensionless N after Dissociation Details edits");
    }

    static void CompetitiveAffinityInitializationUsesActiveControls()
    {
        var priorUnit = AppSettings.DefaultConcentrationUnit;
        var priorKdInput = AppSettings.InputAffinityAsDissociationConstant;
        var culture = CultureInfo.CurrentCulture;
        AppSettings.DefaultConcentrationUnit = ConcentrationUnit.nM;
        AppSettings.InputAffinityAsDissociationConstant = true;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var failures = new List<string>();
        try
        {
            foreach (var global in new[] { false, true })
            {
                try { CompetitiveAffinityInitializationUsesActiveControls(global, failures); }
                catch (Exception error) { failures.Add("global=" + global + ": " + error); }
                finally { DataManager.Clear(DataClearMode.ResetSession); }
            }
        }
        finally
        {
            AppSettings.DefaultConcentrationUnit = priorUnit;
            AppSettings.InputAffinityAsDissociationConstant = priorKdInput;
            CultureInfo.CurrentCulture = culture;
        }
        Check(failures.Count == 0, string.Join("\n", failures));
    }

    static void CompetitiveAffinityInitializationUsesActiveControls(bool global, List<string> failures)
    {
        DataManager.Init();
        var experiments = global
            ? new[] { CompetitiveExperiment("native-competitive-a.itc"), CompetitiveExperiment("native-competitive-b.itc") }
            : new[] { CompetitiveExperiment("native-competitive.itc") };
        foreach (var data in experiments)
        {
            var attached = new CompetitiveBinding(data);
            attached.InitializeParameters(data);
            attached.Parameters.Table[ParameterType.Affinity1].Update(7, false);
            attached.Solution = SolutionInterface.FromModel(attached,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            data.Model = attached;
        }
        var attachedSolutions = experiments.Select(data => data.Model.Solution).ToArray();
        DataManager.AddData(experiments);

        using (var controller = Create<DataAnalysisViewController>())
        {
            var type = controller.GetType();
            SetOutlet(type, controller, "AnalysisModeControl",
                new NSSegmentedControl { SegmentCount = 2, SelectedSegment = global ? 1 : 0 });
            SetOutlet(type, controller, "ParameterStackView", new NSStackView());
            SetOutlet(type, controller, "ConstraintStackView", new NSStackView());
            SetOutlet(type, controller, "ModelOptionsStackView", new NSStackView());
            SetOutlet(type, controller, "GraphView", Create<AnalysisGraphView>());
            SetOutlet(type, controller, "FitSimplexButton", new NSButton());
            SetOutlet(type, controller, "ErrorMethodControl", Popup("None"));
            var menu = Popup("One Site", "Competitive Binding");
            menu.ItemAtIndex(0).Tag = (int)AnalysisModel.OneSetOfSites;
            menu.ItemAtIndex(1).Tag = (int)AnalysisModel.CompetitiveBinding;
            menu.Target = controller;
            menu.Action = new Selector("AnalysisModelClicked:");
            SetOutlet(type, controller, "ModelTypeControl", menu);

            if (global)
            {
                var mode = (NSSegmentedControl)GetOutlet(type, controller, "AnalysisModeControl");
                mode.Target = controller;
                mode.Action = new Selector("AnalysisModeClicked:");
                Check(mode.SendAction(mode.Action, mode.Target), "Global analysis-mode action was not dispatched");
            }
            menu.SelectItem(1);
            Check(menu.SendAction(menu.Action, menu.Target), "Competitive model-selector action was not dispatched");
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
            Check(controller.Workspace.IsReady, "Competitive analysis workspace did not rebuild");

            if (global)
            {
                var constraintControls = ((IList)type.GetField("constraintControls", PrivateInstance).GetValue(controller))
                    .Cast<object>().ToArray();
                var constraint = constraintControls.Single(row => (ParameterType)row.GetType()
                        .GetField("key", PrivateInstance).GetValue(row) == ParameterType.Affinity1);
                var options = (IReadOnlyList<VariableConstraint>)constraint.GetType()
                    .GetField("options", PrivateInstance).GetValue(constraint);
                var popup = (NSPopUpButton)constraint.GetType().GetField("popup", PrivateInstance).GetValue(constraint);
                popup.SelectItem(options.ToList().IndexOf(VariableConstraint.SameForAll));
                Check(popup.SendAction(popup.Action, popup.Target), "Shared Kd-constraint action was not dispatched");
                NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
            }

            var rows = ParameterRows(type, controller, ParameterType.Affinity1);
            Check(rows.Length == 1, "Expected one active affinity editor after Single or shared Global setup");
            for (var i = 0; i < rows.Length; i++)
            {
                var input = (NSTextField)rows[i].GetType().GetField("input", PrivateInstance).GetValue(rows[i]);
                var displayed = string.IsNullOrWhiteSpace(input.StringValue)
                    ? input.PlaceholderString.Replace("Auto: ", "")
                    : input.StringValue;
                Console.WriteLine("ITC-131 native initial: global=" + global + ", editor=" + i
                    + ", displayed Kd=" + displayed + ", attached log10Ka=7, expected Kd=100 nM");
                if (displayed != "100")
                {
                    failures.Add("global=" + global + ": automatic competitive affinity showed " + displayed + " nM instead of 100 nM");
                }
            }

            // Explicit value and lock changes must survive the next real workspace rebuild.
            var firstInput = (NSTextField)rows[0].GetType().GetField("input", PrivateInstance).GetValue(rows[0]);
            firstInput.StringValue = "10";
            NSNotificationCenter.DefaultCenter.PostNotificationName("NSControlTextDidChangeNotification", firstInput);
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
            var preview = type.GetMethod("BuildInspectorPreviewContext", PrivateInstance).Invoke(controller, null);
            var previewAffinities = ((IEnumerable)preview.GetType().GetProperty("ExposedParameters").GetValue(preview))
                .Cast<Parameter>().Where(parameter => parameter.Key == ParameterType.Affinity1).ToArray();
            Check(previewAffinities.Length > 0
                && previewAffinities.All(parameter => Math.Abs(parameter.Value - 8) < 1e-12),
                "Native 10 nM edit did not produce canonical preview log10Ka=8");
            controller.Workspace.TryRebuild();
            type.GetMethod("RebuildInspectorEditorViews", PrivateInstance).Invoke(controller, null);
            var freeRetained = ParameterRows(type, controller, ParameterType.Affinity1);
            Check(((NSTextField)freeRetained[0].GetType().GetField("input", PrivateInstance).GetValue(freeRetained[0])).StringValue == "10",
                "Free 10 nM affinity was not retained through rebuild");
            Check(((NSSwitch)freeRetained[0].GetType().GetField("lockSwitch", PrivateInstance).GetValue(freeRetained[0])).State == (int)NSCellStateValue.Off,
                "Free affinity unexpectedly became locked");
            var firstLock = (NSSwitch)freeRetained[0].GetType().GetField("lockSwitch", PrivateInstance).GetValue(freeRetained[0]);
            firstLock.State = (int)NSCellStateValue.On;
            Check(firstLock.SendAction(firstLock.Action, firstLock.Target), "Affinity lock action was not dispatched");
            controller.Workspace.TryRebuild();
            type.GetMethod("RebuildInspectorEditorViews", PrivateInstance).Invoke(controller, null);
            var retained = ParameterRows(type, controller, ParameterType.Affinity1);
            Check(((NSTextField)retained[0].GetType().GetField("input", PrivateInstance).GetValue(retained[0])).StringValue == "10",
                "Explicit 10 nM affinity was not retained through rebuild");
            Check(((NSSwitch)retained[0].GetType().GetField("lockSwitch", PrivateInstance).GetValue(retained[0])).State == (int)NSCellStateValue.On,
                "Locked affinity was not retained through rebuild");
            Console.WriteLine("PASS: native Competitive affinity editor accepts 10 nM as log10Ka=8; free and locked drafts survive rebuild; global=" + global);

            // Restore defaults through the production menu helper; the attached result is independent.
            var mainWindow = Create<MainWindowController>();
            try
            {
                var mainWindowType = mainWindow.GetType();
                var resetHandler = type.GetMethod("OnResetStoredAnalysisStateRequested", PrivateInstance);
                EventHandler forwardReset = (sender, args) => resetHandler.Invoke(controller, new object[] { sender, args });
                DataAnalysisViewController.ResetStoredAnalysisStateRequested += forwardReset;
                try { mainWindowType.GetMethod("RestoreAnalysisDefaults", PrivateInstance).Invoke(mainWindow, null); }
                finally { DataAnalysisViewController.ResetStoredAnalysisStateRequested -= forwardReset; }
            }
            finally { mainWindow.Dispose(); }
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
            if (global)
            {
                var resetConstraintControls = ((IList)type.GetField("constraintControls", PrivateInstance).GetValue(controller))
                    .Cast<object>().ToArray();
                var constraint = resetConstraintControls.Single(row => (ParameterType)row.GetType()
                    .GetField("key", PrivateInstance).GetValue(row) == ParameterType.Affinity1);
                var options = (IReadOnlyList<VariableConstraint>)constraint.GetType()
                    .GetField("options", PrivateInstance).GetValue(constraint);
                var popup = (NSPopUpButton)constraint.GetType().GetField("popup", PrivateInstance).GetValue(constraint);
                var sharedIndex = options.ToList().IndexOf(VariableConstraint.SameForAll);
                Check(sharedIndex >= 0, "Post-reset affinity constraint does not offer Shared Kd");
                popup.SelectItem(sharedIndex);
                Check(popup.SendAction(popup.Action, popup.Target), "Post-reset shared Kd-constraint action was not dispatched");
                NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
            }
            var resetRows = ParameterRows(type, controller, ParameterType.Affinity1);
            Check(resetRows.Length == 1, "Expected one active affinity editor after default sharing setup");
            foreach (var row in resetRows)
            {
                var input = (NSTextField)row.GetType().GetField("input", PrivateInstance).GetValue(row);
                var display = string.IsNullOrWhiteSpace(input.StringValue)
                    ? input.PlaceholderString.Replace("Auto: ", "") : input.StringValue;
                Console.WriteLine("ITC-131 native reset display: global=" + global + ", Kd=" + display + " nM");
                Check(display == "1", "Restore analysis defaults did not show the 1 nM automatic value; got " + display);
            }
            for (var i = 0; i < experiments.Length; i++)
                Check(ReferenceEquals(experiments[i].Model.Solution, attachedSolutions[i])
                    && attachedSolutions[i].Parameters[ParameterType.Affinity1].Value == 7,
                    "Restore analysis defaults changed the attached solution's canonical log10Ka value");
            Console.WriteLine("PASS: native Restore analysis defaults shows 1 nM and retains attached log10Ka=7; global=" + global);
            type.GetMethod("ClearInspectorRows", PrivateInstance).Invoke(controller, null);
        }
    }

    static object[] ParameterRows(Type controllerType, object controller, ParameterType key)
        => ((IList)controllerType.GetField("parameterControls", PrivateInstance).GetValue(controller))
            .Cast<object>().Where(row => (ParameterType)row.GetType().GetProperty("ParameterKey").GetValue(row) == key).ToArray();

    static ExperimentData CompetitiveExperiment(string name)
    {
        var data = new ExperimentData(name)
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        for (var i = 0; i < 5; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, 420e-6 * 2e-6, include: true)
            {
                ActualCellConcentration = new FloatWithError(35e-6 * (1 - .01 * i)),
                ActualTitrantConcentration = new FloatWithError((i + 1) * 5e-6),
                Ratio = (i + 1) * 5e-6 / (35e-6 * (1 - .01 * i)),
                Temperature = 25,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + i * 1e-7));
            data.Injections.Add(injection);
        }
        return data;
    }

    static void ModelSwitchUsesPhysicalStoichiometry(AnalysisModel target, bool global)
    {
        DataManager.Init();
        var experiments = global ? new[] { SwitchExperiment(), SwitchExperiment() } : new[] { SwitchExperiment() };
        DataManager.AddData(experiments);
        using (var controller = Create<DataAnalysisViewController>())
        {
            var type = controller.GetType();
            var mode = new NSSegmentedControl { SegmentCount = 2, SelectedSegment = global ? 1 : 0 };
            SetOutlet(type, controller, "AnalysisModeControl", mode);
            SetOutlet(type, controller, "ParameterStackView", new NSStackView());
            SetOutlet(type, controller, "ConstraintStackView", new NSStackView());
            SetOutlet(type, controller, "ModelOptionsStackView", new NSStackView());
            SetOutlet(type, controller, "FitSimplexButton", new NSButton());
            SetOutlet(type, controller, "ErrorMethodControl", Popup("None"));
            var menu = Popup("Dissociation", target.ToString());
            menu.ItemAtIndex(0).Tag = (int)AnalysisModel.Dissociation;
            menu.ItemAtIndex(1).Tag = (int)target;
            menu.Target = controller;
            menu.Action = new Selector("AnalysisModelClicked:");
            SetOutlet(type, controller, "ModelTypeControl", menu);
            menu.SendAction(menu.Action, menu.Target);
            controller.Workspace.SetGlobalMode(global);
            foreach (var data in experiments)
            {
                ExperimentDetailsPopoverController.Data = data;
                using (var details = new SwitchDetailsController { View = new NSView(new CGRect(0, 0, 720, 600)) })
                {
                    details.ViewDidLoad();
                    var detailsType = typeof(ExperimentDetailsPopoverController);
                    ((NSTextField)detailsType.GetField("CellConcentrationField", PrivateInstance).GetValue(details)).StringValue = "35";
                    ((NSTextField)detailsType.GetField("SyringeConcentrationField", PrivateInstance).GetValue(details)).StringValue = "700";
                    detailsType.GetMethod("Apply", PrivateInstance).Invoke(details, new object[] { null });
                    Check(details.Applied, "Details Apply did not complete");
                }
                Check(data.AxisType == AnalysisXAxisType.TitrantConcentration, "Dissociation axis precondition");
                Check(data.Injections.Last().Ratio == data.Injections.Last().ActualTitrantConcentration,
                    "Details did not recalculate on the concentration axis");
            }
            menu.SelectItem(1);
            menu.SendAction(menu.Action, menu.Target);
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
            if (global)
            {
                var row = ((IList)type.GetField("constraintControls", PrivateInstance).GetValue(controller)).Cast<object>()
                    .Single(view => (ParameterType)view.GetType().GetField("key", PrivateInstance).GetValue(view) == ParameterType.Nvalue1);
                var options = (IReadOnlyList<VariableConstraint>)row.GetType().GetField("options", PrivateInstance).GetValue(row);
                var sharing = (NSPopUpButton)row.GetType().GetField("popup", PrivateInstance).GetValue(row);
                sharing.SelectItem(options.ToList().IndexOf(VariableConstraint.SameForAll));
                sharing.SendAction(sharing.Action, sharing.Target);
                NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
            }
            var models = global ? controller.Workspace.Context.GlobalModel.Models : new[] { controller.Workspace.Context.SingleModel }.AsEnumerable();
            var controls = ((IList)type.GetField("parameterControls", PrivateInstance).GetValue(controller)).Cast<object>()
                .Where(view => (ParameterType)view.GetType().GetProperty("ParameterKey").GetValue(view) == ParameterType.Nvalue1).ToArray();
            Check(controls.Length > 0, "Missing native N editor");
            foreach (var row in controls)
            {
                var input = (NSTextField)row.GetType().GetField("input", PrivateInstance).GetValue(row);
                var text = string.IsNullOrEmpty(input.StringValue) ? input.PlaceholderString.Replace("Auto: ", "") : input.StringValue;
                Console.WriteLine("ITC-135 native " + target + ", global=" + global + ": displayed N=" + text + "; expected .5125");
                Check(Math.Abs(double.Parse(text, CultureInfo.CurrentCulture) - .5125) < .0001, "Displayed N: " + text);
            }
            foreach (var model in models)
                Check(Math.Abs(model.Parameters.Table[ParameterType.Nvalue1].Value - .5125) < 1e-12,
                    "Expected N=.5125, actual=" + model.Parameters.Table[ParameterType.Nvalue1].Value);
            foreach (var data in experiments)
                Check(data.Injections.Last().Ratio == data.Injections.Last().ActualTitrantConcentration,
                    "Model switch rewrote plotting coordinates");
            type.GetMethod("ClearInspectorRows", PrivateInstance).Invoke(controller, null);
        }
    }

    static ExperimentData SwitchExperiment()
    {
        var data = new ExperimentData("native-model-switch.itc")
        {
            CellConcentration = new FloatWithError(0), SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 200e-6, MeasuredTemperature = 25, TargetTemperature = 25,
        };
        for (var i = 0; i < 5; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, 420e-6 * 2e-6, true);
            injection.SetPeakArea(new FloatWithError(-2e-6 + i * 1e-7));
            data.Injections.Add(injection);
        }
        RawDataReader.ProcessInjections(data, DilutionMethod.MicroCal);
        var model = new Dissociation(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
        return data;
    }

    static ExperimentData CreateConstraintExperiment(string name, double temperature)
    {
        var data = new ExperimentData(name)
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = temperature,
            TargetTemperature = temperature,
        };
        for (var index = 0; index < 5; index++)
        {
            var injection = new InjectionData(data, index, 2e-6,
                data.SyringeConcentration * 2e-6, include: true)
            {
                ActualCellConcentration = data.CellConcentration * 0.99,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) * 5e-6 / (data.CellConcentration * 0.99),
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7));
            data.Injections.Add(injection);
        }
        return data;
    }

    static bool WaitForIdle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (ProcessingActivity.IsBusy && DateTime.UtcNow < deadline)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
        return !ProcessingActivity.IsBusy;
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
