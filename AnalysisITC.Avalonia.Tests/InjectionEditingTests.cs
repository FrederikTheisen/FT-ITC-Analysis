using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class InjectionEditingTests
{
    [AvaloniaFact]
    public void BufferGraphPointerCannotChangeIncludedHeatsWhileProcessing()
    {
        var experiment = new ExperimentData("buffer.itc");
        for (var i = 0; i < 3; i++)
        {
            var injection = new InjectionData(experiment, i, 2e-6, 1e-9, include: true);
            injection.SetPeakArea(new FloatWithError((i + 1) * 1e-6, 1e-8));
            experiment.Injections.Add(injection);
        }
        var graph = new BufferSubtractionGraphControl();
        graph.SetData(experiment, Array.Empty<ExperimentData>(), null, false);
        var window = new Window { Width = 800, Height = 600, Content = graph };
        window.Show();
        try
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(800, 600));
            bitmap.Render(graph);
            var points = (List<(InjectionData Injection, Point Point)>)typeof(BufferSubtractionGraphControl)
                .GetField("bufferPointPositions", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(graph)!;
            var hit = points.First();
            var changes = 0;
            graph.BufferPointIncludeChanged += (_, _) => changes++;
            using (ProcessingActivity.Begin())
            {
                window.MouseDown(hit.Point, MouseButton.Left);
                window.MouseUp(hit.Point, MouseButton.Left);
                Assert.True(hit.Injection.Include);
                Assert.Equal(0, changes);
            }
            window.MouseDown(hit.Point, MouseButton.Left);
            window.MouseUp(hit.Point, MouseButton.Left);
            Assert.False(hit.Injection.Include);
            Assert.Equal(1, changes);
        }
        finally { window.Close(); }
    }
}
