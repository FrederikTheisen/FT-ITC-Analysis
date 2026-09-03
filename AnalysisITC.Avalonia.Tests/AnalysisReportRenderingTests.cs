using System;
using System.IO;
using System.Linq;
using System.Text;

using Xunit;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisReportRenderingTests
{
    public AnalysisReportRenderingTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void RendererCreatesMultipageVectorA4PdfAndPreviewBitmap()
    {
        var document = CreateDocument();
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);

        Assert.True(plan.Pages.Count >= 2);
        Assert.All(plan.Pages, page =>
        {
            Assert.Equal(21 * AnalysisReportLayoutEngine.PointsPerCentimeter, page.Width, 4);
            Assert.Equal(29.7 * AnalysisReportLayoutEngine.PointsPerCentimeter, page.Height, 4);
        });

        using var bitmap = renderer.RenderPageBitmap(document, plan, 0, 600);
        Assert.InRange((double)bitmap.Height / bitmap.Width, 1.413, 1.415);

        using var stream = new MemoryStream();
        renderer.WritePdf(document, plan, stream);
        var bytes = stream.ToArray();
        var text = Encoding.Latin1.GetString(bytes);
        Assert.True(bytes.Length > 2_000);
        Assert.StartsWith("%PDF", text);
        Assert.Contains("Vector report test", text);
        Assert.Contains("/FontFile2", text);
        Assert.DoesNotContain("/Subtype /Image", text);
        Assert.True(Count(text, "/Type /Page") >= plan.Pages.Count);
    }

    [Theory]
    [InlineData("A result", "A result")]
    [InlineData("bad/name", "bad-name")]
    [InlineData("...", "analysis")]
    public void SuggestedFilenameStemIsSanitized(string input, string expected) =>
        Assert.Equal(expected, AnalysisReportWindow.SanitizeFileName(input));

    static AnalysisReportDocument CreateDocument()
    {
        var document = new AnalysisReportDocument
        {
            Title = "Vector report test",
            ResultName = "Result α",
            Creator = "FT-ITC Analysis",
            ApplicationVersion = "1.5.0"
        };
        var cover = new AnalysisReportSection(AnalysisReportSectionKind.Cover, "cover", document.Title,
            AnalysisReportLayoutPolicy.KeepTogether | AnalysisReportLayoutPolicy.ShrinkToSinglePage);
        cover.Add(new AnalysisReportNoticeBlock("Status", "Valid with scientific warning: ΔH ± SD", AnalysisReportNoticeLevel.Warning));
        cover.Add(new AnalysisReportFigureBlock("Fit overview", "A", CreateFigure(), AnalysisReportLayoutPolicy.KeepTogether));
        document.AddSection(cover);

        var appendix = new AnalysisReportSection(AnalysisReportSectionKind.Appendix, "appendix", "Appendix",
            AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
        appendix.Add(new AnalysisReportPlotBlock("Temperature dependence", "Temperature (°C)", "ΔH (kJ mol⁻¹)", new[]
        {
            new AnalysisReportPlotSeries("Saved observations", AnalysisReportPlotSeriesKind.Points, new[]
            {
                new AnalysisReportPlotPoint(20, -25, -27, -23),
                new AnalysisReportPlotPoint(30, -22, -24, -20),
            }),
            new AnalysisReportPlotSeries("Fit", AnalysisReportPlotSeriesKind.Line, new[]
            {
                new AnalysisReportPlotPoint(20, -25), new AnalysisReportPlotPoint(30, -22),
            })
        }));
        appendix.Add(new AnalysisReportCorrelationMatrixBlock("Correlation matrix", new[] { "Kd", "ΔH" },
            new[,] { { 1.0, -.75 }, { -.75, 1.0 } }, new[] { "Numeric values support monochrome printing." }));
        appendix.Add(new AnalysisReportTableBlock("Provenance",
            new[] { new AnalysisReportTableColumn("name", "Experiment"), new AnalysisReportTableColumn("note", "Note") },
            Enumerable.Range(1, 70).Select(index => new AnalysisReportTableRow(new[] { "Experiment " + index, "Saved input and provenance" })),
            AnalysisReportLayoutPolicy.AllowContinuation));
        document.AddSection(appendix);
        return document;
    }

    static PublicationFigureDocument CreateFigure()
    {
        var options = new PublicationFigureOptions
        {
            ShowThermogram = false,
            ShowResiduals = true,
            ShowFitParameters = false,
            PlotWidthCentimeters = 12,
            PlotHeightCentimeters = 8,
        };
        var figure = new PublicationFigureDocument(options) { Title = "Fit" };
        figure.FitPanel = Panel(PublicationPanelKind.Fit, "Molar ratio", "Heat (kJ mol⁻¹)");
        figure.ResidualPanel = Panel(PublicationPanelKind.Residual, "Molar ratio", "Residual (µJ)");
        return figure;
    }

    static PublicationFigurePanel Panel(PublicationPanelKind kind, string x, string y)
    {
        var panel = new PublicationFigurePanel
        {
            Kind = kind,
            XAxis = new PublicationAxis(x, PublicationAxisPlacement.Bottom, 0, 2, 4),
            YAxis = new PublicationAxis(y, PublicationAxisPlacement.Left, -2, 2, 4),
            DrawZeroLine = true,
        };
        panel.Series.Add(new PublicationSeries
        {
            Role = kind == PublicationPanelKind.Fit ? PublicationSeriesRole.Fit : PublicationSeriesRole.Thermogram,
            Points = { new PublicationPoint(0, -1), new PublicationPoint(1, .5), new PublicationPoint(2, 1) }
        });
        return panel;
    }

    static int Count(string value, string token)
    {
        var count = 0; var offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0) { count++; offset += token.Length; }
        return count;
    }
}
