using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using AnalysisITC.UI.MacOS;

static class FtxtcDuplicatePromptTests
{
    static int Main(string[] args)
    {
        NSApplication.Init();
        try
        {
            var summary = new FtxtcDuplicateSummary("overlapping.ftxtc", 2, 1, 1, 3, new[] { "Experiment A", "Result A" });
            var factory = typeof(MacFtxtcDuplicatePromptService).GetMethod("CreateAlert", BindingFlags.Static | BindingFlags.NonPublic);
            using (var alert = (NSAlert)factory.Invoke(null, new object[] { summary }))
            {
                Check(alert.AlertStyle == NSAlertStyle.Warning, "warning style");
                Check(alert.MessageText == FtxtcDuplicatePresentation.Title, "shared title");
                Check(alert.Buttons[0].Title == FtxtcDuplicatePresentation.SkipLabel, "default skip action");
                Check(alert.Buttons[1].Title == FtxtcDuplicatePresentation.CopyLabel, "explicit copy action");
                Check(alert.Buttons[0].ToolTip == FtxtcDuplicatePresentation.SkipToolTip, "skip tooltip");
                Check(alert.Buttons[1].ToolTip == FtxtcDuplicatePresentation.CopyToolTip, "copy tooltip");
                Check(alert.Window.DefaultButtonCell.Handle == alert.Buttons[0].Cell.Handle, "Enter skips");
                Check((alert.Window.StyleMask & NSWindowStyle.Closable) != 0, "window can close");
            }
            RunAction(summary, window => SendKey(window, "\r", 36), FtxtcDuplicateAction.SkipDuplicates);
            RunAction(summary, window => SendKey(window, "\u001b", 53), FtxtcDuplicateAction.SkipDuplicates);
            RunAction(summary, window => window.PerformClose(window), FtxtcDuplicateAction.SkipDuplicates);
            RunAction(summary, window => Buttons(window.ContentView)
                .Single(button => button.Title == FtxtcDuplicatePresentation.CopyLabel).PerformClick(window),
                FtxtcDuplicateAction.ImportCopies);
            Console.WriteLine("PASS: native duplicate warning, shared text/tooltips, Enter, Escape, closing, explicit copy and layout");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    static void RunAction(FtxtcDuplicateSummary summary, Action<NSWindow> action, FtxtcDuplicateAction expected)
    {
        Exception failure = null;
        using (var input = NSTimer.CreateTimer(0.1, timer =>
        {
            try
            {
                var window = NSApplication.SharedApplication.ModalWindow;
                Check(window.Frame.Width > 0 && window.Frame.Height > 0,
                    "native warning layout: " + window.Frame);
                action(window);
            }
            catch (Exception error)
            {
                failure = error;
                NSApplication.SharedApplication.AbortModal();
            }
        }))
        using (var timeout = NSTimer.CreateTimer(3, timer =>
        {
            failure = new Exception("Native prompt did not respond to the action.");
            NSApplication.SharedApplication.AbortModal();
        }))
        {
            NSRunLoop.Main.AddTimer(input, NSRunLoopMode.ModalPanel);
            NSRunLoop.Main.AddTimer(timeout, NSRunLoopMode.ModalPanel);
            var result = new MacFtxtcDuplicatePromptService().ChooseAction(summary);
            input.Invalidate();
            timeout.Invalidate();
            if (failure != null) throw failure;
            Check(result == expected, "native modal action");
        }
    }

    static void SendKey(NSWindow window, string key, ushort code)
    {
        using (var input = NSEvent.KeyEvent(NSEventType.KeyDown, new CGPoint(), (NSEventModifierMask)0,
            0, window.WindowNumber, null, key, key, false, code))
            NSApplication.SharedApplication.PostEvent(input, false);
    }

    static IEnumerable<NSButton> Buttons(NSView view)
    {
        var button = view as NSButton;
        if (button != null) yield return button;
        foreach (var child in view.Subviews)
            foreach (var nested in Buttons(child)) yield return nested;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
