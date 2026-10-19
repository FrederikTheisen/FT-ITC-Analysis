using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;
using Foundation;
using AnalysisITC.Platform;
using AnalysisITC.UI.MacOS;

static class DataValidationPromptTests
{
    static int Main(string[] args)
    {
        NSApplication.Init();
        try
        {
            CheckPrompt(true, new[] { "Discard", "Keep" }, "Keep", DataValidationPromptAction.Keep);
            CheckPrompt(true, new[] { "Discard", "Keep" }, "Discard", DataValidationPromptAction.Discard);
            CheckPrompt(false, new[] { "Cancel" }, "Cancel", DataValidationPromptAction.Discard);
            Console.WriteLine("PASS: native validation offers Keep/Discard without repair, and only Cancel for rejected raw imports");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    static void CheckPrompt(bool allowKeep, string[] expectedButtons, string selectedButton, DataValidationPromptAction expectedAction)
    {
        Exception failure = null;
        var clicked = false;
        using (var input = NSTimer.CreateRepeatingTimer(0.05, timer =>
        {
            if (clicked) return;
            try
            {
                var window = NSApplication.SharedApplication.ModalWindow;
                if (window == null) return;
                var buttons = Buttons(window.ContentView).ToArray();
                if (!buttons.Select(button => button.Title).OrderBy(title => title)
                    .SequenceEqual(expectedButtons.OrderBy(title => title)))
                    throw new Exception("Unexpected validation prompt buttons: " + string.Join(", ", buttons.Select(button => button.Title)));
                clicked = true;
                buttons.Single(button => button.Title == selectedButton).PerformClick(window);
            }
            catch (Exception error)
            {
                failure = error;
                NSApplication.SharedApplication.AbortModal();
            }
        }))
        using (var timeout = NSTimer.CreateTimer(5, timer =>
        {
            failure = new Exception("Validation prompt did not respond.");
            NSApplication.SharedApplication.AbortModal();
        }))
        {
            NSRunLoop.Main.AddTimer(input, NSRunLoopMode.ModalPanel);
            NSRunLoop.Main.AddTimer(timeout, NSRunLoopMode.ModalPanel);
            try
            {
                var result = new MacDataValidationPromptService().AskValidationIssue(
                    "Incomplete thermogram", "Review the recorded injection coverage.", false, false, allowKeep);
                if (failure != null) throw failure;
                if (!clicked || result.Action != expectedAction) throw new Exception("Validation prompt returned the wrong action.");
            }
            finally { input.Invalidate(); timeout.Invalidate(); }
        }
    }

    static IEnumerable<NSButton> Buttons(NSView view)
    {
        var button = view as NSButton;
        if (button != null) yield return button;
        foreach (var child in view.Subviews)
            foreach (var nested in Buttons(child)) yield return nested;
    }
}
