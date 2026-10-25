using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AppKit;
using Foundation;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Processing;
using AnalysisITC.Platform;
using AnalysisITC.UI.MacOS;

// Exercise the real alert button actions and modal event loop.
static class TandemImportPromptTests
{
    static int Main(string[] args)
    {
        NSApplication.Init();
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CorrectInvalidInputs();
            RejectInvalidDeadVolumes();
            RejectInvalidMixingFractions();
            ChooseMicroCalAfterError(0);
            ChooseMicroCalAfterError(1);
            CorrectDuringFileImport(args[0]);
            AcceptValidInputs("80", "0", 0);
            AcceptValidInputs("80", "100", 1);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
            AcceptValidInputs("80,5", "25,5", .255);
            AcceptValidInputs("80.5", "25.5", .255);
            Console.WriteLine("PASS: native tandem import retains its dialog and drafts after invalid input, supports correction or MicroCal, and accepts boundary/localized values");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    static void CorrectInvalidInputs()
    {
        var defaults = TandemConcatenation.BackMixingSettings.MicroCalDefault();
        NSWindow original = null;
        var result = RunPrompt(defaults, 3, (step, window) =>
        {
            var fields = Fields(window);
            var overflow = Views<NSButton>(window.ContentView).Single(b => b.Title == "Remove overflow between segments");
            if (step == 0)
            {
                original = window;
                fields[0].StringValue = "0";
                fields[1].StringValue = "200";
                overflow.State = NSCellStateValue.Off;
            }
            else
            {
                Check(window.Handle == original.Handle && window.IsVisible, "the original dialog must remain open");
                Check(overflow.State == NSCellStateValue.Off, "overflow choice must survive validation");
                Check(fields[1].StringValue == "200", "mixing input must survive validation");
                var error = Views<NSTextField>(window.ContentView).Single(f => !f.Hidden && f.StringValue.Contains("must be"));
                Check(error.Frame.Height >= error.Cell.CellSizeForBounds(error.Bounds).Height, "validation text must fit");
                if (step == 1)
                {
                    Check(fields[0].StringValue == "0" && error.StringValue.StartsWith("Dead volume"), "dead-volume error and draft");
                    fields[0].StringValue = "65";
                }
                else
                {
                    Check(fields[0].StringValue == "65" && error.StringValue.StartsWith("Mixing fraction"), "mixing error and corrected dead volume");
                    fields[1].StringValue = "25";
                }
            }
            Click(window, "Use Back-Mixing Compensation");
        });
        Check(result.UseBackMixingMethod && Math.Abs(result.DeadVolume - 65e-6) < 1e-15
            && result.MixingFraction == .25 && !result.DidRemoveOverflow, "corrected settings must be returned");
        Check(result.RemoveOverflowVolume == defaults.RemoveOverflowVolume, "retain the supplied overflow volume");
        Check(defaults.DeadVolume == 80e-6 && !defaults.UseBackMixingMethod && defaults.DidRemoveOverflow,
            "validation and correction must not mutate defaults");
    }

    static void ChooseMicroCalAfterError(int field)
    {
        var defaults = TandemConcatenation.BackMixingSettings.MicroCalDefault();
        var result = RunPrompt(defaults, 2, (step, window) =>
        {
            if (step == 0)
            {
                Fields(window)[field].StringValue = "NaN";
                Click(window, "Use Back-Mixing Compensation");
            }
            else Click(window, "Use MicroCal Concat");
        });
        Check(ReferenceEquals(result, defaults), "MicroCal must remain available with invalid compensation inputs");
    }

    static void RejectInvalidMixingFractions()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var cultureName in new[] { "en-US", "da-DK" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                foreach (var text in new[] { "NaN", "Infinity", "-Infinity", "1e999", "-1e999", "-1", "101", "", "abc", "25%" })
                {
                    var defaults = TandemConcatenation.BackMixingSettings.MicroCalDefault();
                    NSWindow original = null;
                    var result = RunPrompt(defaults, 2, (step, window) =>
                    {
                        var fields = Fields(window);
                        var overflow = Views<NSButton>(window.ContentView).Single(b => b.Title == "Remove overflow between segments");
                        if (step == 0)
                        {
                            original = window;
                            fields[0].StringValue = "65";
                            fields[1].StringValue = text;
                            overflow.State = NSCellStateValue.Off;
                        }
                        else
                        {
                            Check(window.Handle == original.Handle && window.IsVisible, "invalid mixing fraction must retain the same dialog");
                            Check(fields[0].StringValue == "65" && fields[1].StringValue == text
                                && overflow.State == NSCellStateValue.Off, "invalid mixing fraction must retain all drafts");
                            Check(Views<NSTextField>(window.ContentView).Any(f => !f.Hidden && f.StringValue.StartsWith("Mixing fraction must be")),
                                "invalid mixing fraction must show an inline field error");
                            Check(defaults.DeadVolume == 80e-6 && defaults.MixingFraction == 0
                                && defaults.DidRemoveOverflow && !defaults.UseBackMixingMethod, "invalid input must preserve defaults");
                            fields[1].StringValue = cultureName == "da-DK" ? "25,5" : "25.5";
                        }
                        Click(window, "Use Back-Mixing Compensation");
                    });
                    Check(Math.Abs(result.DeadVolume - 65e-6) < 1e-15 && result.MixingFraction == .255
                        && !result.DidRemoveOverflow && result.UseBackMixingMethod,
                        "mixing-fraction correction must retain the other drafts and complete the same prompt");
                }
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    static void RejectInvalidDeadVolumes()
    {
        foreach (var text in new[] { "", "abc", "80 uL", "-1", "0", "NaN", "Infinity", "-Infinity", "1e999", "1e-320" })
        {
            var defaults = TandemConcatenation.BackMixingSettings.MicroCalDefault();
            NSWindow original = null;
            var result = RunPrompt(defaults, 2, (step, window) =>
            {
                var fields = Fields(window);
                var overflow = Views<NSButton>(window.ContentView).Single(b => b.Title == "Remove overflow between segments");
                if (step == 0)
                {
                    original = window;
                    fields[0].StringValue = text;
                    fields[1].StringValue = "25";
                    overflow.State = NSCellStateValue.Off;
                }
                else
                {
                    Check(window.Handle == original.Handle && window.IsVisible, "invalid dead volume must retain the same dialog");
                    Check(fields[0].StringValue == text && fields[1].StringValue == "25"
                        && overflow.State == NSCellStateValue.Off, "invalid dead volume must retain all drafts");
                    Check(Views<NSTextField>(window.ContentView).Any(f => !f.Hidden && f.StringValue.StartsWith("Dead volume must be")),
                        "invalid dead volume must show an inline field error");
                    Check(defaults.DeadVolume == 80e-6 && !defaults.UseBackMixingMethod, "invalid input must preserve defaults");
                    fields[0].StringValue = "65";
                }
                Click(window, "Use Back-Mixing Compensation");
            });
            Check(Math.Abs(result.DeadVolume - 65e-6) < 1e-15 && result.MixingFraction == .25
                && !result.DidRemoveOverflow, "correction must complete the same prompt with its retained settings");
        }
    }

    static void AcceptValidInputs(string deadVolume, string fraction, double expectedFraction)
    {
        var result = RunPrompt(TandemConcatenation.BackMixingSettings.MicroCalDefault(), 1, (_, window) =>
        {
            var fields = Fields(window);
            fields[0].StringValue = deadVolume;
            fields[1].StringValue = fraction;
            Click(window, "Use Back-Mixing Compensation");
        });
        Check(result.UseBackMixingMethod && result.MixingFraction == expectedFraction && result.DidRemoveOverflow,
            "valid compensation inputs must close the dialog and retain settings");
        Check(Math.Abs(result.DeadVolume - (deadVolume == "80" ? 80e-6 : 80.5e-6)) < 1e-15,
            "dead volume must convert from microliters to liters");
    }

    static void CorrectDuringFileImport(string repository)
    {
        var original = PlatformServices.TandemImportPromptService;
        try
        {
            PlatformServices.RegisterTandemImportPromptService(new MacTandemImportPromptService());
            var path = Path.Combine(repository, "AnalysisITC.Tests", "Tandem", "Lenette112+113+114.itc");
            var reader = typeof(DataReader).Assembly.GetType("AnalysisITC.Core.DataReaders.MicroCalITC200Reader")
                .GetMethod("ReadPath");
            var imported = RunModal(() => (ExperimentData)reader.Invoke(null, new object[] { path }), 2, (step, window) =>
            {
                var fields = Fields(window);
                fields[0].StringValue = "80";
                fields[1].StringValue = step == 0 ? "200" : "30";
                Click(window, "Use Back-Mixing Compensation");
            });
            Check(imported.IsTandemExperiment && imported.Segments.Count == 3 && imported.InjectionCount > 0,
                "the same file import must complete after correcting its options");
            Check(imported.TandemMergeDescription.Contains("MixFrac=30.0%"),
                "the import must use the corrected compensation setting");
        }
        finally { PlatformServices.RegisterTandemImportPromptService(original); }
    }

    static TandemConcatenation.BackMixingSettings RunPrompt(
        TandemConcatenation.BackMixingSettings defaults, int expectedSteps, Action<int, NSWindow> action)
        => RunModal(() => new MacTandemImportPromptService().AskBackMixingSettings("tandem.itc", 3, defaults),
            expectedSteps, action);

    static T RunModal<T>(Func<T> operation, int expectedSteps, Action<int, NSWindow> action)
    {
        Exception failure = null;
        var step = 0;
        using (var input = NSTimer.CreateRepeatingTimer(.1, timer =>
        {
            try
            {
                Check(step < expectedSteps, "prompt did not finish after the expected actions");
                var window = NSApplication.SharedApplication.ModalWindow;
                Check(window != null, "tandem prompt must be modal");
                action(step++, window);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                failure = error;
                NSApplication.SharedApplication.AbortModal();
            }
        }))
        using (var timeout = NSTimer.CreateTimer(5, timer =>
        {
            Console.Error.WriteLine("Native tandem prompt timed out");
            failure = new Exception("Tandem prompt did not respond.");
            NSApplication.SharedApplication.AbortModal();
        }))
        {
            NSRunLoop.Main.AddTimer(input, NSRunLoopMode.ModalPanel);
            NSRunLoop.Main.AddTimer(timeout, NSRunLoopMode.ModalPanel);
            try
            {
                var result = operation();
                if (failure != null) throw failure;
                Check(step == expectedSteps, "dialog closed before inputs could be corrected");
                return result;
            }
            finally { input.Invalidate(); timeout.Invalidate(); }
        }
    }

    static NSTextField[] Fields(NSWindow window) => Views<NSTextField>(window.ContentView)
        .Where(f => f.Editable).OrderByDescending(f => f.Frame.Y).ToArray();

    static void Click(NSWindow window, string title) =>
        Views<NSButton>(window.ContentView).Single(button => button.Title == title).PerformClick(window);

    static IEnumerable<T> Views<T>(NSView view) where T : NSView
    {
        var match = view as T;
        if (match != null) yield return match;
        foreach (var child in view.Subviews)
            foreach (var nested in Views<T>(child)) yield return nested;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
