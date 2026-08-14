using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using SkiaSharp;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Avalonia.Drawing;

internal enum PublicationFontPlatform
{
    MacOS,
    Windows,
    Linux,
    Other
}

internal readonly struct PublicationFontFaceDefinition
{
    public PublicationFontFaceDefinition(SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        Weight = weight;
        Slant = slant;
    }

    public SKFontStyleWeight Weight { get; }
    public SKFontStyleSlant Slant { get; }
}

internal sealed class NativePublicationFontDefinition
{
    public NativePublicationFontDefinition(
        string family,
        bool isBundled,
        SKFontStyleWeight regularWeight,
        SKFontStyleWeight emphasisWeight)
    {
        Family = family;
        IsBundled = isBundled;
        Faces = new[]
        {
            new PublicationFontFaceDefinition(regularWeight, SKFontStyleSlant.Upright),
            new PublicationFontFaceDefinition(regularWeight, SKFontStyleSlant.Italic),
            new PublicationFontFaceDefinition(emphasisWeight, SKFontStyleSlant.Upright),
            new PublicationFontFaceDefinition(emphasisWeight, SKFontStyleSlant.Italic)
        };
    }

    public string Family { get; }
    public bool IsBundled { get; }
    public IReadOnlyList<PublicationFontFaceDefinition> Faces { get; }
}

internal interface ISkiaSystemFontSource
{
    bool HasFamily(string familyName);

    SKTypeface? MatchFamily(
        string familyName,
        SKFontStyleWeight weight,
        SKFontStyleSlant slant);
}

internal sealed class SkiaSystemFontSource : ISkiaSystemFontSource
{
    readonly SKFontManager manager;
    readonly HashSet<string> families;

    public SkiaSystemFontSource(SKFontManager manager)
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        families = new HashSet<string>(manager.FontFamilies, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasFamily(string familyName) => families.Contains(familyName);

    public SKTypeface? MatchFamily(
        string familyName,
        SKFontStyleWeight weight,
        SKFontStyleSlant slant)
    {
        if (!HasFamily(familyName)) return null;

        using var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
        return manager.MatchFamily(familyName, style);
    }
}

internal sealed class SkiaPublicationFontSet : IDisposable
{
    readonly IReadOnlyList<SKData> data;

    public SkiaPublicationFontSet(
        PublicationFont requestedFont,
        string resolvedFamily,
        SKTypeface regular,
        SKTypeface italic,
        SKTypeface emphasis,
        SKTypeface emphasisItalic,
        bool isFallback = false,
        string fallbackReason = "",
        IReadOnlyList<SKData>? data = null)
    {
        RequestedFont = requestedFont;
        ResolvedFamily = resolvedFamily ?? throw new ArgumentNullException(nameof(resolvedFamily));
        Regular = regular ?? throw new ArgumentNullException(nameof(regular));
        Italic = italic ?? throw new ArgumentNullException(nameof(italic));
        Emphasis = emphasis ?? throw new ArgumentNullException(nameof(emphasis));
        EmphasisItalic = emphasisItalic ?? throw new ArgumentNullException(nameof(emphasisItalic));
        IsFallback = isFallback;
        FallbackReason = fallbackReason ?? "";
        this.data = data ?? Array.Empty<SKData>();
    }

    public PublicationFont RequestedFont { get; }
    public string ResolvedFamily { get; }
    public bool IsFallback { get; }
    public string FallbackReason { get; }
    public string ResolutionDescription => IsFallback
        ? $"{ResolvedFamily} (native family unavailable)"
        : ResolvedFamily;

    internal SKTypeface Regular { get; }
    internal SKTypeface Italic { get; }
    internal SKTypeface Emphasis { get; }
    internal SKTypeface EmphasisItalic { get; }

    public SKFont CreateFont(float size, bool bold = false, bool italic = false)
    {
        var typeface = (bold, italic) switch
        {
            (true, true) => EmphasisItalic,
            (true, false) => Emphasis,
            (false, true) => Italic,
            _ => Regular
        };

        return new SKFont(typeface, size)
        {
            Embolden = false,
            SkewX = 0
        };
    }

