using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Processing;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Avalonia;
using Avalonia.Media;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ScientificGraphDecimalCultureTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void ResultAndBufferGraphAxisLabelsUsePeriods(string cultureName)
    {
        InCulture(cultureName, () =>
        {
            foreach (var (type, method) in new[]
            {
                (typeof(ResultParameterGraphControl), "FormatAxisValue"),
                (typeof(ResultDependenceGraphControl), "FormatAxisValue"),
                (typeof(BufferSubtractionGraphControl), "Format"),
            })
            {
                var text = (string)type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { -1.25 })!;
                Assert.Equal("-1.25", text);
            }
        });
    }

    [AvaloniaTheory]
    [InlineData("en-US")]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void ProcessingAndAnalysisHoverNumbersUsePeriods(string cultureName)
    {
        var original = PreferencesState.FromSettings();
        try
        {
            PreferencesState.Defaults().ApplyToSettings();
            var data = Experiment();
            var analysis = new IntegratedHeatsGraphControl { Experiment = data };
            analysis.Measure(new Size(600, 400));
            analysis.Arrange(new Rect(0, 0, 600, 400));
            var processing = new ProcessingGraphControl { Experiment = data };
            var hover = typeof(ProcessingGraphControl).GetMethod("BuildHoverLines", BindingFlags.Instance | BindingFlags.NonPublic)!;
            string[] expectedAnalysis = Array.Empty<string>(), expectedProcessing = Array.Empty<string>();
            InCulture("en-US", () =>
            {
                expectedAnalysis = analysis.HoverLinesForTesting(data.Injections[0], false).ToArray();
                expectedProcessing = ((IEnumerable<string>)hover.Invoke(processing, new object[] { data.DataPoints[0] })!).ToArray();
            });
            InCulture(cultureName, () =>
            {
                Assert.Contains("Molar Ratio: 0.125", analysis.HoverLinesForTesting(data.Injections[0], false));
                Assert.Equal(expectedAnalysis, analysis.HoverLinesForTesting(data.Injections[0], false));
                var enumerable = (IEnumerable<string>)hover.Invoke(processing, new object[] { data.DataPoints[0] })!;
                using var enumerator = enumerable.GetEnumerator();
                Assert.True(enumerator.MoveNext());
                Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
                Assert.Equal(expectedProcessing, ((IEnumerable<string>)hover.Invoke(processing, new object[] { data.DataPoints[0] })!).ToArray());
                Assert.Contains("Time: 0.5 s", expectedProcessing);
            });
        }
        finally { original.ApplyToSettings(); }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void ReportPlotAndSupportingFigurePixelsAreCultureIndependent(string cultureName)
    {
        var data = Experiment();
        var figureOptions = new PublicationFigureOptions { ShowThermogram = false, ShowFitParameters = false };
        var canvas = PublicationFigureCanvasBuilder.Build(new[] { data }, figureOptions,
            new PublicationFigureCanvasOptions { Columns = 1, Rows = 1 });
        var document = new AnalysisReportDocument { Title = "User text, 1,25", GeneratedAtUtc = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc) };
        var section = new AnalysisReportSection(AnalysisReportSectionKind.AdvancedAnalysis, "culture-plot", "User text, 1,25", AnalysisReportLayoutPolicy.None);
        section.Add(new AnalysisReportPlotBlock("Fractional ticks", "X", "Y", new[]
        {
            new AnalysisReportPlotSeries("User text, 1,25", AnalysisReportPlotSeriesKind.Points,
                new[] { new AnalysisReportPlotPoint(-1.234, -1.234), new AnalysisReportPlotPoint(1.234, 1.234) }),
        }));
        document.AddSection(section);
        var reportRenderer = new SkiaAnalysisReportRenderer();
        var reportPlan = reportRenderer.CreatePlan(document);
        var canvasRenderer = new SkiaFigureCanvasRenderer();
        byte[] expectedReport = Array.Empty<byte>(), expectedCanvas = Array.Empty<byte>();
        InCulture("en-US", () =>
        {
            using var reportBitmap = reportRenderer.RenderPageBitmap(document, reportPlan, 0, 700);
            using var canvasBitmap = canvasRenderer.RenderBitmap(canvasRenderer.CreatePlan(canvas), 700);
            expectedReport = reportBitmap.Bytes;
            expectedCanvas = canvasBitmap.Bytes;
        });
        InCulture(cultureName, () =>
        {
            using var reportBitmap = reportRenderer.RenderPageBitmap(document, reportPlan, 0, 700);
            using var canvasBitmap = canvasRenderer.RenderBitmap(canvasRenderer.CreatePlan(canvas), 700);
            Assert.Equal(expectedReport, reportBitmap.Bytes);
            Assert.Equal(expectedCanvas, canvasBitmap.Bytes);
            using var reportPdf = new MemoryStream();
            reportRenderer.WritePdf(document, reportPlan, reportPdf);
            using var canvasPdf = new MemoryStream();
            canvasRenderer.WritePdf(canvasRenderer.CreatePlan(canvas), canvasPdf);
            Assert.StartsWith("%PDF", Encoding.Latin1.GetString(reportPdf.ToArray()));
            Assert.StartsWith("%PDF", Encoding.Latin1.GetString(canvasPdf.ToArray()));
            Assert.Equal("User text, 1,25", document.Title);
        });
    }

    [AvaloniaTheory]
    [InlineData("en-US")]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void WorkspaceParameterBoxDrawsPeriodDecimals(string cultureName)
    {
        var original = PreferencesState.FromSettings();
        try
        {
            PreferencesState.Defaults().ApplyToSettings();
            AppSettings.NumberPrecision = NumberPrecision.AllDecimals;
            var data = Experiment();
            var model = new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, .8199);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            data.Model = model;
            var graph = new IntegratedHeatsGraphControl { Experiment = data };
            graph.Measure(new Size(800, 600));
            graph.Arrange(new Rect(0, 0, 800, 600));
            InCulture(cultureName, () =>
            {
                var group = new DrawingGroup();
                using (var drawing = group.Open()) graph.Render(drawing);
                Assert.Contains("N = 0.8199", string.Concat(RenderedText(group)));
            });
        }
        finally { original.ApplyToSettings(); }
    }

    static IEnumerable<string> RenderedText(global::Avalonia.Media.Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyphs) yield return glyphs.GlyphRun!.Characters.ToString();
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var text in RenderedText(child)) yield return text;
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void FinalFigurePdfMetadataKeepsPeriodsAndIntervalSeparator(string cultureName)
    {
        var original = PreferencesState.FromSettings();
        try
        {
            PreferencesState.Defaults().ApplyToSettings();
            InCulture(cultureName, () =>
            {
                var data = Experiment();
                var model = new OneSetOfSites(data);
                model.InitializeParameters(data);
                model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, .8199);
                var solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
                solution.Convergence.SetUnweightedRmsd(.8199);
                solution.Parameters[ParameterType.Nvalue1] = new FloatWithError(.8199, .002, .8156, .8236);
                model.Solution = solution;
                data.Model = model;
                var figure = PublicationFigureBuilder.Build(new PublicationFigureSource(data, solution),
                    new PublicationFigureOptions { ShowThermogram = false,
                        TextUncertaintyStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval });
                using var stream = new MemoryStream();
                new SkiaFigureRenderer().WritePdf(figure, stream);
                var pdf = Encoding.Latin1.GetString(stream.ToArray());
                var match = Regex.Match(pdf, @"/Keywords\s*<([0-9a-fA-F]+)>");
                Assert.True(match.Success, "Expected the PDF's Unicode Keywords string.");
                var keywords = Encoding.BigEndianUnicode.GetString(Convert.FromHexString(match.Groups[1].Value)).TrimStart('\uFEFF');
                Assert.Contains("N = 0.820 ± 0.002 [0.816.. 0.824]", keywords);
                Assert.Contains("Loss: 0.82", keywords);
                Assert.DoesNotContain("Loss: 0..82", keywords);
                Assert.Equal(.8199, solution.Parameters[ParameterType.Nvalue1].Value);
                Assert.Equal(.8156, solution.Parameters[ParameterType.Nvalue1].Lower);
                Assert.Equal(.8236, solution.Parameters[ParameterType.Nvalue1].Upper);
            });
        }
        finally { original.ApplyToSettings(); }
    }

    static ExperimentData Experiment()
    {
        var data = new ExperimentData("user text, 1,25.itc")
        {
            CellConcentration = new FloatWithError(20.25e-6), SyringeConcentration = new FloatWithError(200.25e-6),
            CellVolume = 200e-6, MeasuredTemperature = 25.5, TargetTemperature = 25.5,
            DataPoints = new List<DataPoint> { new(.5f, 1.25e-6f, 25.5f), new(1.5f, 2.5e-6f, 25.5f) },
        };
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2.5e-6, 1e-9, include: true)
            {
                Ratio = .125 * (index + 1), ActualCellConcentration = 20e-6, ActualTitrantConcentration = 2.5e-6 * (index + 1),
            };
            injection.SetPeakArea(new FloatWithError(-12.345e-6 + index * 1e-6));
            data.Injections.Add(injection);
        }
        return data;
    }

    static void InCulture(string name, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            action();
            Assert.Equal(name, CultureInfo.CurrentCulture.Name);
            Assert.Equal(name, CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
