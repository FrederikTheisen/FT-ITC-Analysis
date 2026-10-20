// Exercises the archived Publish controls and their production actions; no app bundle is created.
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using AppKit;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;

static class FinalFigureDimensionInputTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int failures;
    static int passed;

    static int Main(string[] args)
    {
        NSApplication.Init();
        var store = PlatformServices.SettingsStore;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
            PreferencesState.Defaults().Apply();
            AppSettings.PerformOnlineChecksOnLaunch = false;
            foreach (var name in new[] { "en-US", "da-DK" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Run(name + " decimal commas", () => DecimalCommas(args[1]));
                Run(name + " full numeric tokens", () => ValidNumbers(args[1]));
                Run(name + " negative dimensions", () => NegativeNumbers(args[1]));
                Run(name + " invalid input", () => InvalidNumbers(args[1]));
                Run(name + " inclusive dimension limits", () => DimensionLimits(args[1]));
                Run(name + " independent commits and retained drafts", () => DraftsAndRetry(args[1]));
                Run(name + " precision survives refresh", () => PrecisionSurvivesRefresh(args[1]));
                Run(name + " figure construction uses committed sizes", () => FigureDimensions(args[1]));
            }
            Console.WriteLine((failures == 0 ? "PASS: " : "FAIL: ") + passed + " native dimension groups passed; " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
            PlatformServices.RegisterSettingsStore(store);
            CultureInfo.CurrentCulture = culture;
        }
    }

    static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + name + ": " + error); }
    }

    static void DecimalCommas(string storyboard)
    {
        using (var view = new PublishView(storyboard))
        {
            view.Commit(true, "6,5");
            Equal(FinalFigureGraphView.Width, 6.5f, "comma width");
            view.Commit(false, "12,5 cm");
            Equal(FinalFigureGraphView.Height, 12.5f, "comma height");
        }
    }

    static void ValidNumbers(string storyboard)
    {
        using (var view = new PublishView(storyboard))
            foreach (var width in new[] { true, false })
            {
                foreach (var input in new[] { "6.25", "6,25", "6.25cm", " 6,25 CM ", "+6.25 cm", "6.25e0 cm" })
                {
                    view.Commit(width, input);
                    Equal(Value(width), 6.25f, input);
                    Check(!view.Field(width).TextColor.IsEqual(NSColor.SystemRed), "Valid input remains marked invalid: " + input);
                    Check(!string.IsNullOrWhiteSpace(view.Field(width).ToolTip), "Dimension tooltip is missing");
                }
                view.Commit(width, "1e1");
                Equal(Value(width), 10f, "scientific notation");
            }
    }

    static void NegativeNumbers(string storyboard)
    {
        using (var view = new PublishView(storyboard))
            foreach (var width in new[] { true, false })
            {
                var before = Value(width);
                view.Commit(width, "-5");
                Equal(Value(width), before, "negative input must preserve live size");
                Check(view.Field(width).StringValue == "-5", "Negative draft was replaced");
            }
    }

    static void InvalidNumbers(string storyboard)
    {
        using (var view = new PublishView(storyboard))
            foreach (var width in new[] { true, false })
                foreach (var input in new[] { "", " ", "0", "-0", "NaN", "Infinity", "-Infinity", "1e1000", "1,5",
                    "1e2", "abc6cm", "6mm", "6cm cm", "6..5", "6,,5", "6,2.5", "6.2,5", "6 250", "6\t25", "6e" })
                {
                    var before = Value(width);
                    view.Commit(width, input);
                    Equal(Value(width), before, "Invalid input changed size: " + input);
                    Check(view.Field(width).StringValue == input, "Invalid draft was replaced: " + input);
                    Check(view.Field(width).TextColor.IsEqual(NSColor.SystemRed), "Invalid input was not marked: " + input);
                    Check(view.Field(width).ToolTip.Contains(width ? "3" : "4")
                        && view.Field(width).ToolTip.Contains(width ? "20" : "28")
                        && view.Field(width).ToolTip.Contains("cm"), "Invalid tooltip does not explain the range");
                }
    }

    static void DimensionLimits(string storyboard)
    {
        using (var view = new PublishView(storyboard))
            foreach (var width in new[] { true, false })
            {
                foreach (var endpoint in width ? new[] { 3f, 20f } : new[] { 4f, 28f })
                {
                    view.Commit(width, endpoint.ToString(CultureInfo.InvariantCulture));
                    Equal(Value(width), endpoint, "inclusive endpoint");
                }
                foreach (var input in width ? new[] { "2.99", "20.01", "2.9999999999", "20.0000000001" }
                    : new[] { "3.99", "28.01", "3.9999999999", "28.0000000001" })
                {
                    var before = Value(width);
                    view.Commit(width, input);
                    Equal(Value(width), before, "Out-of-range value was changed or clamped");
                    Check(view.Field(width).StringValue == input, "Out-of-range draft was replaced");
                }
            }
    }

    static void DraftsAndRetry(string storyboard)
    {
        using (var view = new PublishView(storyboard))
        {
            view.Field(false).StringValue = "12,25";
            view.Commit(true, "invalid width");
            Equal(FinalFigureGraphView.Width, 6f, "invalid width");
            Equal(FinalFigureGraphView.Height, 12.25f, "valid height must commit independently");
            Check(view.Field(true).StringValue == "invalid width", "Invalid draft disappeared after apply");
            var titles = view.Outlet<NSSwitch>("EmbeddedShowAxisTitles");
            titles.State = titles.State == 0 ? 1 : 0;
            var expectedTitles = titles.State != 0;
            Check(titles.SendAction(titles.Action, titles.Target), "Axis-title action was not dispatched");
            Check(FinalFigureGraphView.ShowAxisTitles == expectedTitles, "Invalid width blocked another valid option");
            var tabs = view.Outlet<NSSegmentedControl>("PublishOptionsTabControl");
            tabs.SelectedSegment = 1;
            Check(tabs.SendAction(tabs.Action, tabs.Target), "Tab action was not dispatched");
            tabs.SelectedSegment = 0;
            Check(tabs.SendAction(tabs.Action, tabs.Target), "General tab action was not dispatched");
            view.Controller.ViewWillAppear();
            PreferencesState.FromSettings().Apply();
            Check(view.Field(true).StringValue == "invalid width", "Ordinary refresh discarded the invalid draft");
            Equal(FinalFigureGraphView.Width, 6f, "Refresh changed the previous width");
            view.Commit(true, "6,25 cm");
            Equal(FinalFigureGraphView.Width, 6.25f, "corrected width");
            Check(!view.Field(true).TextColor.IsEqual(NSColor.SystemRed), "Correction did not clear the invalid marker");
        }
    }

    static void PrecisionSurvivesRefresh(string storyboard)
    {
        using (var view = new PublishView(storyboard))
        {
            view.Commit(true, "6.25 cm");
            view.Commit(false, "12.75 cm");
            view.Controller.ViewWillAppear();
            view.Commit(false, view.Field(false).StringValue);
            Equal(FinalFigureGraphView.Width, 6.25f, "refresh rounded width");
            Equal(FinalFigureGraphView.Height, 12.75f, "refresh rounded height");
        }
    }

    static void FigureDimensions(string storyboard)
    {
        using (var view = new PublishView(storyboard))
        {
            view.Commit(true, "6.25");
            view.Commit(false, "12.75");
            var data = new ExperimentData("dimension-test.itc")
            {
                CellVolume = 200e-6, CellConcentration = new FloatWithError(20e-6),
                SyringeConcentration = new FloatWithError(200e-6), MeasuredTemperature = 25,
            };
            for (var index = 0; index < 4; index++)
            {
                var injection = new InjectionData(data, index, 2e-6, 4e-10, true);
                injection.SetPeakArea(new FloatWithError(-1e-5 * (index + 1)));
                injection.Ratio = index + 1;
                data.Injections.Add(injection);
            }
            var figure = FinalFigureGraphView.SetupForExport(data, null, null, PublicationFigureModelSelection.None);
            Equal((float)figure.PlotDimensions.Width, 6.25f, "constructed figure width");
            Equal((float)figure.PlotDimensions.Height, 12.75f, "constructed figure height");
        }
    }

    static float Value(bool width) => width ? FinalFigureGraphView.Width : FinalFigureGraphView.Height;
    static void Equal(float actual, float expected, string context)
        => Check(actual == expected, context + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture)
            + ", got " + actual.ToString("R", CultureInfo.InvariantCulture));
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    sealed class PublishView : IDisposable
    {
        public readonly FinalFigureViewController Controller;
        readonly NSStoryboard storyboard;
        readonly NSBundle resources;

        public PublishView(string path)
        {
            FinalFigureGraphView.Width = 6;
            FinalFigureGraphView.Height = 10;
            new Class(typeof(FinalFigureGraphView));
            new Class(typeof(FinalFigureViewController));
            new Class(typeof(WorkspaceTabControl));
            new Class(typeof(HuggingTextView));
            resources = NSBundle.FromPath(Path.GetDirectoryName(path));
            storyboard = NSStoryboard.FromName("Main", resources);
            Controller = (FinalFigureViewController)storyboard.InstantiateControllerWithIdentifier("FinalFigureViewController");
            Check(Controller.View != null, "Archived Publish view is missing");
            Check(Field(true).Editable && Field(false).Editable, "Archived dimensions are not editable");
        }

        public T Outlet<T>(string name) => (T)typeof(FinalFigureViewController).GetProperty(name, Instance).GetValue(Controller);
        public NSTextField Field(bool width) => Outlet<NSTextField>(width ? "EmbeddedWidthLabel" : "EmbeddedHeightLabel");
        public void Commit(bool width, string text)
        {
            var field = Field(width);
            field.StringValue = text;
            Check(field.SendAction(field.Action, field.Target), "Archived dimension action was not dispatched");
        }

        public void Dispose()
        {
            AppDelegate.StartPrintOperation -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), Controller,
                typeof(FinalFigureViewController).GetMethod("AppDelegate_StartPrintOperation", Instance));
            Controller.Dispose();
            storyboard.Dispose();
            resources.Dispose();
        }
    }
}