    public void Dispose()
    {
        Regular.Dispose();
        Italic.Dispose();
        Emphasis.Dispose();
        EmphasisItalic.Dispose();
        foreach (var item in data) item.Dispose();
    }
}

internal sealed class SkiaPublicationFontResolver
{
    const string InterLight = "Assets/Fonts/Inter/Inter-Light.ttf";
    const string InterLightItalic = "Assets/Fonts/Inter/Inter-LightItalic.ttf";
    const string InterMedium = "Assets/Fonts/Inter/Inter-Medium.ttf";
    const string InterMediumItalic = "Assets/Fonts/Inter/Inter-MediumItalic.ttf";
    const string LiberationRegular = "Assets/Fonts/LiberationSans/LiberationSans-Regular.ttf";
    const string LiberationItalic = "Assets/Fonts/LiberationSans/LiberationSans-Italic.ttf";
    const string LiberationBold = "Assets/Fonts/LiberationSans/LiberationSans-Bold.ttf";
    const string LiberationBoldItalic = "Assets/Fonts/LiberationSans/LiberationSans-BoldItalic.ttf";

    readonly Func<PublicationFontPlatform> platform;
    readonly ISkiaSystemFontSource systemFonts;
    readonly Func<string, Stream> openAsset;
    readonly Action<string> log;
    readonly Func<PublicationFontPlatform, NativePublicationFontDefinition?> nativeDefinition;
    readonly ConcurrentDictionary<PublicationFont, Lazy<SkiaPublicationFontSet>> cache = new();

    public static SkiaPublicationFontResolver Shared { get; } = new(
        CurrentPlatform,
        new SkiaSystemFontSource(SKFontManager.Default),
        AppAssetLoader.Open,
        message => AppEventHandler.PrintAndLog(message));

    internal SkiaPublicationFontResolver(
        Func<PublicationFontPlatform> platform,
        ISkiaSystemFontSource systemFonts,
        Func<string, Stream> openAsset,
        Action<string>? log = null,
        Func<PublicationFontPlatform, NativePublicationFontDefinition?>? nativeDefinition = null)
    {
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.systemFonts = systemFonts ?? throw new ArgumentNullException(nameof(systemFonts));
        this.openAsset = openAsset ?? throw new ArgumentNullException(nameof(openAsset));
        this.log = log ?? (_ => { });
        this.nativeDefinition = nativeDefinition ?? GetNativeDefinition;
    }

