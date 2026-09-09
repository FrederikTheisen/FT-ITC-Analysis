using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;

using Xunit;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Controls;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisReportRenderingTests
{
    public AnalysisReportRenderingTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void MarkdownTablesRenderWithContinuationAndAllRows()
    {
        var markdown = "## Overall interpretation\n\nA compact comparison of **Results 1 and 2**.\n\n" +
            "| Experiment | Kd (µM) | Assessment |\n| :--- | ---: | :---: |\n" +
            string.Join("\n", Enumerable.Range(1, 70).Select(i => $"| **Experiment {i}A** | 4.1 | Consistent *affinity*; inspect the local baseline if the signal changes abruptly. |"));
        var document = AnalysisReportBuilder.BuildInterpretationPreview(markdown);
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);
        Assert.True(plan.Pages.Count > 1);
        var fragments = plan.Pages.SelectMany(p => p.Fragments).Where(f => f.Block is AnalysisReportTableBlock).ToList();
        Assert.Equal(70, fragments.Sum(f => f.ItemCount));
        for (var page = 0; page < plan.Pages.Count; page++)
        {
            using var bitmap = renderer.RenderPageBitmap(document, plan, page, 1000);
            Assert.True(bitmap.Width > 0);
            var path = Environment.GetEnvironmentVariable("FTITC_TABLE_PREVIEW_PATH");
            if (page == 0 && !string.IsNullOrEmpty(path))
            {
                using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                using var output = File.Create(path);
                data.SaveTo(output);
            }
        }
    }

    [Fact]
    public void RendererCreatesMultipageVectorA4PdfAndPreviewBitmap()
    {
        var document = CreateDocument();
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);

        Assert.Equal("Exported 3 Sep 2026 UTC", document.ExportDateText);
        Assert.Equal("ANALYSIS VALID", document.StatusBadgeText);
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

    [Fact]
    public void ReportBuilderExposesMeaningfulControlsAndCalmInitialState()
    {
        var window = new AnalysisReportWindow();
        var controls = window.GetLogicalDescendants().OfType<Control>().ToList();

        foreach (var name in new[]
        {
            "Select report contents", "Report subtitle", "Report title", "Energy units",
            "Temperature units", "Uncertainties", "Update report preview",
            "Export analysis report as PDF", "Report status", "Selected report contents details",
            "Report workspace view", "Interpretation workspace", "Report preview workspace",
            "Report preview pages", "Report preview zoom",
            "Report interpretation editor", "Interpretation status",
            "Edit report interpretation", "Generate interpretation with AI",
            "Include injection tables", "Condense repeated experiments"
        })
            Assert.Contains(controls, control => AutomationProperties.GetName(control) == name);

        var status = Assert.Single(controls, control => AutomationProperties.GetName(control) == "Report status");
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        Assert.False(status.IsVisible);
        var export = Assert.Single(controls.OfType<Button>(), control =>
            AutomationProperties.GetName(control) == "Export analysis report as PDF");
        Assert.Equal("Export...", export.Content);
        Assert.Equal(86, export.MinWidth);
        var energy = Assert.Single(controls.OfType<ComboBox>(), control =>
            AutomationProperties.GetName(control) == "Energy units");
        Assert.Equal(new[] { "Joule", "Calories" }, energy.Items.Cast<object>().Select(item => item.ToString()));
        var resultSelector = Assert.Single(controls.OfType<Button>(), control =>
            AutomationProperties.GetName(control) == "Select report contents");
        Assert.Equal(42, resultSelector.MinHeight);
        Assert.Equal(HorizontalAlignment.Stretch, resultSelector.HorizontalAlignment);
        var resultDetails = Assert.Single(controls.OfType<TextBlock>(), control =>
            AutomationProperties.GetName(control) == "Selected report contents details");
        Assert.Equal(11, resultDetails.FontSize);
        Assert.Equal("No result selected.", resultDetails.Text);
        var uncertainty = Assert.Single(controls.OfType<ComboBox>(), control =>
            AutomationProperties.GetName(control) == "Uncertainties");
        Assert.Contains("95% CI", uncertainty.Items.Cast<object>().Select(item => item.ToString()));
        Assert.Contains("SD + 95% CI", uncertainty.Items.Cast<object>().Select(item => item.ToString()));
        Assert.True(Assert.Single(controls.OfType<CheckBox>(), control =>
            AutomationProperties.GetName(control) == "Include injection tables").IsChecked);
        var condenseRepeated = Assert.Single(controls.OfType<CheckBox>(), control =>
            AutomationProperties.GetName(control) == "Condense repeated experiments");
        Assert.True(condenseRepeated.IsChecked);
        Assert.False(condenseRepeated.IsEnabled);
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text =>
            text.Text?.Contains("No preview yet", StringComparison.Ordinal) == true);
        var selector = Assert.Single(controls.OfType<SegmentedSelector>(), control =>
            AutomationProperties.GetName(control) == "Report workspace view");
        Assert.Equal(0, selector.SelectedIndex);
        Assert.Equal(new[] { "Interpretation", "Preview" }, selector.Options);
        var interpretationHost = Assert.Single(controls, control =>
            AutomationProperties.GetName(control) == "Interpretation workspace");
        var previewHost = Assert.Single(controls, control =>
            AutomationProperties.GetName(control) == "Report preview workspace");
        Assert.True(interpretationHost.IsVisible);
        Assert.False(previewHost.IsVisible);
        var previewPages = Assert.Single(controls.OfType<ItemsControl>(), control =>
            AutomationProperties.GetName(control) == "Report preview pages");
        Assert.IsNotType<ListBox>(previewPages);
        Assert.False(previewPages.Focusable);
        var previewZoom = Assert.Single(controls.OfType<ComboBox>(), control =>
            AutomationProperties.GetName(control) == "Report preview zoom");
        Assert.Equal(2, previewZoom.SelectedIndex);
        Assert.Equal(new[] { "50%", "75%", "100%", "125%", "150%", "200%" },
            previewZoom.Items.Cast<object>().Select(item => item.ToString()));
        var interpretation = Assert.Single(controls.OfType<TextBox>(), control =>
            AutomationProperties.GetName(control) == "Report interpretation editor");
        Assert.True(interpretation.AcceptsReturn);
        Assert.Equal(global::Avalonia.Media.TextWrapping.Wrap, interpretation.TextWrapping);
        Assert.DoesNotContain(controls, control =>
            AutomationProperties.GetName(control) == "Approved report interpretation");
    }

    [Fact]
    public void SupportingFigureExposesPanelTitleToggle()
    {
        var window = new SupportingFigureCanvasWindow(new PublicationFigureOptions(), null);
        var toggle = Assert.Single(window.GetLogicalDescendants().OfType<CheckBox>(),
            control => AutomationProperties.GetName(control) == "Show panel titles");

        Assert.False(toggle.IsChecked);
        Assert.Contains("experiment name", AutomationProperties.GetHelpText(toggle), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretationDialogExposesMainQuestionAndCombinedAdditionalContext()
    {
        var report = new AnalysisReport();
        report.UpdateStudyContext(new AnalysisITC.Core.Interpretation.AnalysisStudyContext
        {
            ScientificQuestion = "Saved question",
            SystemDescription = "Saved system",
            AdditionalNotes = "Saved caveats",
        });
        var dialog = new AnalysisInterpretationDialog(report, null!, new HttpClient(), () => { });
        var controls = dialog.GetLogicalDescendants().OfType<Control>().ToList();

        foreach (var name in new[]
        {
            "Main question", "Additional context",
            "Generated interpretation draft", "Use generated interpretation in report"
        })
            Assert.Contains(controls, control => AutomationProperties.GetName(control) == name);
        Assert.Equal("Saved question", Assert.Single(controls.OfType<TextBox>(), control =>
            AutomationProperties.GetName(control) == "Main question").Text);
        Assert.Equal("Saved system\n\nSaved caveats", Assert.Single(controls.OfType<TextBox>(), control =>
            AutomationProperties.GetName(control) == "Additional context").Text);
        foreach (var editor in controls.OfType<TextBox>().Where(control =>
            AutomationProperties.GetName(control) is "Main question" or "Additional context"))
        {
            Assert.Equal(global::Avalonia.Media.TextWrapping.Wrap, editor.TextWrapping);
            Assert.Equal(ScrollBarVisibility.Disabled,
                editor.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        }
        Assert.True(Assert.Single(controls.OfType<CheckBox>(), control =>
            AutomationProperties.GetName(control) == "Include compressed thermograms").IsChecked);
        Assert.Equal(3, controls.OfType<TextBox>().Count());
        Assert.False(Assert.Single(controls.OfType<TextBox>(), control =>
            AutomationProperties.GetName(control) == "Generated interpretation draft").IsVisible);
    }

    [Fact]
    public void InterpretationPreviewMeasuresFullTextWithoutShrinkingToTwoPages()
    {
        var renderer = new SkiaAnalysisReportRenderer();
        var concise = AnalysisReportBuilder.BuildInterpretationPreview("## Overall interpretation\n\nThe supplied fits support a consistent affinity estimate.");
        Assert.Single(renderer.CreatePlan(concise).Pages);
        // Many short paragraphs exercise the actual layout independently of the word ceiling.
        var spaced = AnalysisReportBuilder.BuildInterpretationPreview("## Overall interpretation\n\n" +
            string.Join("\n\n", Enumerable.Repeat("A distinct observation remains uncertain.", 150)));
        Assert.True(renderer.CreatePlan(spaced).Pages.Count > 2);
    }

    static AnalysisReportDocument CreateDocument()
    {
        var document = new AnalysisReportDocument
        {
            Title = "Vector report test",
            ResultName = "Result α",
            Creator = "FT-ITC Analysis",
            ApplicationVersion = "1.5.0",
            GeneratedAtUtc = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc),
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
