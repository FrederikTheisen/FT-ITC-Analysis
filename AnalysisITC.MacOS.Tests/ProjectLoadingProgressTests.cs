using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;

static class ProjectLoadingProgressTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        var path = Path.Combine(Path.GetTempPath(), "ftxtc-loading-" + Guid.NewGuid().ToString("N") + ".ftxtc");
        var previousContext = SynchronizationContext.Current;
        var exampleProject = args.Length >= 2 && args[0] == "--project";
        var expectedItemCount = exampleProject ? (args.Length == 3 ? int.Parse(args[2]) : 5) : 2;
        try
        {
            if (exampleProject) File.Copy(args[1], path);
            else Task.Run(() => FTXTCWriter.WriteFileAsync(path, new[]
            {
                new ExperimentData("first.itc") { Name = "First experiment λ" },
                new ExperimentData("second.itc") { Name = "Second experiment 李明" },
            })).GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(new NativeUiContext());
            var handle = Send(Send(new Class(typeof(StatusBarViewController)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
            using (var controller = Runtime.GetNSObject<StatusBarViewController>(handle))
            using (var stream = File.OpenRead(path))
            {
                controller.View = new NSView(new CGRect(0, 0, 800, 30));
                foreach (var outlet in typeof(StatusBarViewController).GetProperties(Instance))
                    if (outlet.GetCustomAttribute<OutletAttribute>() != null)
                    {
                        var control = (NSView)Activator.CreateInstance(outlet.PropertyType);
                        outlet.SetValue(controller, control);
                        controller.View.AddSubview(control);
                    }
                controller.ViewDidLoad();
                var label = (NSTextField)typeof(StatusBarViewController).GetProperty("DocumentStatusLabel", Instance).GetValue(controller);
                var indicator = (NSProgressIndicator)typeof(StatusBarViewController).GetProperty("ProgressIndicator", Instance).GetValue(controller);
                var rendered = new List<string>();
                var validating = false;
                var itemProgress = false;
                var earlyFitProgress = false;
                var finishing = false;
                Task<FtxtcReadResult> read = null;
                EventHandler<string> handler = (sender, message) => NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    if (read != null && !read.IsCompleted)
                    {
                        rendered.Add(label.StringValue);
                        validating |= label.StringValue == "Validating project..." && !indicator.Hidden && indicator.Indeterminate;
                        itemProgress |= !indicator.Hidden && !indicator.Indeterminate
                            && (exampleProject ? indicator.DoubleValue > 0 && indicator.DoubleValue < 100
                                : label.StringValue == "Experiment: Second experiment 李明" && indicator.DoubleValue == 50);
                        earlyFitProgress |= label.StringValue.StartsWith("Fit: ", StringComparison.Ordinal)
                            && !indicator.Hidden && !indicator.Indeterminate && indicator.DoubleValue > 0 && indicator.DoubleValue < 10;
                        finishing |= label.StringValue == "Finishing…" && !indicator.Hidden && indicator.Indeterminate;
                    }
                });
                StatusBarManager.SecondaryStatusUpdated += handler;
                try
                {
                    read = FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict, interactive: true);
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    while (!read.IsCompleted && DateTime.UtcNow < deadline)
                        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.001));
                    Check(read.IsCompleted, "Project loading did not finish while the UI handled updates");
                    var restored = read.GetAwaiter().GetResult();
                    if (!exampleProject)
                    {
                        Check(rendered.Contains("Experiment: First experiment λ"), "First item did not reach the native status bar during loading");
                        Check(rendered.Contains("Experiment: Second experiment 李明"), "Second item did not reach the native status bar during loading");
                    }
                    else
                    {
                        Check(rendered.Exists(text => text.StartsWith("Fit: ", StringComparison.Ordinal)), "Fit name did not reach the native status bar");
                        Check(rendered.Exists(text => text.StartsWith("Result: ", StringComparison.Ordinal)), "Result name did not reach the native status bar");
                        Check(rendered.Exists(text => text.StartsWith("Report: ", StringComparison.Ordinal)), "Report name did not reach the native status bar");
                        Check(earlyFitProgress, "Quick experiments consumed too much of the progress bar");
                    }
                    Check(validating, "Validation did not show indeterminate progress");
                    Check(itemProgress, "Item did not show partial determinate progress");
                    Check(finishing, "Final preparation did not show indeterminate progress");
                    Check(restored.Containers.Length == expectedItemCount && restored.Issues.Count == 0, "Progress updates changed restored content");
                    Check(!label.StringValue.Contains("Experiment:"), "Item status remained after loading");
                    var hideDeadline = DateTime.UtcNow.AddSeconds(2);
                    while (!indicator.Hidden && DateTime.UtcNow < hideDeadline)
                        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.001));
                    Check(indicator.Hidden, "Progress remained visible after loading");
                }
                finally { StatusBarManager.SecondaryStatusUpdated -= handler; }
            }
            Console.WriteLine("PASS: native project loading shows validation, determinate item progress and finishing, then hides");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            StatusBarManager.ClearAppStatus();
            File.Delete(path);
        }
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
