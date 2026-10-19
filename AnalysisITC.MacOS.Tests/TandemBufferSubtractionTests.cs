// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh TandemBufferSubtractionTests
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.UI.MacOS.CustomViews;

static class TandemBufferSubtractionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main()
    {
        NSApplication.Init();
        try
        {
            for (var mode = 0; mode < 3; mode++) MergerAvailability(mode);
            SubtractionTargets();
            DetailsAvailability();
            Console.WriteLine("PASS: native tandem restrictions cover all merger modes, subtraction targets, and Details controls/removal");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally { DataManager.Clear(DataClearMode.ResetSession); }
    }

    static void MergerAvailability(int mode)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var first = Experiment("first");
        var second = Experiment("second");
        Correct(second);
        DataManager.AddData(new[] { first, second });
        using (var controller = MakeController<ExperimentMergerViewController>())
        {
            controller.ViewDidLoad();
            var table = Property<NSTableView>(controller, "MergeTableView");
            table.SelectRows(NSIndexSet.FromNSRange(new NSRange(0, 2)), false);
            Property<NSSegmentedControl>(controller, "MergeMethodControl").SelectedSegment = mode;
            Invoke(controller, "SetupMethodControls");
            var button = Property<NSButton>(controller, "MergeButtonControl");
            Check(!button.Enabled, "corrected source admitted in mode " + mode);
            Check(button.ToolTip == BufferSubtractionPolicy.CorrectedSourceReason, "missing restriction explanation");
            string status = null;
            EventHandler<string> statusChanged = (_, message) => status = message;
            StatusBarManager.StatusUpdated += statusChanged;
            try { controller.ViewWillAppear(); }
            finally { StatusBarManager.StatusUpdated -= statusChanged; }
            Check(status == BufferSubtractionPolicy.CorrectedSourceReason, "opening the merger hid the restriction explanation");
            using (var activity = ProcessingActivity.TryBegin())
            {
                Check(activity != null && !button.Enabled, "busy merger was enabled");
            }
            Check(!button.Enabled && button.ToolTip == BufferSubtractionPolicy.CorrectedSourceReason,
                "busy completion lost corrected-source restriction");
            second.ClearBufferSubtraction(notify: false);
            Invoke(controller, "ValidateMergeButton");
            Check(button.Enabled, "removing correction did not restore creation");
            Check(button.ToolTip != BufferSubtractionPolicy.CorrectedSourceReason, "restriction tooltip remained");
            controller.ViewWillDisappear();
        }
    }

    static void SubtractionTargets()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var reference = Experiment("blank");
        var ordinary = Experiment("ordinary");
        var tandem = Experiment("tandem");
        tandem.AddSegment(new TandemExperimentSegment(0, tandem.CellConcentration, 0));
        DataManager.AddData(new[] { reference, ordinary, tandem });
        using (var controller = MakeController<BufferSubtractionTool>())
        {
            controller.ViewDidLoad();
            var source = Field<ExperimentMergeQueueDataSource>(controller, "mergeSource");
            Check(source.Items.Any(item => item.Data == ordinary), "ordinary target missing");
            Check(source.Items.All(item => item.Data != tandem), "tandem remained a subtraction target");
            Check(Property<NSTableView>(controller, "SelectListView").ToolTip == BufferSubtractionPolicy.TandemTargetReason,
                "target list lacks restriction explanation");
            controller.ViewWillDisappear();
        }
    }

    static void DetailsAvailability()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var tandem = Experiment("tandem");
        Correct(tandem);
        tandem.AddSegment(new TandemExperimentSegment(0, tandem.CellConcentration, 0));
        foreach (var data in new[] { tandem, Experiment("ordinary") })
        using (var row = new ExperimentAttributeView(new CGRect(0, 0, 680, 22), new ExperimentAttribute(), true, data))
        {
            var keys = Property<NSPopUpButton>(row, "KeySelectionControl");
            Check(keys.Menu.Items.Any(item => item.Tag == (int)AttributeKey.BufferSubtraction) == !data.IsTandemExperiment,
                "wrong subtraction key availability");
        }
        using (var row = new ExperimentAttributeView(new CGRect(0, 0, 680, 22), tandem.Attributes.Single().Copy(), true, tandem))
        {
            foreach (var name in new[] { "EnumPopUpControl", "BufferSubtractionMethodControl" })
            {
                var control = Property<NSPopUpButton>(row, name);
                Check(!control.Enabled && control.ToolTip == BufferSubtractionPolicy.TandemTargetReason,
                    "existing tandem correction remains editable");
            }
            var removed = false;
            row.Remove += (_, __) => removed = true;
            row.Subviews.OfType<NSButton>().Single(button => button.ToolTip == "Remove attribute").PerformClick(row);
            Check(removed, "existing correction cannot be removed");
        }
    }

    static T MakeController<T>() where T : NSViewController
    {
        var handle = Send(Send(new Class(typeof(T)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        var controller = Runtime.GetNSObject<T>(handle);
        controller.View = new NSView(new CGRect(0, 0, 700, 500));
        foreach (var outlet in typeof(T).GetProperties(Private).Where(property => property.GetCustomAttribute<OutletAttribute>() != null))
        {
            // The restriction tests need no graph rendering or storyboard resources.
            if (outlet.PropertyType == typeof(BufferSubtractionGraphView)) continue;
            var value = (NSView)Activator.CreateInstance(outlet.PropertyType);
            if (value is NSSegmentedControl) ((NSSegmentedControl)value).SegmentCount = 3;
            if (value is NSSlider) ((NSSlider)value).MaxValue = 1;
            outlet.SetValue(controller, value);
            controller.View.AddSubview(value);
        }
        if (controller is ExperimentMergerViewController)
        {
            var slider = Property<NSSlider>(controller, "BackMixingSliderControl");
            slider.Action = new Selector("takeFloatValueFrom:");
            slider.Target = Property<NSTextField>(controller, "BackMixFracLabel");
        }
        return controller;
    }

    static void Correct(ExperimentData data) => data.AddOrUpdateAttribute(new BufferSubtractionSettings(
        "stored-blank", BufferSubtractionMethod.MatchedInjection,
        new BufferSubtractionReferenceSnapshot("blank", new[] { new BufferSubtractionReferencePoint(1, new FloatWithError(2e-6)) }))
        .ToAttribute(), notify: false);

    static ExperimentData Experiment(string name)
    {
        var data = new ExperimentData(name)
        {
            CellVolume = 200e-6, CellConcentration = new FloatWithError(20e-6), SyringeConcentration = new FloatWithError(200e-6)
        };
        data.DataPoints.Add(new DataPoint(0, 0, 25));
        data.DataPoints.Add(new DataPoint(1, 0, 25));
        var injection = InjectionData.FromPEAQFile(data, 0, true, 0, 2e-6, 10, 1, 25);
        injection.SetPeakArea(new FloatWithError(10e-6));
        data.Injections.Add(injection);
        return data;
    }

    static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
    static T Property<T>(object instance, string name) => (T)instance.GetType().GetProperty(name, Private).GetValue(instance);
    static void Invoke(object instance, string name) => instance.GetType().GetMethod(name, Private).Invoke(instance, null);
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
