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
        try
        {
            Task.Run(() => FTXTCWriter.WriteFileAsync(path, new[]
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
                var rendered = new List<string>();
                Task<FtxtcReadResult> read = null;
                EventHandler<string> handler = (sender, message) => NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    if (read != null && !read.IsCompleted) rendered.Add(label.StringValue);
                });
                StatusBarManager.SecondaryStatusUpdated += handler;
                try
                {
                    read = FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict, interactive: true);
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!read.IsCompleted && DateTime.UtcNow < deadline)
                        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.001));
                    Check(read.IsCompleted, "Project loading did not finish while the UI handled updates");
                    var restored = read.GetAwaiter().GetResult();
                    Check(rendered.Contains("Experiment: First experiment λ"), "First item did not reach the native status bar during loading");
                    Check(rendered.Contains("Experiment: Second experiment 李明"), "Second item did not reach the native status bar during loading");
                    Check(restored.Containers.Length == 2 && restored.Issues.Count == 0, "Progress updates changed restored content");
                    Check(!label.StringValue.Contains("Experiment:"), "Item status remained after loading");
                }
                finally { StatusBarManager.SecondaryStatusUpdated -= handler; }
            }
            Console.WriteLine("PASS: project item names reach the native status bar while loading and clear afterward");
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
