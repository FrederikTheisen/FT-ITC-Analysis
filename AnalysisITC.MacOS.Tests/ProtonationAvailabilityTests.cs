// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh ProtonationAvailabilityTests
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using Foundation;
using CoreGraphics;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Buffer = AnalysisITC.Core.Data.Buffer;

static class ProtonationAvailabilityTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        try
        {
            CheckView(CreateResult(Buffer.MES, Buffer.Hepes), true);
            var result = CreateResult(Buffer.MES, Buffer.Hepes, Buffer.Succinate);
            result.Solution.Solutions[2].Data.Name = "Succinate experiment with a deliberately long descriptive name covering several solution conditions and sample identifiers";
            CheckView(new AnalysisResult(result.Solution), false);
            Console.WriteLine("PASS: native protonation availability, shared visible reason, and narrow wrapped layout");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void CheckView(AnalysisResult result, bool available)
    {
        var handle = Send(Send(new Class(typeof(AnalysisResultTabViewController)).Handle,
            Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        var controller = Runtime.GetNSObject<AnalysisResultTabViewController>(handle);
        typeof(AnalysisResultTabViewController).GetField("analysisResult", Instance).SetValue(controller, result);
        typeof(AnalysisResultTabViewController).GetMethod("RefreshAvailableGraphTypes", Instance).Invoke(controller, null);
        var modes = (IEnumerable)typeof(AnalysisResultTabViewController).GetField("availableGraphTypes", Instance).GetValue(controller);
        Check(modes.Cast<object>().Any(mode => mode.ToString() == "ProtonationAnalysis") == available, "view availability");
        var section = (NSView)typeof(AnalysisResultTabViewController).GetMethod("BuildAvailabilitySection", Instance).Invoke(controller, null);
        var window = new NSWindow(new CGRect(0, 0, 320, 500), NSWindowStyle.Titled, NSBackingStore.Buffered, false);
        try
        {
            var root = window.ContentView;
            root.AddSubview(section);
            section.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor, 16).Active = true;
            section.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor, -16).Active = true;
            section.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor, 16).Active = true;
            window.MakeKeyAndOrderFront(null);
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.1));
            root.LayoutSubtreeIfNeeded();
            var fields = Descendants(section).OfType<NSTextField>().ToList();
            Check(fields.Any(field => field.StringValue == (available ? "Available" : "Unavailable")), "availability status");
            if (!available)
            {
                var reason = fields.Single(field => field.StringValue == result.ProtonationAnalysisUnavailableReason);
                Check(reason.Cell.Wraps && !reason.UsesSingleLineMode, "reason wraps");
                Check(reason.Frame.Height > 3 * reason.Font.PointSize, "long reason occupies multiple lines");
                var frame = reason.ConvertRectToView(reason.Bounds, root);
                Check(frame.X >= 0 && frame.Right <= root.Bounds.Width && frame.Y >= 0 && frame.Bottom <= root.Bounds.Height,
                    "reason fits narrow panel");
                var output = Environment.GetEnvironmentVariable("FTITC_PROJECT_INFORMATION_QA_DIRECTORY");
                if (!string.IsNullOrEmpty(output))
                {
                    Directory.CreateDirectory(output);
                    window.DisplayIfNeeded();
                    using (var windowImage = CGImage.ScreenImage((int)window.WindowNumber, CGRect.Null,
                        CGWindowListOption.IncludingWindow, CGWindowImageOption.BoundsIgnoreFraming | CGWindowImageOption.BestResolution))
                    {
                        Check(windowImage != null, "native window image is available");
                        var content = root.ConvertRectToView(root.Bounds, null);
                        var scale = windowImage.Width / (double)window.Frame.Width;
                        var crop = new CGRect(content.X * scale,
                            (window.Frame.Height - content.Y - content.Height) * scale,
                            content.Width * scale, content.Height * scale);
                        using (var contentImage = windowImage.WithImageInRect(crop))
                        using (var bitmap = new NSBitmapImageRep(contentImage))
                        using (var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary()))
                            File.WriteAllBytes(Path.Combine(output, "cr021-native-reason.png"), png.ToArray());
                    }
                }
            }
            else Check(!fields.Any(field => field.StringValue.Contains("Protonation analysis requires")), "no unavailable reason");
        }
        finally { window.Close(); }
    }

    static IEnumerable<NSView> Descendants(NSView view)
    {
        foreach (var child in view.Subviews)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static ExperimentData CreateData(Buffer? buffer, string name)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name, MeasuredTemperature = 25, TargetTemperature = 25,
            CellConcentration = new FloatWithError(30e-6), SyringeConcentration = new FloatWithError(400e-6), CellVolume = 1.4e-3,
        };
        if (buffer.HasValue)
        {
            var attribute = ExperimentAttribute.FromKey(AttributeKey.Buffer);
            attribute.IntValue = (int)buffer.Value;
            attribute.DoubleValue = 7;
            attribute.ParameterValue = new FloatWithError(0.05);
            data.Attributes.Add(attribute);
        }
        return data;
    }

    static AnalysisResult CreateResult(params Buffer?[] buffers)
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        var models = buffers.Select((buffer, index) =>
        {
            var model = new OneSetOfSites(CreateData(buffer, "experiment-" + (index + 1)));
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            model.Solution = SolutionInterface.FromModel(model, convergence);
            return (Model)model;
        }).ToList();
        var global = new GlobalModel(models) { ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(model => model.Solution).ToList(), convergence);
        global.Solution = solution;
        return new AnalysisResult(solution);
    }
}
