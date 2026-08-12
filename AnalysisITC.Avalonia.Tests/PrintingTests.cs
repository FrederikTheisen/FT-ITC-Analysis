using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;

using SkiaSharp;

using Xunit;

using AnalysisITC.Avalonia.Printing;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Avalonia.Processing;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class PrintingTests : IDisposable
{
    public PrintingTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void FitCentersAndPreservesAspectRatio()
    {
        var fitted = PrintGeometry.Fit(
            new PrintSize(1600, 900),
            new PrintRect(20, 30, 500, 700));

        Assert.Equal(500, fitted.Width, 6);
        Assert.Equal(281.25, fitted.Height, 6);
        Assert.Equal(20, fitted.X, 6);
        Assert.Equal(239.375, fitted.Y, 6);
    }

    [Fact]
    public void RenderScopeIsNestedAndRestored()
    {
        Assert.False(GraphPrintRenderScope.IsActive);
        using (GraphPrintRenderScope.Enter())
        {
            Assert.True(GraphPrintRenderScope.IsActive);
            using (GraphPrintRenderScope.Enter())
                Assert.True(GraphPrintRenderScope.IsActive);
            Assert.True(GraphPrintRenderScope.IsActive);
        }
        Assert.False(GraphPrintRenderScope.IsActive);
    }

    [Fact]
    public void SnapshotDimensionsUseThreeHundredDpiAndRespectCap()
    {
        Assert.Equal(new PixelSize(750, 375), GraphPrintTarget.CalculatePixelSize(240, 120, out var normalScale));
        Assert.Equal(3.125, normalScale, 6);

        Assert.Equal(new PixelSize(6000, 3000), GraphPrintTarget.CalculatePixelSize(10000, 5000, out var cappedScale));
        Assert.Equal(0.6, cappedScale, 6);
    }

    [Fact]
    public async Task VisualCaptureUsesPrintScopeAndRestoresIt()
    {
        var visual = new ScopeProbeControl();
        var window = new Window { Width = 240, Height = 120, Content = visual };
        window.Show();
        visual.Measure(new Size(240, 120));
        visual.Arrange(new Rect(0, 0, 240, 120));

        AvaloniaGraphSettings.UseDarkTheme();
        try
        {
            using var payload = await GraphPrintTarget.FromVisual("Probe", visual).CaptureAsync();
            Assert.True(visual.RenderedWhilePrinting);
            Assert.True(visual.UsedPrintTheme);
            Assert.False(GraphPrintRenderScope.IsActive);
            Assert.Same(AvaloniaGraphTheme.Dark, AvaloniaGraphSettings.Current);
            Assert.Equal(750, payload.Bitmap.Width);
            Assert.Equal(375, payload.Bitmap.Height);
            Assert.Equal(new PrintSize(240, 120), payload.PdfPageSize);
            Assert.NotEmpty(payload.Pdf);
            Assert.True(payload.PreservePdf);
            var pdfText = System.Text.Encoding.ASCII.GetString(payload.Pdf);
            Assert.Contains("/MediaBox [0 0 240 120]", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);
        }
        finally
        {
            window.Close();
            AvaloniaGraphSettings.UseLightTheme();
        }
    }

    [Fact]
    public async Task PreparedPayloadIsPassedToBackendOnce()
    {
        using var bitmap = new SKBitmap(10, 5);
        using var payload = new GraphPrintPayload("Probe", bitmap.Copy(), new byte[] { 1, 2, 3 });
        var backend = new FakeBackend();

        var outcome = await GraphPrintCoordinator.PrintPreparedAsync(new Window(), payload, backend);

        Assert.Equal(PrintOutcome.Printed, outcome);
        Assert.Equal(1, backend.Calls);
        Assert.Same(payload, backend.Payload);
    }

    [Fact]
    public void PrintCompletionRetiresPreparingStatus()
    {
        var messages = new System.Collections.Generic.List<string>();
        EventHandler<string> handler = (_, message) => messages.Add(message);
        StatusBarManager.ClearAppStatus();
        StatusBarManager.StatusUpdated += handler;
        try
        {
            StatusBarManager.SetStatus("Preparing graph for printing...", 0);
            GraphPrintCoordinator.ReportOutcome(PrintOutcome.Saved, 30);
            System.Threading.Thread.Sleep(100);

            Assert.Contains("PDF saved", messages);
            Assert.NotEqual("Preparing graph for printing...", messages[^1]);
        }
        finally
        {
            StatusBarManager.StatusUpdated -= handler;
            StatusBarManager.ClearAppStatus();
        }
    }

    [Fact]
    public void EmptyWorkspacesDoNotExposePrintTargets()
    {
        Assert.False(new ProcessingWorkspaceControl().TryGetPrintTarget(out _));
        Assert.False(new AnalysisWorkspaceControl().TryGetPrintTarget(out _));
        Assert.False(new FinalFigureWorkspaceControl().TryGetPrintTarget(out _));
        Assert.False(new AnalysisResultWorkspaceControl().TryGetPrintTarget(out _));
    }

    [Fact]
    public void ProcessingWorkspaceExposesRawThermogramGraph()
    {
        var experiment = new ExperimentData("print-test.itc")
        {
            DataPoints = new System.Collections.Generic.List<DataPoint>
            {
                new(0, 1),
                new(1, 2)
            }
        };
        var workspace = new ProcessingWorkspaceControl { Experiment = experiment };

        Assert.True(workspace.TryGetPrintTarget(out var target));
        Assert.NotNull(target);
    }

    public void Dispose()
    {
        while (GraphPrintRenderScope.IsActive)
            throw new InvalidOperationException("A print render scope leaked from a test.");
    }

    sealed class ScopeProbeControl : Control
    {
        public bool RenderedWhilePrinting { get; private set; }
        public bool UsedPrintTheme { get; private set; }

        public override void Render(DrawingContext context)
        {
            RenderedWhilePrinting = GraphPrintRenderScope.IsActive;
            UsedPrintTheme = ReferenceEquals(AvaloniaGraphSettings.CurrentForRender, AvaloniaGraphTheme.Print);
            context.FillRectangle(Brushes.White, Bounds);
            context.DrawLine(new Pen(Brushes.Black, 2), new Point(10, 80), new Point(220, 20));
        }
    }

    sealed class FakeBackend : IGraphPrintBackend
    {
        public int Calls { get; private set; }
        public GraphPrintPayload? Payload { get; private set; }

        public Task<PrintOutcome> PrintAsync(Window owner, GraphPrintPayload payload)
        {
            Calls++;
            Payload = payload;
            return Task.FromResult(PrintOutcome.Printed);
        }
    }
}
