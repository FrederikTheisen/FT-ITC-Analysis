using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppKit;
using Foundation;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.UI.MacOS;

static class TraceabilityStartupGateTests
{
    static int Main(string[] args)
    {
        NSApplication.Init();
        SynchronizationContext.SetSynchronizationContext(new NativeUiContext());
        var scenario = args[1];
        var mode = scenario != "mode-off";
        var confirm = scenario == "confirm-enabled";
        var userName = scenario == "blank-name" || scenario == "cancel-blank" ? "" : "Ada Lovelace";
        var cancel = scenario == "cancel-blank";
        AppSettings.TraceabilityModeEnabled = mode;
        AppSettings.ConfirmUserAtStartup = confirm;
        AppSettings.UserName = userName;
        var shouldConfirm = TraceabilityPresentation.ShouldConfirmAtStartup(mode, confirm, userName);
        var order = new List<string>();
        Task pendingRead = null;
        if (cancel)
        {
            using (var url = NSUrl.FromFilename("/tmp/traceability-startup-must-not-open.ftxtc"))
                pendingRead = MacDataReader.ReadAsync(new[] { url });
        }

        try
        {
            var continued = Wait(MacDataReader.BeginStartupAsync(() =>
            {
                if (!shouldConfirm) return Task.FromResult(true);
                order.Add("confirm");
                return Task.FromResult(!cancel);
            }, () =>
            {
                order.Add("recovery");
                return Task.CompletedTask;
            }));

            Check(continued == !cancel, "Startup continuation did not match confirmation outcome.");
            if (cancel)
            {
                Wait(pendingRead);
                Check(!MacDataReader.StartupComplete && !MacDataReader.IsReadPending,
                    "Cancelled startup became ready or retained a pending file read.");
                Check(DataManager.SourceItems.Count == 0, "Cancelled startup opened queued data.");
                Check(order.SequenceEqual(new[] { "confirm" }), "Recovery ran before cancellation stopped startup.");
            }
            else
            {
                var expected = shouldConfirm ? new[] { "confirm", "recovery" } : new[] { "recovery" };
                Check(order.SequenceEqual(expected), "Confirmation and recovery ran in the wrong order.");
                Check(MacDataReader.StartupComplete, "Startup did not become ready.");
            }

            Console.WriteLine("PASS: native traceability startup gate " + scenario);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    static T Wait<T>(Task<T> task)
    {
        while (!task.IsCompleted) NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
        return task.GetAwaiter().GetResult();
    }

    static void Wait(Task task)
    {
        while (!task.IsCompleted) NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
        task.GetAwaiter().GetResult();
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class NativeUiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object state) =>
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() => callback(state));
    }
}
