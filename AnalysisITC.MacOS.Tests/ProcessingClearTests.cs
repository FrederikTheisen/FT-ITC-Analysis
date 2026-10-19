// Native menu/reset checks; no app bundle, signing or packaging.
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using CoreGraphics;
using ObjCRuntime;
using AppKit;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;

static class ProcessingClearTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        var app = new AppDelegate();
        try
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var data = Experiment();
            DataManager.AddData(data);
            Check(!app.ValidateMenuItem(Menu("deleteallresults")), "result deletion enabled with no saved results");
            Check(app.ValidateMenuItem(Menu("clearprocessingselected")), "selected processing clear disabled");
            var processor = data.Processor;
            processor.DiscardIntegratedPoints = !AppSettings.DiscardIntegrationRegionForBaseline;
            var controller = CreateController(data);
            var handler = (EventHandler<ExperimentData>)Delegate.CreateDelegate(typeof(EventHandler<ExperimentData>), controller,
                typeof(DataProcessingViewControllet).GetMethod("OnProcessingDataChanged", Instance));
            DataManager.DataDidChange += handler;
            try
            {
                Check(NSApplication.SharedApplication.SendAction(new Selector("clearProcessingSelected:"), app, null),
                    "native menu selector was not dispatched");
                Check(!ReferenceEquals(processor, data.Processor), "processor was not replaced");
                Check(data.Processor.BaselineType == BaselineInterpolatorTypes.Spline, "default spline was not installed");
                Check(!data.CanBeAnalyzed && data.BaseLineCorrectedDataPoints == null, "calculated processing survived");
                Check(double.IsNaN(data.Injections[0].RawPeakArea.Value), "cleared heat is not unavailable");
                Check(!app.ValidateMenuItem(Menu("clearprocessingall")), "already cleared data remained eligible");
                Check(Outlet<NSSegmentedControl>(controller, "InterpolatorTypeControl").SelectedSegment == 0,
                    "native inspector still displayed the old baseline method");
                Check(Outlet<NSSlider>(controller, "IntegrationLengthControl").FloatValue == 20,
                    "native inspector did not rebind integration controls");
                Check(Outlet<NSButton>(controller, "SplineDiscardIntegrationRange").State ==
                    (AppSettings.DiscardIntegrationRegionForBaseline ? NSCellStateValue.On : NSCellStateValue.Off),
                    "native discard checkbox retained its old cached state");
                typeof(DataProcessingViewControllet).GetMethod("InitializeData", Instance).Invoke(controller, null);
                Check(!data.CanBeAnalyzed, "opening processing tab recreated calculated heats");
            }
            finally
            {
                DataManager.DataDidChange -= handler;
                controller.Dispose();
            }

            var first = Experiment();
            var second = Experiment();
            DataManager.AddData(new[] { first, second });
            var savedPreference = AppSettings.ConfirmRemoveDelete;
            AppSettings.ConfirmRemoveDelete = false;
            var cancelled = false;
            using (var timer = NSTimer.CreateTimer(TimeSpan.FromMilliseconds(30), _ =>
            {
                cancelled = NSApplication.SharedApplication.ModalWindow != null;
                NSApplication.SharedApplication.AbortModal();
            }))
            {
                NSRunLoop.Current.AddTimer(timer, NSRunLoopMode.ModalPanel);
                app.ClearProcessingAll(null);
            }
            AppSettings.ConfirmRemoveDelete = savedPreference;
            Check(cancelled, "multi-experiment reset did not require confirmation");
            Check(first.CanBeAnalyzed && second.CanBeAnalyzed, "cancelled reset changed experiments");
            Check(DataManager.DeletedDataList.Count == 0, "processing reset added deletion undo history");
            Console.WriteLine("PASS: native reset validation, single reset, tab reopening and batch cancellation");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally { DataManager.Clear(DataClearMode.ResetSession); app.Dispose(); }
    }
    static T Create<T>() where T : NSObject
    {
        var handle = Send(Send(new Class(typeof(T)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        return Runtime.GetNSObject<T>(handle);
    }
    static T Outlet<T>(DataProcessingViewControllet controller, string name) =>
        (T)typeof(DataProcessingViewControllet).GetProperty(name, Instance).GetValue(controller);
    static DataProcessingViewControllet CreateController(ExperimentData data)
    {
        var controller = Create<DataProcessingViewControllet>();
        controller.View = new NSView(new CGRect(0, 0, 800, 600));
        foreach (var outlet in typeof(DataProcessingViewControllet).GetProperties(Instance)
            .Where(property => property.GetCustomAttribute<OutletAttribute>() != null))
        {
            var value = outlet.PropertyType == typeof(DataProcessingGraphView) ? Create<DataProcessingGraphView>()
                : outlet.PropertyType == typeof(IntegrationRangeSlider)
                    ? (NSObject)new IntegrationRangeSlider(new CGRect(20, 20, 200, 20))
                    : (NSObject)Activator.CreateInstance(outlet.PropertyType);
            if (value is NSSegmentedControl) ((NSSegmentedControl)value).SegmentCount = 6;
            outlet.SetValue(controller, value);
        }
        Outlet<DataProcessingGraphView>(controller, "BaselineGraphView").Initialize(data);
        return controller;
    }

    static NSMenuItem Menu(string identifier) { return new NSMenuItem { Identifier = identifier }; }
    static ExperimentData Experiment()
    {
        var data = new ExperimentData("native-clear.itc") { CellConcentration = new FloatWithError(30e-6),
            SyringeConcentration = new FloatWithError(400e-6), CellVolume = 1.4e-3 };
        for (var time = 0; time <= 100; time++) data.DataPoints.Add(new DataPoint(time, 1));
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        var injection = InjectionData.FromPEAQFile(data, 0, true, 20, 2e-6, 40, 2, 25);
        injection.SetPeakArea(new FloatWithError(-2e-6, 1e-8));
        data.Injections.Add(injection);
        return data;
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
