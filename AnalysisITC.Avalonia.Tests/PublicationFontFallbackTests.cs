using System;
using System.Collections.Generic;
using System.Linq;

using SkiaSharp;

using Xunit;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class PublicationFontFallbackTests
{
    // U+037F (Greek capital yot) and U+052F are in the bundled Inter faces but not in Liberation Sans;
    // U+674E (李) is in neither.
    const string Yot = "Ϳ";
    const string CyrillicEl = "ԯ";
    const string Li = "李";
    const string InterLight = "Assets/Fonts/Inter/Inter-Light.ttf";

    public PublicationFontFallbackTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void CharactersMissingFromThePublicationFaceUseAMatchedInstalledFace()
    {
        using var source = new InterFallbackSource();
        using var set = Resolve(source);

        var runs = set.TextRuns("A" + Yot + "B", bold: true, italic: true);

        Assert.Equal(new[] { "A", Yot, "B" }, runs.Select(run => run.Text));
        Assert.Same(set.EmphasisItalic, runs[0].Typeface);
        Assert.StartsWith("Inter", runs[1].Typeface.FamilyName);
        Assert.NotEqual(0, runs[1].Typeface.GetGlyph(0x037F));
        Assert.Same(set.EmphasisItalic, runs[2].Typeface);
        Assert.Equal(
            ("Liberation Sans", SKFontStyleWeight.Bold, SKFontStyleSlant.Italic, 0x037F),
            Assert.Single(source.Requests));
    }

    [Fact]
    public void CoveredTextKeepsThePublicationFaceAndItsMeasurement()
    {
        using var source = new InterFallbackSource();
        using var set = Resolve(source);
        const string text = "ΔG° = −RT ln K (µM), Zoë";
        using var paint = new SKPaint();
        using var font = set.CreateFont(10);

        var run = Assert.Single(set.TextRuns(text, bold: false, italic: false));

        Assert.Same(set.Regular, run.Typeface);
        Assert.Equal(font.MeasureText(text, paint), set.MeasureText(text, 10, paint));
        Assert.Empty(source.Requests);
    }

    [Fact]
    public void MeasurementUsesTheWidthOfSubstitutedGlyphs()
    {
        using var source = new InterFallbackSource();
        using var set = Resolve(source);
        using var paint = new SKPaint();
        using var liberation = set.CreateFont(10);
        using var interData = SKData.Create(global::AnalysisITC.Avalonia.AppAssetLoader.Open(InterLight))!;
        using var interFace = SKTypeface.FromData(interData)!;
        using var inter = new SKFont(interFace, 10);

        var expected = liberation.MeasureText("A", paint) + inter.MeasureText(Yot, paint) + liberation.MeasureText("B", paint);

        Assert.Equal(expected, set.MeasureText("A" + Yot + "B", 10, paint), 4);
    }

    [Fact]
    public void FallbackFacesAreReusedAndUncoveredCharactersAreQueriedOnce()
    {
        using var source = new InterFallbackSource();
        using var set = Resolve(source);

        var covered = set.TextRuns(Yot + CyrillicEl + Yot, bold: false, italic: false);
        var uncovered = set.TextRuns(Li + Li, bold: false, italic: false);
        set.TextRuns(Li, bold: false, italic: false);

        Assert.Single(covered);
        var run = Assert.Single(uncovered);
        Assert.Same(set.Regular, run.Typeface);
        Assert.Equal(new[] { 0x037F, 0x674E }, source.Requests.Select(request => request.CodePoint));
    }

    [Fact]
    public void SurrogatePairsAndCombiningMarksAreNotSplitBetweenRuns()
    {
        using var source = new InterFallbackSource();
        using var set = Resolve(source);
        const string text = "x\U0001D400" + Yot + "́y";

        var runs = set.TextRuns(text, bold: false, italic: false);

        Assert.Equal(text, string.Concat(runs.Select(run => run.Text)));
        Assert.Contains(runs, run => run.Text == Yot + "́");
        Assert.All(runs, run =>
        {
            Assert.False(char.IsLowSurrogate(run.Text[0]));
            Assert.False(char.IsHighSurrogate(run.Text[^1]));
        });
        Assert.Contains(source.Requests, request => request.CodePoint == 0x1D400);
        Assert.DoesNotContain(source.Requests, request => request.CodePoint is >= 0xD800 and <= 0xDFFF);
    }

    [Fact]
    public void InstalledFontsReplaceTheMissingGlyphBoxInRenderedText()
    {
        using var installed = SKFontManager.Default.MatchCharacter(0x674E);
        if (installed == null) return; // No installed font covers the character on this machine.
        var set = SkiaPublicationFontResolver.Shared.Resolve(PublicationFont.LiberationSans);
        using var primary = set.CreateFont(24);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        using var substituted = Render(canvas => set.DrawText(canvas, Li, 4, 32, 24, paint));
        using var box = Render(canvas => canvas.DrawText(Li, 4, 32, SKTextAlign.Left, primary, paint));

        Assert.NotEqual(0, set.TextRuns(Li, bold: false, italic: false).Single().Typeface.GetGlyph(0x674E));
        Assert.NotEqual(Pixels(box), Pixels(substituted));
    }

    static SkiaPublicationFontSet Resolve(ISkiaSystemFontSource source) =>
        new SkiaPublicationFontResolver(
                () => PublicationFontPlatform.Linux,
                source,
                global::AnalysisITC.Avalonia.AppAssetLoader.Open)
            .Resolve(PublicationFont.LiberationSans);

    static SKBitmap Render(Action<SKCanvas> draw)
    {
        var bitmap = new SKBitmap(48, 48);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        draw(canvas);
        canvas.Flush();
        return bitmap;
    }

    static byte[] Pixels(SKBitmap bitmap) => bitmap.Bytes;

    /// <summary>Answers every fallback request with the bundled Inter Light face.</summary>
    sealed class InterFallbackSource : ISkiaSystemFontSource, IDisposable
    {
        readonly List<SKData> data = new();

        public List<(string Family, SKFontStyleWeight Weight, SKFontStyleSlant Slant, int CodePoint)> Requests { get; } = new();

        public bool HasFamily(string familyName) => false;

        public SKTypeface? MatchFamily(string familyName, SKFontStyleWeight weight, SKFontStyleSlant slant) => null;

        public SKTypeface? MatchCharacter(
            string familyName,
            SKFontStyleWeight weight,
            SKFontStyleSlant slant,
            int codePoint)
        {
            Requests.Add((familyName, weight, slant, codePoint));
            using var stream = global::AnalysisITC.Avalonia.AppAssetLoader.Open(InterLight);
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