    public SkiaPublicationFontSet Resolve(PublicationFont requestedFont)
    {
        var normalized = Enum.IsDefined(requestedFont) ? requestedFont : PublicationFont.Native;
        return cache.GetOrAdd(
            normalized,
            key => new Lazy<SkiaPublicationFontSet>(
                () => ResolveCore(key),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    SkiaPublicationFontSet ResolveCore(PublicationFont requestedFont)
    {
        return requestedFont switch
        {
            PublicationFont.Inter => LoadInter(requestedFont),
            PublicationFont.LiberationSans => LoadLiberation(requestedFont),
            _ => ResolveNative()
        };
    }

    SkiaPublicationFontSet ResolveNative()
    {
        var current = platform();
        var definition = nativeDefinition(current);
        if (definition == null)
            return NativeFallback("The current platform has no publication-font mapping.");

        if (definition.IsBundled)
            return LoadLiberation(PublicationFont.Native);

        if (!systemFonts.HasFamily(definition.Family))
            return NativeFallback($"The exact system family '{definition.Family}' is not installed.");

        var faces = new List<SKTypeface>();
        try
        {
            var regular = MatchNativeFace(definition.Family, definition.Faces[0], faces);
            var italic = MatchNativeFace(definition.Family, definition.Faces[1], faces);
            var emphasis = MatchNativeFace(definition.Family, definition.Faces[2], faces);
            var emphasisItalic = MatchNativeFace(definition.Family, definition.Faces[3], faces);

            return new SkiaPublicationFontSet(
                PublicationFont.Native,
                definition.Family,
                regular,
                italic,
                emphasis,
                emphasisItalic);
        }
        catch (Exception ex)
        {
            foreach (var face in faces) face.Dispose();
            return NativeFallback(ex.Message);
        }
    }

    SKTypeface MatchNativeFace(
        string family,
        PublicationFontFaceDefinition definition,
        ICollection<SKTypeface> ownedFaces)
    {
        var face = systemFonts.MatchFamily(family, definition.Weight, definition.Slant)
            ?? throw new InvalidOperationException(
                $"The {family} {StyleName(definition.Weight, definition.Slant)} face is unavailable.");
        ownedFaces.Add(face);

        ValidateFace(face, family, definition.Weight, definition.Slant, allowInterSubfamily: false);
        return face;
    }

    SkiaPublicationFontSet NativeFallback(string reason)
    {
        var message = $"Publication font Native fell back to Liberation Sans: {reason}";
        log(message);
        return LoadLiberation(PublicationFont.Native, isFallback: true, fallbackReason: reason);
    }

    SkiaPublicationFontSet LoadInter(PublicationFont requestedFont)
    {
        return LoadBundledSet(
            requestedFont,
            "Inter",
            true,
            false,
            "",
            (InterLight, SKFontStyleWeight.Light, SKFontStyleSlant.Upright),
            (InterLightItalic, SKFontStyleWeight.Light, SKFontStyleSlant.Italic),
            (InterMedium, SKFontStyleWeight.Medium, SKFontStyleSlant.Upright),
            (InterMediumItalic, SKFontStyleWeight.Medium, SKFontStyleSlant.Italic));
    }

    SkiaPublicationFontSet LoadLiberation(
        PublicationFont requestedFont,
        bool isFallback = false,
        string fallbackReason = "")
    {
        return LoadBundledSet(
            requestedFont,
            "Liberation Sans",
            false,
            isFallback,
            fallbackReason,
            (LiberationRegular, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright),
            (LiberationItalic, SKFontStyleWeight.Normal, SKFontStyleSlant.Italic),
            (LiberationBold, SKFontStyleWeight.Bold, SKFontStyleSlant.Upright),
            (LiberationBoldItalic, SKFontStyleWeight.Bold, SKFontStyleSlant.Italic));
    }

    SkiaPublicationFontSet LoadBundledSet(
        PublicationFont requestedFont,
        string family,
        bool allowInterSubfamily,
        bool isFallback,
        string fallbackReason,
        params (string Path, SKFontStyleWeight Weight, SKFontStyleSlant Slant)[] definitions)
    {
        var data = new List<SKData>();
        var faces = new List<SKTypeface>();
        try
        {
            foreach (var definition in definitions)
            {
                using var stream = openAsset(definition.Path);
                var fontData = SKData.Create(stream)
                    ?? throw new InvalidDataException($"Bundled font asset '{definition.Path}' could not be read.");
                data.Add(fontData);
                var face = SKTypeface.FromData(fontData)
                    ?? throw new InvalidDataException($"Bundled font asset '{definition.Path}' is not a valid typeface.");
                faces.Add(face);
                ValidateFace(face, family, definition.Weight, definition.Slant, allowInterSubfamily);
            }

            return new SkiaPublicationFontSet(
                requestedFont,
                family,
                faces[0],
                faces[1],
                faces[2],
                faces[3],
                isFallback,
                fallbackReason,
                data: data);
        }
        catch
        {
            foreach (var face in faces) face.Dispose();
            foreach (var item in data) item.Dispose();
            throw;
        }
    }

    static void ValidateFace(
        SKTypeface face,
        string family,
        SKFontStyleWeight weight,
        SKFontStyleSlant slant,
        bool allowInterSubfamily)
    {
        var familyMatches = string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase)
            || allowInterSubfamily && face.FamilyName.StartsWith(family + " ", StringComparison.OrdinalIgnoreCase);
        if (!familyMatches || face.FontWeight != (int)weight || face.FontSlant != slant)
        {
            throw new InvalidDataException(
                $"Expected {family} {StyleName(weight, slant)}, but Skia resolved " +
                $"{face.FamilyName} {StyleName((SKFontStyleWeight)face.FontWeight, face.FontSlant)}.");
        }
    }

    static string StyleName(SKFontStyleWeight weight, SKFontStyleSlant slant) =>
        $"{weight}{(slant == SKFontStyleSlant.Upright ? "" : " " + slant)}";

    static PublicationFontPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsMacOS()) return PublicationFontPlatform.MacOS;
        if (OperatingSystem.IsWindows()) return PublicationFontPlatform.Windows;
        if (OperatingSystem.IsLinux()) return PublicationFontPlatform.Linux;
        return PublicationFontPlatform.Other;
    }

    internal static NativePublicationFontDefinition? GetNativeDefinition(PublicationFontPlatform platform)
    {
        return platform switch
        {
            PublicationFontPlatform.MacOS => new NativePublicationFontDefinition(
                "Helvetica Neue", false, SKFontStyleWeight.Light, SKFontStyleWeight.Medium),
            PublicationFontPlatform.Windows => new NativePublicationFontDefinition(
                "Arial", false, SKFontStyleWeight.Normal, SKFontStyleWeight.Bold),
            PublicationFontPlatform.Linux => new NativePublicationFontDefinition(
                "Liberation Sans", true, SKFontStyleWeight.Normal, SKFontStyleWeight.Bold),
            _ => null
        };
    }
}
