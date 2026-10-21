using System;
using System.Collections.Generic;
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
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;

static class ResultExporterTemperatureTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int failures;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        PreferencesState.Defaults().Apply();
        AppSettings.ReferenceTemperature = 27;
        new Class(typeof(AnalysisResultExporterViewController));
        new Class(typeof(AnalysisResultTabViewController));
        new Class(typeof(ExperimentMergerListViewCell));
        using (var resources = NSBundle.FromPath(Path.GetDirectoryName(args[1])))
        using (var storyboard = NSStoryboard.FromName("Main", resources))
        {
            Run("exporter temperature choice survives rebuilding the native exporter", () =>
                VerifySessionChoiceSurvivesRebuild(storyboard));
            Run("native Results temperature menu does not write the legacy preference", VerifyResultsTemperatureIsSessionOnly);
            Run("native Results temperature text retains fractional precision across units", VerifyResultsTemperaturePrecision);
            foreach (var allRows in new[] { false, true })
                foreach (var tsv in new[] { false, true })
                {
                    Run("Celsius baseline " + allRows + "/" + tsv, () => Verify(storyboard, allRows, tsv, false));
                    Run("Celsius/Kelvin/Celsius " + allRows + "/" + tsv, () => Verify(storyboard, allRows, tsv, true));
                }
        }
        DataManager.Clear(DataClearMode.ResetSession);
        Console.WriteLine(failures == 0 ? "PASS: all result exporter temperature cases" : "FAIL: " + failures + " result exporter temperature cases");
        return failures == 0 ? 0 : 1;
    }

    static void VerifyResultsTemperatureIsSessionOnly()
    {
        const string legacyKey = "AnalysisResultUseKelvin";
        var defaults = NSUserDefaults.StandardUserDefaults;
        var legacyKeyObject = new NSString(legacyKey);
        var previousLegacyValue = defaults.ValueForKey(legacyKeyObject) as NSObject;
        var sentinel = new NSString("itc156-session-only-test");
        try
        {
            defaults.SetValueForKey(sentinel, legacyKeyObject);
            defaults.Synchronize();
            TemperatureUnitSession.Reset(TemperatureUnit.Celsius);
            using (var resultsController = Allocate<AnalysisResultTabViewController>())
            using (var mainController = Allocate<MainWindowController>())
            {
                var menuItem = (NSMenuItem)typeof(MainWindowController).GetMethod("CreateTemperatureUnitMenuItem", Instance)
                    .Invoke(mainController, new object[] { true });
                var kelvin = menuItem.Submenu.ItemAt(1);
                Check(kelvin.Title == "Kelvin" && kelvin.Enabled, "Native Results temperature menu is missing Kelvin");
                Check(NSApplication.SharedApplication.SendAction(kelvin.Action, kelvin.Target, kelvin),
                    "Native Results Kelvin menu action did not dispatch");
                typeof(AnalysisResultTabViewController).GetMethod("ApplyTemperatureUnit", Instance)
                    .Invoke(resultsController, new object[] { true });
                Check(AnalysisResultTabViewController.CurrentUseKelvin, "Results session did not accept Kelvin");
                Check(Equals(defaults.ValueForKey(legacyKeyObject), sentinel),
                    "Results temperature selector wrote the legacy persistent preference");
            }
        }
        finally
        {
            if (previousLegacyValue == null) defaults.RemoveObject(legacyKeyObject);
            else defaults.SetValueForKey(previousLegacyValue, legacyKeyObject);
            defaults.Synchronize();
        }
    }

    static void VerifyResultsTemperaturePrecision()
    {
        TemperatureUnitSession.Reset(TemperatureUnit.Celsius);
        using (var resultsController = Allocate<AnalysisResultTabViewController>())
        {
            var type = typeof(AnalysisResultTabViewController);
            var result = CreateResult();
            type.GetField("analysisResult", Instance).SetValue(resultsController, result);
            type.GetMethod("BuildParameterEvaluationSection", Instance).Invoke(resultsController, null);
            var field = (NSTextField)type.GetField("evaluationTemperatureControl", Instance).GetValue(resultsController);
            resultsController.View = new NSView(new CGRect(0, 0, 100, 100));
            const double originalCelsius = 31.123456789;

            type.GetField("evaluationTextUsesKelvin", Instance).SetValue(resultsController, false);
            field.StringValue = originalCelsius.ToString("G15", System.Globalization.CultureInfo.InvariantCulture);
            type.GetMethod("ApplyTemperatureUnit", Instance).Invoke(resultsController, new object[] { true });
            Check(Math.Abs(double.Parse(field.StringValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture)
                - (originalCelsius + 273.15)) < 1e-13,
                "Native Results C-to-K conversion did not use the live edited field: " + field.StringValue
                    + "; unit=" + TemperatureUnitSession.Results
                    + "; priorUnit=" + type.GetField("evaluationTextUsesKelvin", Instance).GetValue(resultsController));

            type.GetMethod("ApplyTemperatureUnit", Instance).Invoke(resultsController, new object[] { false });
            Check(Math.Abs(double.Parse(field.StringValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture)
                - originalCelsius) < 1e-13,
                "Native Results K-to-C conversion lost fractional precision");
        }
    }

    static T Allocate<T>() where T : NSObject
    {
        var type = typeof(T);
        var handle = Send(Send(new Class(type).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        return Runtime.GetNSObject<T>(handle);
    }

    static void VerifySessionChoiceSurvivesRebuild(NSStoryboard storyboard)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        TemperatureUnitSession.Reset(AppSettings.DefaultTemperatureUnit);
        var result = CreateResult();
        DataManager.AddData(result.Solution.Solutions.Select(s => (ITCDataContainer)s.Data).Concat(new ITCDataContainer[] { result }));
        using (var first = (AnalysisResultExporterViewController)storyboard.InstantiateControllerWithIdentifier("NSViewController-y9t-uG-ZTy"))
        {
            var firstTemperature = Descendants(first.View).OfType<NSPopUpButton>()
                .Single(p => p.ItemTitles().SequenceEqual(new[] { "Celsius", "Kelvin" }));
            firstTemperature.SelectItem(1);
            Check(firstTemperature.SendAction(firstTemperature.Action, firstTemperature.Target),
                "Actual exporter temperature action did not dispatch");
            Check(firstTemperature.IndexOfSelectedItem == 1, "Actual exporter did not accept Kelvin");
        }
        using (var rebuilt = (AnalysisResultExporterViewController)storyboard.InstantiateControllerWithIdentifier("NSViewController-y9t-uG-ZTy"))
        {
            var rebuiltTemperature = Descendants(rebuilt.View).OfType<NSPopUpButton>()
                .Single(p => p.ItemTitles().SequenceEqual(new[] { "Celsius", "Kelvin" }));
            Check(rebuiltTemperature.IndexOfSelectedItem == 1,
                "Rebuilt exporter discarded the process-session temperature choice");
        }
    }

    static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + name + ": " + error); }
    }

    static void Verify(NSStoryboard storyboard, bool allRows, bool tsv, bool switchUnits)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        TemperatureUnitSession.Reset(AppSettings.DefaultTemperatureUnit);
        var result = CreateResult();
        DataManager.AddData(result.Solution.Solutions.Select(s => (ITCDataContainer)s.Data).Concat(new ITCDataContainer[] { result }));
        // Present the archived view as a sheet so AppKit resolves its control sizes.
        using (var controller = (AnalysisResultExporterViewController)storyboard.InstantiateControllerWithIdentifier("NSViewController-y9t-uG-ZTy"))
        using (var window = new NSWindow(new CGRect(0, 0, 470, 550), NSWindowStyle.Titled, NSBackingStore.Buffered, false))
        using (var parent = new NSViewController { View = new NSView(new CGRect(0, 0, 470, 550)) })
        {
            Check(controller.View != null, "Archived exporter view is missing");
            window.ReleasedWhenClosed = false;
            window.ContentViewController = parent;
            window.OrderFront(null);
            parent.PresentViewControllerAsSheet(controller);
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.1));
            var temperature = Descendants(controller.View).OfType<NSPopUpButton>()
                .SingleOrDefault(p => p.ItemTitles().SequenceEqual(new[] { "Celsius", "Kelvin" }));
            if (switchUnits) Check(temperature != null, "Actual native exporter has no Celsius/Kelvin selector");
            if (temperature != null) Check(temperature.IndexOfSelectedItem == 0 && temperature.Enabled && !temperature.Hidden,
                "Temperature selector must initially offer Celsius");
            if (temperature != null)
            {
                Check(temperature.ToolTip == AnalysisResultExportPresentation.TemperatureToolTip, "Temperature guidance differs between desktops");
                Check(Descendants(temperature.Superview).OfType<NSTextField>().Any(label => label.StringValue == AnalysisResultExportPresentation.TemperatureLabel),
                    "Temperature label is missing");
                controller.View.LayoutSubtreeIfNeeded();
                var frame = temperature.ConvertRectToView(temperature.Bounds, controller.View);
                var bounds = controller.View.Bounds;
                Check(frame.Width >= 100 && frame.Height > 0 && frame.X >= 0 && frame.Y >= 0
                    && frame.X + frame.Width <= bounds.Width && frame.Y + frame.Height <= bounds.Height,
                    "Temperature selector is clipped: control=" + frame + "; view=" + bounds);
            }
            Outlet<NSSegmentedControl>(controller, "ExportTypeControl").SelectedSegment = allRows ? 1 : 0;
            Outlet<NSSegmentedControl>(controller, "ExportFormatControl").SelectedSegment = tsv ? 1 : 0;
            Outlet<NSSegmentedControl>(controller, "ErrorTypeControl").SelectedSegment = 1;
            var copy = Descendants(controller.View).OfType<NSButton>().Single(b => b.Title == "Copy to Clipboard");
            var originalSolution = result.Solution;
            var originalValues = result.Solution.Solutions.Select(s => s.Parameters[ParameterType.Affinity1].Value).ToArray();
            string celsius = null;
            var directory = Path.Combine(Path.GetTempPath(), "itc154-native-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                foreach (var index in switchUnits ? new[] { 0, 1, 0 } : new[] { 0 })
                {
                    if (temperature != null) temperature.SelectItem(index);
                    copy.PerformClick(controller);
                    var text = NSPasteboard.GeneralPasteboard.GetStringForType("NSStringPboardType");
                    Check(!string.IsNullOrEmpty(text), "Copy action did not write a table to the actual pasteboard");
                    var rows = Table(text, tsv);
                    var label = (allRows ? "Temperature" : "Evaluation temperature") + (index == 1 ? " (K)" : " (°C)");
                    var column = Array.IndexOf(rows[0], label);
                    Check(column >= 0, "Wrong temperature header: " + text);
                    var expected = allRows ? new[] { index == 1 ? "293.2" : "20.0", index == 1 ? "303.2" : "30.0" }
                        : new[] { index == 1 ? "300.2" : "27.0" };
                    Check(rows.Skip(1).Select(row => row[column]).SequenceEqual(expected), "Wrong physical temperatures: " + text);
                    if (celsius == null) celsius = text;
                    var before = Table(celsius, tsv);
                    Check(rows.Length == before.Length, "Unit choice changed row count");
                    for (var row = 0; row < rows.Length; row++)
                        Check(before[row].Where((_, i) => i != column).SequenceEqual(rows[row].Where((_, i) => i != column)),
                            "Unit choice changed another exported column");

                    // Exercise the same configured output and protected file writer as Export to File.
                    var output = (string)controller.GetType().GetMethod("BuildOutput", Instance).Invoke(controller, null);
                    var path = Path.Combine(directory, "results." + (tsv ? "tsv" : "csv"));
                    var write = controller.GetType().GetMethod("WriteResultTable", BindingFlags.Static | BindingFlags.NonPublic);
                    var outcome = (ExportOutcome)write.Invoke(null, new object[] { path, output, ExportBatchWriter.CaptureExistingPaths(new[] { path }) });
                    Check(outcome.IsSuccess && File.ReadAllText(path) == text, "File and clipboard output differ");
                    Check(Object.ReferenceEquals(originalSolution, result.Solution), "Export replaced the saved solution");
                    Check(result.Solution.Solutions.Select(s => s.Temp).SequenceEqual(new[] { 20.0, 30.0 }), "Export changed fitted temperatures");
                    Check(originalValues.SequenceEqual(result.Solution.Solutions.Select(s => s.Parameters[ParameterType.Affinity1].Value)),
                        "Export changed affinity values");
                    Check(AppSettings.ReferenceTemperature == 27, "Unit choice changed evaluation temperature preference");
                }
            }
            finally { Directory.Delete(directory, true); parent.DismissViewController(controller); window.Close(); }
        }
    }

    static string[][] Table(string value, bool tsv) => value.Split(new[] { Environment.NewLine }, StringSplitOptions.None)
        .Select(row => row.Split(tsv ? '\t' : ',')).ToArray();

    static IEnumerable<NSView> Descendants(NSView view)
    {
        foreach (var child in view.Subviews)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    static T Outlet<T>(object controller, string name) => (T)controller.GetType().GetProperty(name, Instance).GetValue(controller);
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    static AnalysisResult CreateResult()
    {
        var models = new List<Model>();
        foreach (var temperature in new[] { 20.0, 30.0 })
        {
            var data = new ExperimentData("temperature-export-" + temperature)
            {
                CellConcentration = new FloatWithError(20e-6), SyringeConcentration = new FloatWithError(400e-6),
                CellVolume = 200e-6, MeasuredTemperature = temperature, TargetTemperature = temperature,
            };
            for (var i = 0; i < 3; i++)
            {
                var injection = new InjectionData(data, i, 2e-6, 8e-10, include: true)
                {
                    Ratio = i + 1, ActualCellConcentration = 20e-6, ActualTitrantConcentration = (i + 1) * 5e-6,
                };
                injection.SetPeakArea(new FloatWithError(-2e-6 + i * 1e-7));
                data.Injections.Add(injection);
            }
            var model = new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            models.Add(model);
        }
        var global = new GlobalModel(models);
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(m => m.Solution).ToList(),
            models[0].Solution.Convergence, reconstructBootstrap: false);
        global.Solution = solution;
        return new AnalysisResult(solution);
    }
}
