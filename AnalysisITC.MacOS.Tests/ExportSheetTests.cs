using System;
using System.Reflection;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

static class ExportSheetTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly Native => typeof(AnalysisITC.AppDelegate).Assembly;

    static int Main(string[] args)
    {
        NSApplication.Init();
        try
        {
            var failures = 0;
            Run("CR-052/053 checked correction drafts", () => CorrectionDraftSurvivesFormatAndUnitChanges(true), ref failures);
            Run("CR-052/053 unchecked correction drafts", () => CorrectionDraftSurvivesFormatAndUnitChanges(false), ref failures);
            Run("CR-052/053 option visibility", OptionVisibilityTracksFormatAndEligibility, ref failures);
            Run("CR-052/053 rejected and accepted confirmations", InvalidAndEmptyConfirmationsRetainDraft, ref failures);
            Run("CR-053 uncorrected Peaks values", OffsetDisabledPeaksUseUncorrectedPeakAndFit, ref failures);
            Run("CR-052/053 cancellation", CancelLeavesExportPreferencesUnchanged, ref failures);
            Run("ITC-126 initial selection scope is preserved", InitialSelectionScopeIsPreserved, ref failures);
            Run("ITC-126 initially empty scopes remain selected and reject confirmation", InitiallyEmptyScopesRemainSelected, ref failures);
            Console.WriteLine(failures == 0 ? "PASS: all export sheet cases" : "FAIL: " + failures + " export sheet cases");
            return failures == 0 ? 0 : 1;
        }
        finally { DataManager.Clear(DataClearMode.ResetSession); }
    }

    static void Run(string name, Action test, ref int failures)
    {
        try { test(); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + name + ": " + error); }
    }

    static void CorrectionDraftSurvivesFormatAndUnitChanges(bool desiredCorrectionState)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(new[]
        {
            Experiment("eligible.itc", eligible: true),
            Experiment("ineligible.itc", eligible: false),
        });
        DataManager.SelectIndex(0);
        var settings = new ExportAccessoryViewSettings
        {
            Export = ExportType.Data,
            Selection = ExportDataSelection.SelectedData,
            ExportBaselineCorrectDataPoints = true,
            ExportOffsetCorrected = true,
            ExportEnergyUnit = EnergyUnit.KiloJoule,
        };
        var host = new Host(settings, _ => { });
        try
        {
            var controller = host.Controller;
            var baseline = Field<NSButton>(controller, "correctedDataCheck");
            var offset = Field<NSButton>(controller, "offsetCorrectedCheck");
            var format = Field<NSPopUpButton>(controller, "formatPopup");
            var selection = Field<NSSegmentedControl>(controller, "selectionControl");
            Check(baseline.State == NSCellStateValue.On && settings.ExportBaselineCorrectDataPoints, "baseline correction did not initialize checked");
            Check(offset.State == NSCellStateValue.On && settings.ExportOffsetCorrected, "offset correction did not initialize checked");

            Click(baseline, controller);
            Check(baseline.State == NSCellStateValue.Off && !settings.ExportBaselineCorrectDataPoints, "baseline click did not update settings");
            Click(baseline, controller);
            Check(baseline.State == NSCellStateValue.On && settings.ExportBaselineCorrectDataPoints, "baseline click did not restore settings");
            if (!desiredCorrectionState)
            {
                Click(baseline, controller);
                Check(baseline.State == NSCellStateValue.Off && !settings.ExportBaselineCorrectDataPoints, "baseline could not be drafted off");
            }

            ChooseFormat(format, ExportType.Peaks, controller);
            Check(baseline.Hidden && !offset.Hidden, "Peaks correction controls are not scoped correctly");
            Click(offset, controller);
            Check(offset.State == NSCellStateValue.Off && !settings.ExportOffsetCorrected, "offset click did not update settings");
            Click(offset, controller);
            Check(offset.State == NSCellStateValue.On && settings.ExportOffsetCorrected, "offset click did not restore settings");
            if (!desiredCorrectionState)
            {
                Click(offset, controller);
                Check(offset.State == NSCellStateValue.Off && !settings.ExportOffsetCorrected, "offset could not be drafted off");
            }

            ChooseFormat(format, ExportType.InterchangeCsv, controller);
            Check((baseline.State == NSCellStateValue.On) == desiredCorrectionState && (offset.State == NSCellStateValue.On) == desiredCorrectionState,
                "format change lost correction checkbox drafts");
            var unit = Field<NSPopUpButton>(controller, "energyUnitPopup");
            unit.SelectItem(3);
            Activate(unit, controller);
            Check(settings.ExportEnergyUnit == EnergyUnit.KCal && (baseline.State == NSCellStateValue.On) == desiredCorrectionState && (offset.State == NSCellStateValue.On) == desiredCorrectionState,
                "unit change lost the selected unit or correction drafts");

            ChooseFormat(format, ExportType.Data, controller);
            selection.SelectedSegment = (int)ExportDataSelection.AllData;
            Activate(selection, controller);
            Check(settings.Selection == ExportDataSelection.AllData, "All-data scope was not applied from the selection control");
            Check(!baseline.Enabled && !offset.Enabled && settings.ExportBaselineCorrectDataPoints == desiredCorrectionState && settings.ExportOffsetCorrected == desiredCorrectionState &&
                  (baseline.State == NSCellStateValue.On) == desiredCorrectionState && (offset.State == NSCellStateValue.On) == desiredCorrectionState,
                "ineligible All-data scope cleared correction drafts (enabled=" + baseline.Enabled + "/" + offset.Enabled +
                ", checked=" + baseline.State + "/" + offset.State + ", settings=" + settings.ExportBaselineCorrectDataPoints + "/" + settings.ExportOffsetCorrected + ")");
            selection.SelectedSegment = (int)ExportDataSelection.SelectedData;
            DataManager.SelectIndex(0);
            Activate(selection, controller);
            Check(settings.Selection == ExportDataSelection.SelectedData, "Selected-data scope was not applied from the selection control");
            Check(baseline.Enabled && offset.Enabled && (baseline.State == NSCellStateValue.On) == desiredCorrectionState && (offset.State == NSCellStateValue.On) == desiredCorrectionState,
                "returning to eligible Selected data did not restore enabled checked corrections");
        }
        finally { host.Dispose(); }
    }

    static void OptionVisibilityTracksFormatAndEligibility()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(Experiment("eligible.itc", eligible: true));
        var settings = new ExportAccessoryViewSettings { Export = ExportType.Data, Selection = ExportDataSelection.AllData };
        var host = new Host(settings, _ => { });
        try
        {
            var controller = host.Controller;
            var baseline = Field<NSButton>(controller, "correctedDataCheck");
            var offset = Field<NSButton>(controller, "offsetCorrectedCheck");
            var noOptions = Field<NSTextField>(controller, "noOptionsLabel");
            var format = Field<NSPopUpButton>(controller, "formatPopup");
            var energyRow = Field<NSView>(controller, "energyUnitRow");
            Check(!baseline.Hidden && baseline.Enabled && offset.Hidden && noOptions.Hidden, "Data option visibility is wrong");
            Check(energyRow.Hidden, "Data unexpectedly shows the molar energy unit row");
            SelectFormat(format, ExportType.Peaks);
            format.SendAction(format.Action, format.Target);
            Check(baseline.Hidden && !offset.Hidden && offset.Enabled && noOptions.Hidden, "Peaks option visibility is wrong");
            Check(!energyRow.Hidden, "Peaks hid the molar energy unit row");
            SelectFormat(format, ExportType.ITCsim);
            format.SendAction(format.Action, format.Target);
            Check(baseline.Hidden && !offset.Hidden && !energyRow.Hidden && noOptions.Hidden, "ITCsim option visibility is wrong");
            SelectFormat(format, ExportType.InterchangeCsv);
            format.SendAction(format.Action, format.Target);
            Check(baseline.Hidden && offset.Hidden && !energyRow.Hidden && !noOptions.Hidden, "Combined Data options or energy unit visibility is wrong");
            var view = ((NSViewController)controller).View;
            view.LayoutSubtreeIfNeeded();
            Check(view.Bounds.Width == 470 && view.Bounds.Height == 490, "Combined Data sheet content is not 470 by 490 points");
            foreach (var fieldName in new[] { "outputNameField", "formatPopup", "energyUnitPopup", "selectionControl", "descriptionLabel", "unitsLabel" })
                Check(InBounds(view, Field<NSView>(controller, fieldName)), "Combined Data control exceeds the sheet bounds: " + fieldName);
            Check(InBounds(view, FindButton(view, "Choose Folder...")), "Choose Folder button exceeds the sheet bounds");
            Check(InBounds(view, FindButton(view, "Cancel")), "Cancel button exceeds the sheet bounds");
            var description = Field<NSTextField>(controller, "descriptionLabel");
            var measuredDescriptionHeight = description.Cell.CellSizeForBounds(new CGRect(0, 0, description.Frame.Width, 1000)).Height;
            Check(description.Frame.Height + 1 >= measuredDescriptionHeight, "Combined Data description is clipped at the fixed sheet size");
            SelectFormat(format, ExportType.MicroCal);
            format.SendAction(format.Action, format.Target);
            Check(baseline.Hidden && offset.Hidden && energyRow.Hidden && !noOptions.Hidden, "format without options did not show the empty-options message");
            SelectFormat(format, ExportType.PYTC);
            format.SendAction(format.Action, format.Target);
            Check(baseline.Hidden && offset.Hidden && energyRow.Hidden && !noOptions.Hidden, "pytc energy unit visibility is wrong");

            ChooseFormat(format, ExportType.Peaks, controller);
            DataManager.Clear(DataClearMode.ResetSession);
            Invoke(controller, "RefreshControls");
            Check(!offset.Hidden && !offset.Enabled, "offset correction stayed enabled with an empty data scope");
            DataManager.AddData(Experiment("returned.itc", eligible: true));
            Invoke(controller, "RefreshControls");
            Check(offset.Enabled, "offset correction did not re-enable when eligible data returned");
        }
        finally { host.Dispose(); }
    }

    static void InvalidAndEmptyConfirmationsRetainDraft()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(Experiment("draft.itc", eligible: true));
        var completionCount = 0;
        var completed = false;
        var settings = new ExportAccessoryViewSettings
        {
            Export = ExportType.Peaks,
            Selection = ExportDataSelection.AllData,
            ExportBaselineCorrectDataPoints = true,
            ExportOffsetCorrected = true,
        };
        var host = new Host(settings, accepted => { completionCount++; completed = accepted; });
        try
        {
            var controller = host.Controller;
            var name = Field<NSTextField>(controller, "outputNameField");
            var error = Field<NSTextField>(controller, "errorLabel");
            var baseline = Field<NSButton>(controller, "correctedDataCheck");
            var offset = Field<NSButton>(controller, "offsetCorrectedCheck");
            Click(offset, controller);
            Check(!settings.ExportOffsetCorrected, "offset draft click did not update settings before rejection");
            name.StringValue = "   ";
            host.ChooseFolder();
            Check(error.StringValue == "Enter an output name." && completionCount == 0, "blank name was accepted");
            Check(!settings.ExportOffsetCorrected, "blank-name rejection discarded the correction draft");
            name.StringValue = "retained-draft";
            var selection = Field<NSSegmentedControl>(controller, "selectionControl");
            selection.SelectedSegment = (int)ExportDataSelection.SelectedData;
            DataManager.Clear(DataClearMode.ResetSession);
            host.ChooseFolder();
            Check(error.StringValue == "Select an experiment or choose Active or All data." && completionCount == 0, "empty selection was accepted");
            Check(name.StringValue == "retained-draft", "output-name draft was discarded after failed confirmations");
            Check(!settings.ExportOffsetCorrected && baseline.State == NSCellStateValue.On,
                "empty-data rejection discarded correction checkbox drafts");
            DataManager.AddData(Experiment("draft-returned.itc", eligible: true));
            selection.SelectedSegment = (int)ExportDataSelection.AllData;
            Activate(selection, controller);
            Check(settings.Selection == ExportDataSelection.AllData, "All-data scope was not applied before the final confirmation");
            Click(offset, controller);
            Check(settings.ExportOffsetCorrected, "native offset-on click did not update the restored draft");
            DataManager.AddData(Experiment("draft-ineligible.itc", eligible: false));
            Activate(selection, controller);
            Check(!offset.Enabled && settings.ExportOffsetCorrected, "mixed-data refresh did not retain the offset draft while disabling it");
            host.ChooseFolder();
            Check(completionCount == 1 && completed, "valid confirmation did not complete after data returned");
            Check(settings.OutputBaseName == "retained-draft", "accepted output-name draft changed");
            Check(!settings.ExportBaselineCorrectDataPoints && !settings.ExportOffsetCorrected,
                "successful confirmation did not normalize corrections for mixed eligibility");
        }
        finally { host.Dispose(); }
    }

    static void CancelLeavesExportPreferencesUnchanged()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(Experiment("cancel.itc", eligible: true));
        var type = AppSettings.DefaultExportType;
        var name = AppSettings.ExportOutputBaseName;
        var selection = AppSettings.ExportSelectionMode;
        var baselinePreference = AppSettings.ExportBaselineCorrectedData;
        var settings = new ExportAccessoryViewSettings { Export = ExportType.InterchangeCsv, OutputBaseName = "cancelled" };
        var host = new Host(settings, accepted => Check(!accepted, "Cancel reported acceptance"));
        try
        {
            var format = Field<NSPopUpButton>(host.Controller, "formatPopup");
            ChooseFormat(format, ExportType.Data, host.Controller);
            Click(Field<NSButton>(host.Controller, "correctedDataCheck"), host.Controller);
            host.Cancel();
            Check(AppSettings.DefaultExportType == type && AppSettings.ExportOutputBaseName == name && AppSettings.ExportSelectionMode == selection,
                "Cancel changed saved export preferences");
            Check(AppSettings.ExportBaselineCorrectedData == baselinePreference,
                "Cancel wrote the baseline correction application preference");
        }
        finally { host.Dispose(); }
    }

    static void InitialSelectionScopeIsPreserved()
    {
        foreach (var scope in new[] { ExportDataSelection.SelectedData, ExportDataSelection.IncludedData, ExportDataSelection.AllData })
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var a = Experiment("scope-a.itc", eligible: true);
            var b = Experiment("scope-b.itc", eligible: true);
            var c = Experiment("scope-c.itc", eligible: true);
            a.Include = false;
            b.Include = true;
            c.Include = true;
            DataManager.AddData(new[] { a, b, c });
            DataManager.SelectIndex(0);
            var expected = scope == ExportDataSelection.SelectedData
                ? new[] { a }
                : scope == ExportDataSelection.IncludedData ? new[] { b, c } : new[] { a, b, c };
            var settings = new ExportAccessoryViewSettings { Export = ExportType.Data, Selection = scope, OutputBaseName = "scope-check" };
            settings.SetData();
            AssertExactData(settings.Data, expected, "pre-host " + scope);

            var completed = false;
            var completionCount = 0;
            var host = new Host(settings, accepted => { completed = accepted; completionCount++; });
            try
            {
                var selection = Field<NSSegmentedControl>(host.Controller, "selectionControl");
                Check(selection.SelectedSegment == (int)scope, "initial segment did not reflect " + scope + ": " + selection.SelectedSegment);
                Check(settings.Selection == scope, "controller changed the requested " + scope + " scope during initialization");
                AssertExactData(settings.Data, expected, "initial " + scope);
                host.ChooseFolder();
                Check(completed && completionCount == 1, "Choose Folder did not accept the initialized " + scope + " scope");
                Check(selection.SelectedSegment == (int)scope, "confirmation changed the selected " + scope + " segment");
                Check(settings.Selection == scope, "confirmation changed the " + scope + " scope");
                AssertExactData(settings.Data, expected, "accepted " + scope);
            }
            finally { host.Dispose(); }
        }
    }

    static void InitiallyEmptyScopesRemainSelected()
    {
        foreach (var scope in new[] { ExportDataSelection.SelectedData, ExportDataSelection.IncludedData, ExportDataSelection.AllData })
        {
            DataManager.Clear(DataClearMode.ResetSession);
            if (scope != ExportDataSelection.AllData)
            {
                var inactive = Experiment("inactive.itc", eligible: true);
                inactive.Include = false;
                DataManager.AddData(inactive);
                if (scope == ExportDataSelection.SelectedData) DataManager.SelectIndex(-1);
            }

            var settings = new ExportAccessoryViewSettings { Export = ExportType.Data, Selection = scope, OutputBaseName = "empty-scope" };
            settings.SetData();
            Check(settings.Data.Count == 0, "empty-scope fixture unexpectedly contains data for " + scope);
            var completed = false;
            var completionCount = 0;
            var host = new Host(settings, accepted => { completed = accepted; completionCount++; });
            try
            {
                var selection = Field<NSSegmentedControl>(host.Controller, "selectionControl");
                Check(selection.SelectedSegment == (int)scope, "initial empty segment did not reflect " + scope + ": " + selection.SelectedSegment);
                Check(settings.Selection == scope && settings.Data.Count == 0,
                    "controller substituted data or changed the initially empty " + scope + " scope");
                host.ChooseFolder();
                Check(!completed && completionCount == 0, "empty " + scope + " scope was accepted");
                Check(Field<NSTextField>(host.Controller, "errorLabel").StringValue == "Select an experiment or choose Active or All data.",
                    "empty " + scope + " scope did not show the existing data-selection validation");
                Check(settings.Selection == scope && settings.Data.Count == 0,
                    "rejected confirmation substituted data or changed the empty " + scope + " scope");
            }
            finally { host.Dispose(); }
        }
    }

    static void AssertExactData(System.Collections.Generic.IReadOnlyList<ExperimentData> actual, ExperimentData[] expected, string context)
    {
        Check(actual.Count == expected.Length, context + " data count " + actual.Count + " != " + expected.Length);
        for (var i = 0; i < expected.Length; i++)
            Check(Object.ReferenceEquals(actual[i], expected[i]), context + " data member/order differs at " + i);
    }

    static void OffsetDisabledPeaksUseUncorrectedPeakAndFit()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var data = Experiment("offset-check.itc", eligible: false);
        data.SyringeConcentration = new FloatWithError(0.001);
        var injection = new InjectionData(data, 0, 0.001, 1e-6, include: true) { Ratio = 1.25 };
        injection.SetPeakArea(new FloatWithError(-0.012, 0.000001));
        data.Injections.Add(injection);
        var model = new Offset(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 250);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
        DataManager.AddData(data);

        var settings = new ExportAccessoryViewSettings
        {
            Export = ExportType.Peaks,
            Selection = ExportDataSelection.AllData,
            ExportBaselineCorrectDataPoints = true,
            ExportOffsetCorrected = true,
            ExportEnergyUnit = EnergyUnit.Joule,
            OutputBaseName = "offset-check",
        };
        var completed = false;
        var host = new Host(settings, accepted => completed = accepted);
        try
        {
            var controller = host.Controller;
            var offset = Field<NSButton>(controller, "offsetCorrectedCheck");
            Click(offset, controller);
            Check(offset.State == NSCellStateValue.Off, "native offset-off click did not update the checkbox");
            var unit = Field<NSPopUpButton>(controller, "energyUnitPopup");
            unit.SelectItem(1);
            Activate(unit, controller);
            host.ChooseFolder();
            Check(completed, "Peaks confirmation did not complete: " + Field<NSTextField>(controller, "errorLabel").StringValue);
            var build = typeof(Exporter).GetMethod("BuildIntegratedPeakLines", BindingFlags.Static | BindingFlags.NonPublic);
            var lines = (System.Collections.Generic.List<string>)build.Invoke(null, new object[] { data, settings });
            var values = lines[1].Split(',');
            Near(double.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture), -12, "uncorrected integrated peak");
            Near(double.Parse(values[3], System.Globalization.CultureInfo.InvariantCulture), 0.25, "uncorrected fitted enthalpy");
            Check(!settings.ExportOffsetCorrected, "Peaks confirmation did not retain the offset-off choice");
        }
        finally { host.Dispose(); }
    }

    sealed class Host : IDisposable
    {
        public object Controller { get; }
        readonly NSWindow window;

        public Host(ExportAccessoryViewSettings settings, Action<bool> completed)
        {
            var controllerType = Native.GetType("AnalysisITC.ExportSheetViewController", true);
            Controller = Activator.CreateInstance(controllerType, Instance, null, new object[] { settings, completed }, null);
            ((NSViewController)Controller).LoadView();
            window = new NSWindow(new CGRect(0, 0, 470, 490), NSWindowStyle.Titled, NSBackingStore.Buffered, false);
            window.ReleasedWhenClosed = false;
            window.ContentView = ((NSViewController)Controller).View;
            Invoke(Controller, "ViewWillAppear");
            window.ContentView.LayoutSubtreeIfNeeded();
            Check(window.Frame.Width >= 470 && window.Frame.Height >= 490, "sheet did not retain its fixed dimensions");
        }

        public void ChooseFolder() => FindButton(window.ContentView, "Choose Folder...").PerformClick((NSObject)Controller);
        public void Cancel() => FindButton(window.ContentView, "Cancel").PerformClick((NSObject)Controller);

        public void Dispose()
        {
            window.Close();
            window.Dispose();
            ((NSViewController)Controller).Dispose();
        }
    }

    static NSButton FindButton(NSView view, string title)
    {
        var found = TryFindButton(view, title);
        if (found != null) return found;
        throw new Exception("Button missing: " + title);
    }

    static NSButton TryFindButton(NSView view, string title)
    {
        if (view is NSButton button && button.Title == title) return button;
        foreach (var child in view.Subviews)
        {
            var found = TryFindButton(child, title);
            if (found != null) return found;
        }
        return null;
    }

    static void ChooseFormat(NSPopUpButton popup, ExportType type, object sender)
    {
        SelectFormat(popup, type);
        Activate(popup, sender);
    }

    static void Activate(NSControl control, object sender) => control.SendAction(control.Action, control.Target);
    static void Click(NSButton button, object sender) => button.PerformClick((NSObject)sender);

    static void SelectFormat(NSPopUpButton popup, ExportType type)
    {
        foreach (var item in popup.Items())
            if ((int)item.Tag == (int)type) { popup.SelectItem(item); return; }
        throw new Exception("Format menu item missing: " + type);
    }

    static bool InBounds(NSView root, NSView child)
    {
        var frame = child.ConvertRectToView(child.Bounds, root);
        return frame.X >= -1 && frame.Y >= -1 && frame.X + frame.Width <= root.Bounds.Width + 1 && frame.Y + frame.Height <= root.Bounds.Height + 1;
    }

    static ExperimentData Experiment(string name, bool eligible)
    {
        var data = new ExperimentData(name)
        {
            DataPoints = new System.Collections.Generic.List<DataPoint> { new DataPoint(0f, 1f) },
            BaseLineCorrectedDataPoints = eligible ? new System.Collections.Generic.List<DataPoint> { new DataPoint(0f, 0.5f) } : null,
        };
        if (eligible)
        {
            var model = new Offset(data);
            model.InitializeParameters(data);
            data.Model = model;
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        }
        return data;
    }

    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Instance).GetValue(target);
    static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Near(double actual, double expected, string message)
    {
        if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(actual - expected) > Math.Max(1e-9, Math.Abs(expected) * 1e-12))
            throw new Exception(message + ": " + actual + " != " + expected);
    }
}
