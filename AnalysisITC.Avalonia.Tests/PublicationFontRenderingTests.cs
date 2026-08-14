using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SkiaSharp;

using Xunit;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Printing;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class PublicationFontRenderingTests
{
    const string ScientificGlyphs = "ABC xyz 0123 αβγ Δδ µμ ° ± ₀₁₂₃ ¹²³⁴";

    public PublicationFontRenderingTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Theory]
    [InlineData(PublicationFont.Inter, "Inter", 300, 500)]
    [InlineData(PublicationFont.LiberationSans, "Liberation Sans", 400, 700)]
    public void BundledSetsContainFourRealValidatedScientificFaces(
        PublicationFont selection,
        string family,
        int regularWeight,
        int emphasisWeight)
    {
        var set = SkiaPublicationFontResolver.Shared.Resolve(selection);

        Assert.Equal(family, set.ResolvedFamily);
        Assert.False(set.IsFallback);
        AssertFace(set.Regular, family, regularWeight, SKFontStyleSlant.Upright);
        AssertFace(set.Italic, family, regularWeight, SKFontStyleSlant.Italic);
        AssertFace(set.Emphasis, family, emphasisWeight, SKFontStyleSlant.Upright);
        AssertFace(set.EmphasisItalic, family, emphasisWeight, SKFontStyleSlant.Italic);
        Assert.NotSame(set.Regular, set.Italic);
        Assert.NotSame(set.Regular, set.Emphasis);
        Assert.NotSame(set.Emphasis, set.EmphasisItalic);

        foreach (var style in new[]
                 {
                     (Bold: false, Italic: false),
                     (Bold: false, Italic: true),
                     (Bold: true, Italic: false),
                     (Bold: true, Italic: true)
                 })
        {
            using var font = set.CreateFont(12, style.Bold, style.Italic);
            Assert.True(font.ContainsGlyphs(ScientificGlyphs));
            Assert.False(font.Embolden);
            Assert.Equal(0, font.SkewX);
        }
    }

    [Theory]
    [InlineData("Assets/Fonts/Inter/Inter-Light.ttf", "164414f0aacbe98a7e64addc43f7b3bfd2e32f7b90e101feeab227f14c371bda")]
    [InlineData("Assets/Fonts/Inter/Inter-LightItalic.ttf", "c3f9efa776957eefaeac8a2991a990fd1bba6cb928dbaeab7abd0655f3a7693c")]
    [InlineData("Assets/Fonts/Inter/Inter-Medium.ttf", "97ad806f526e41546d46365bb3a393145f75b7b1568913db74549ad8b8dba872")]
    [InlineData("Assets/Fonts/Inter/Inter-MediumItalic.ttf", "51c2c8d7c36f7c26e6e2678b5c3069b329bde9a081154553b0f5bc2d4fc14075")]
    [InlineData("Assets/Fonts/LiberationSans/LiberationSans-Regular.ttf", "76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8")]
    [InlineData("Assets/Fonts/LiberationSans/LiberationSans-Italic.ttf", "e5bae5c4cde31f22142753855f4f8fb86da6ff39955ed3c0a11248b0d16948b0")]
    [InlineData("Assets/Fonts/LiberationSans/LiberationSans-Bold.ttf", "788abee4c806d660e8aee46689dd8540cd4bb98da03dcc9d171ce3efd99a9173")]
    [InlineData("Assets/Fonts/LiberationSans/LiberationSans-BoldItalic.ttf", "698da70fc191cc5f33ad4d6d3fe830fe4624b898ea2e3169955928b7c491f1ee")]
    public void BundledAssetMatchesRecordedHash(string path, string expectedHash)
    {
        using var stream = global::AnalysisITC.Avalonia.AppAssetLoader.Open(path);

        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void ResolverPublishesOneCompleteSetAcrossThreads()
    {
        var openCount = 0;
        var resolver = new SkiaPublicationFontResolver(
            () => PublicationFontPlatform.Linux,
            new MissingSystemFontSource(),
            path =>
            {
                Interlocked.Increment(ref openCount);
                return global::AnalysisITC.Avalonia.AppAssetLoader.Open(path);
            });

        var resolved = Enumerable.Range(0, 64)
            .AsParallel()
            .Select(_ => resolver.Resolve(PublicationFont.Native))
            .ToArray();

        Assert.All(resolved, item => Assert.Same(resolved[0], item));
        Assert.Equal(4, openCount);
    }

    [Fact]
    public void NativePlatformDefinitionsAreExactAndComplete()
    {
        AssertNativeDefinition(
            PublicationFontPlatform.MacOS,
            "Helvetica Neue",
            isBundled: false,
            SKFontStyleWeight.Light,
            SKFontStyleWeight.Medium);
        AssertNativeDefinition(
            PublicationFontPlatform.Windows,
            "Arial",
            isBundled: false,
            SKFontStyleWeight.Normal,
            SKFontStyleWeight.Bold);
        AssertNativeDefinition(
            PublicationFontPlatform.Linux,
            "Liberation Sans",
            isBundled: true,
            SKFontStyleWeight.Normal,
            SKFontStyleWeight.Bold);
        Assert.Null(SkiaPublicationFontResolver.GetNativeDefinition(PublicationFontPlatform.Other));
    }

    [Fact]
    public void MissingWindowsFamilyFallsBackOnceWithDiagnosticReason()
    {
        var source = new MissingSystemFontSource();
        var diagnostics = new List<string>();
        var resolver = new SkiaPublicationFontResolver(
            () => PublicationFontPlatform.Windows,
            source,
            global::AnalysisITC.Avalonia.AppAssetLoader.Open,
            diagnostics.Add);

        var first = resolver.Resolve(PublicationFont.Native);
        var second = resolver.Resolve(PublicationFont.Native);

        Assert.Same(first, second);
        Assert.True(first.IsFallback);
        Assert.Equal("Liberation Sans", first.ResolvedFamily);
        Assert.Equal("Liberation Sans (native family unavailable)", first.ResolutionDescription);
        Assert.Contains("Arial", first.FallbackReason);
        Assert.Equal(new[] { "Arial" }, source.FamilyChecks);
        Assert.Empty(source.MatchRequests);
        Assert.Single(diagnostics);
        Assert.Contains("Arial", diagnostics[0]);
    }

    [Fact]
    public void SubstitutedWindowsFamilyIsRejectedInsteadOfAcceptedFromSkia()
    {
        using var source = new BundledSystemFontSource("Arial");
        var resolver = new SkiaPublicationFontResolver(
            () => PublicationFontPlatform.Windows,
            source,
            global::AnalysisITC.Avalonia.AppAssetLoader.Open);

        var set = resolver.Resolve(PublicationFont.Native);

        Assert.True(set.IsFallback);
        Assert.Contains("Expected Arial", set.FallbackReason);
        Assert.Single(source.MatchRequests);
        Assert.Equal(SKFontStyleWeight.Normal, source.MatchRequests[0].Weight);
        Assert.Equal(SKFontStyleSlant.Upright, source.MatchRequests[0].Slant);
    }

    [Fact]
    public void MissingSingleNativeStyleFallsBackTheWholeFamily()
    {
        using var source = new BundledSystemFontSource("Liberation Sans", missingCall: 4);
        var diagnostics = new List<string>();
        var resolver = new SkiaPublicationFontResolver(
            () => PublicationFontPlatform.Windows,
            source,
            global::AnalysisITC.Avalonia.AppAssetLoader.Open,
            diagnostics.Add,
            _ => new NativePublicationFontDefinition(
                "Liberation Sans",
                isBundled: false,
                SKFontStyleWeight.Normal,
                SKFontStyleWeight.Bold));

        var set = resolver.Resolve(PublicationFont.Native);

        Assert.True(set.IsFallback);
        Assert.Equal(4, source.MatchRequests.Count);
        Assert.Equal(SKFontStyleSlant.Italic, source.MatchRequests[^1].Slant);
        Assert.Equal(SKFontStyleWeight.Bold, source.MatchRequests[^1].Weight);
        Assert.Contains("Bold Italic", set.FallbackReason);
        Assert.Single(diagnostics);
        AssertFace(set.Regular, "Liberation Sans", 400, SKFontStyleSlant.Upright);
        AssertFace(set.Italic, "Liberation Sans", 400, SKFontStyleSlant.Italic);
        AssertFace(set.Emphasis, "Liberation Sans", 700, SKFontStyleSlant.Upright);
        AssertFace(set.EmphasisItalic, "Liberation Sans", 700, SKFontStyleSlant.Italic);
    }

    [Fact]
    public void RichTextMetricsIncludeSuperscriptAndSubscriptOffsets()
    {
        var fonts = SkiaPublicationFontResolver.Shared.Resolve(PublicationFont.Inter);
        using var bitmap = new SKBitmap(200, 100);
        using var canvas = new SKCanvas(bitmap);
        var drawing = new SkiaDrawingContext(canvas, fonts);

        var plain = drawing.MeasureRichText("Kd = 1.23", 12);
        var scripted = drawing.MeasureRichText("*K*{d} = 1.23^2^ µM", 12);

        Assert.True(scripted.Height > plain.Height);
        drawing.DrawRichText("*K*{d} = 1.23^2^ µM", new SKPoint(0, 0), 12, SKColors.Black);
    }

    [Fact]
    public void AnnotationLeadingUsesStableBaselinesWhileContainingScientificText()
    {
        const float fontSize = 12;
        var fonts = SkiaPublicationFontResolver.Shared.Resolve(PublicationFont.Inter);
        using var bitmap = new SKBitmap(200, 100);
        using var canvas = new SKCanvas(bitmap);
        var drawing = new SkiaDrawingContext(canvas, fonts);
        var measurements = new[]
        {
            drawing.MeasureRichTextMetrics("**Model A** | RMSD = 0.12", fontSize),
            drawing.MeasureRichTextMetrics("*K*{d,1} = 1.2 ± 0.1 µM", fontSize),
            drawing.MeasureRichTextMetrics("Δ*H*{1} = −12.3 kJ mol^−1^", fontSize)
        };

        var layout = SkiaFigureRenderer.CalculateAnnotationTextLayout(measurements, fontSize);

        Assert.Equal(fontSize * 1.15f, layout.LineAdvance, precision: 3);
        for (var index = 0; index < measurements.Length; index++)
        {
            Assert.True(measurements[index].Top + index * layout.LineAdvance >= layout.Top);
            Assert.True(measurements[index].Bottom + index * layout.LineAdvance <= layout.Bottom);
        }

        var metricStackHeight = measurements.Sum(measurement => measurement.Height)
            + (measurements.Length - 1) * fontSize * 0.15f;
        Assert.True(layout.Bottom - layout.Top < metricStackHeight);
    }

    [Theory]
    [InlineData(PublicationFont.Inter, "Inter")]
    [InlineData(PublicationFont.LiberationSans, "LiberationSans")]
    public void FinalFigureRendersAsBitmapAndVectorPdf(PublicationFont font, string pdfFamilyName)
    {
        var document = CreateFigure(font);
        var renderer = new SkiaFigureRenderer();

        using var bitmap = renderer.RenderBitmap(document, 1000);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);

        using var stream = new MemoryStream();
        renderer.WritePdf(document, stream);
        var pdf = stream.ToArray();
        var pdfText = Encoding.Latin1.GetString(pdf);

        Assert.True(pdf.Length > 1_000);
        Assert.StartsWith("%PDF", pdfText);
        Assert.Contains(pdfFamilyName, pdfText);
        Assert.Contains("/FontFile2", pdfText);
        Assert.DoesNotContain("/Subtype /Image", pdfText);
    }

    [Fact]
    public void SupportingCanvasInheritsCapturedFontForBitmapAndPdf()
    {
        var experiment = new ExperimentData("font-canvas-test.itc");
        var figureOptions = new PublicationFigureOptions
        {
            Font = PublicationFont.Inter,
            ShowThermogram = false,
            ShowResiduals = false,
            ShowFitParameters = false
        };
        var canvasDocument = PublicationFigureCanvasBuilder.Build(
            new[] { experiment },
            figureOptions,
            new PublicationFigureCanvasOptions { Columns = 1, Rows = 1 });
        var renderer = new SkiaFigureCanvasRenderer();
        var plan = renderer.CreatePlan(canvasDocument);

        Assert.True(plan.IsValid, plan.ValidationError);
        Assert.Equal("Inter", plan.Fonts.ResolvedFamily);
        Assert.Equal(PublicationFont.Inter, Assert.Single(plan.Figures).Options.Font);
        Assert.Equal("A", Assert.Single(plan.Cells).Cell.PanelLabel);

        using var bitmap = renderer.RenderBitmap(plan, 1000);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);

        var path = Path.Combine(Path.GetTempPath(), $"ft-itc-font-{Guid.NewGuid():N}.pdf");
        try
        {
            renderer.WritePdf(plan, path);
            var pdf = File.ReadAllBytes(path);
            Assert.True(pdf.Length > 1_000);
            var pdfText = Encoding.Latin1.GetString(pdf);
            Assert.Contains("Inter", pdfText);
            Assert.Contains("/FontFile2", pdfText);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task PublicationPrintCaptureUsesSelectedFontAndVectorPdf()
    {
        var target = GraphPrintTarget.FromPublicationFigure(
            "Publication font print test",
            () => CreateFigure(PublicationFont.Inter),
            new SkiaFigureRenderer());

        using var payload = await target.CaptureAsync();
        var pdfText = Encoding.Latin1.GetString(payload.Pdf);

        Assert.True(payload.PreservePdf);
        Assert.True(payload.PdfPageSize.IsValid);
        Assert.True(payload.Bitmap.Width >= 1_200);
        Assert.Contains("Inter", pdfText);
        Assert.Contains("/FontFile2", pdfText);
        Assert.DoesNotContain("/Subtype /Image", pdfText);
    }

    [Fact]
    public void PublishedOutputContainsBothFontLicensesAndProvenance()
    {
        var licenseDirectory = Path.Combine(AppContext.BaseDirectory, "Licenses", "Fonts");

        Assert.Contains("SIL OPEN FONT LICENSE", File.ReadAllText(Path.Combine(licenseDirectory, "Inter-OFL.txt")));
        Assert.Contains("SIL OPEN FONT LICENSE", File.ReadAllText(Path.Combine(licenseDirectory, "LiberationSans-OFL.txt")));
        var provenance = File.ReadAllText(Path.Combine(licenseDirectory, "PROVENANCE.md"));
        Assert.Contains("Inter 4.1", provenance);
        Assert.Contains("Liberation Sans 2.1.5", provenance);
        Assert.Contains("SHA-256", provenance);
    }

    static PublicationFigureDocument CreateFigure(PublicationFont font)
    {
        var options = new PublicationFigureOptions
        {
            Font = font,
            FontSize = 12,
            ShowThermogram = false,
            ShowResiduals = false,
            ShowAxisTitles = true
        };
        var annotation = new PublicationAnnotationBox
        {
            Placement = PublicationInfoBoxPlacement.Upper
        };
        annotation.Lines.Add("**Model A** | RMSD = 0.12");
        annotation.Lines.Add("*K*{d} = 1.2 ± 0.1 µM");
        annotation.Lines.Add("Δ*H* = −12.3 kJ mol^−1^");

        var panel = new PublicationFigurePanel
        {
            Kind = PublicationPanelKind.Fit,
            XAxis = new PublicationAxis("Molar ratio α", PublicationAxisPlacement.Bottom, 0, 2, 6),
            YAxis = new PublicationAxis("ΔH (µJ mol^−1^)", PublicationAxisPlacement.Left, -15, 5, 6),
            DrawZeroLine = true
        };
        panel.Series.Add(new PublicationSeries
        {
            Role = PublicationSeriesRole.Fit,
            Points = new List<PublicationPoint>
            {
                new(0, -1),
                new(0.5, -8),
                new(1, -12),
                new(2, -13)
            }
        });
        panel.Points.Add(new PublicationErrorPoint
        {
            X = 0.5,
            Y = -8,
            LowerY = -9,
            UpperY = -7
        });
        panel.AnnotationBoxes.Add(annotation);

        return new PublicationFigureDocument(options)
        {
            Title = "Publication font smoke test",
            PlotWidth = 280,
            PlotHeight = 360,
            FitPanel = panel
        };
    }

    static void AssertFace(
        SKTypeface face,
        string family,
        int weight,
        SKFontStyleSlant slant)
    {
        Assert.True(
            string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase)
            || face.FamilyName.StartsWith(family + " ", StringComparison.OrdinalIgnoreCase),
            $"Unexpected family {face.FamilyName}.");
        Assert.Equal(weight, face.FontWeight);
        Assert.Equal(slant, face.FontSlant);
    }

    static void AssertNativeDefinition(
        PublicationFontPlatform platform,
        string family,
        bool isBundled,
        SKFontStyleWeight regularWeight,
        SKFontStyleWeight emphasisWeight)
    {
        var definition = Assert.IsType<NativePublicationFontDefinition>(
            SkiaPublicationFontResolver.GetNativeDefinition(platform));
        Assert.Equal(family, definition.Family);
        Assert.Equal(isBundled, definition.IsBundled);
        Assert.Collection(
            definition.Faces,
            face => AssertStyle(face, regularWeight, SKFontStyleSlant.Upright),
            face => AssertStyle(face, regularWeight, SKFontStyleSlant.Italic),
            face => AssertStyle(face, emphasisWeight, SKFontStyleSlant.Upright),
            face => AssertStyle(face, emphasisWeight, SKFontStyleSlant.Italic));
    }

    static void AssertStyle(
        PublicationFontFaceDefinition face,
        SKFontStyleWeight weight,
        SKFontStyleSlant slant)
    {
        Assert.Equal(weight, face.Weight);
        Assert.Equal(slant, face.Slant);
    }

    sealed class MissingSystemFontSource : ISkiaSystemFontSource
    {
        public List<string> FamilyChecks { get; } = new();
        public List<(string Family, SKFontStyleWeight Weight, SKFontStyleSlant Slant)> MatchRequests { get; } = new();

        public bool HasFamily(string familyName)
        {
            FamilyChecks.Add(familyName);
            return false;
        }

        public SKTypeface? MatchFamily(
            string familyName,
            SKFontStyleWeight weight,
            SKFontStyleSlant slant)
        {
            MatchRequests.Add((familyName, weight, slant));
            return null;
        }
    }

    sealed class BundledSystemFontSource : ISkiaSystemFontSource, IDisposable
    {
        readonly string reportedFamily;
        readonly int missingCall;
        readonly List<SKData> data = new();

        public BundledSystemFontSource(string reportedFamily, int missingCall = 0)
        {
            this.reportedFamily = reportedFamily;
            this.missingCall = missingCall;
        }

        public List<(string Family, SKFontStyleWeight Weight, SKFontStyleSlant Slant)> MatchRequests { get; } = new();

        public bool HasFamily(string familyName) =>
            string.Equals(familyName, reportedFamily, StringComparison.OrdinalIgnoreCase);

        public SKTypeface? MatchFamily(
            string familyName,
            SKFontStyleWeight weight,
            SKFontStyleSlant slant)
        {
            MatchRequests.Add((familyName, weight, slant));
            if (MatchRequests.Count == missingCall) return null;

            var path = (weight, slant) switch
            {
                (SKFontStyleWeight.Bold, SKFontStyleSlant.Italic) =>
                    "Assets/Fonts/LiberationSans/LiberationSans-BoldItalic.ttf",
                (SKFontStyleWeight.Bold, _) =>
                    "Assets/Fonts/LiberationSans/LiberationSans-Bold.ttf",
                (_, SKFontStyleSlant.Italic) =>
                    "Assets/Fonts/LiberationSans/LiberationSans-Italic.ttf",
                _ => "Assets/Fonts/LiberationSans/LiberationSans-Regular.ttf"
            };
            using var stream = global::AnalysisITC.Avalonia.AppAssetLoader.Open(path);
            var item = SKData.Create(stream)!;
            data.Add(item);
            return SKTypeface.FromData(item);
        }

        public void Dispose()
        {
            foreach (var item in data) item.Dispose();
        }
    }
}
