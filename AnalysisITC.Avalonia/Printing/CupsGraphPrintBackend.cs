using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AnalysisITC.Avalonia.Printing;

internal sealed class CupsGraphPrintBackend : IGraphPrintBackend
{
    public async Task<PrintOutcome> PrintAsync(Window owner, GraphPrintPayload payload)
    {
        IReadOnlyList<CupsPrinter> printers;
        try
        {
            printers = await Task.Run(CupsNative.GetPrinters);
        }
        catch (DllNotFoundException)
        {
            printers = Array.Empty<CupsPrinter>();
        }

        var selection = await LinuxPrintDialogWindow.ShowAsync(owner, printers, payload.SourceSize);
        if (selection == null) return PrintOutcome.Canceled;
        if (selection.SaveAsPdf)
            return await SavePdfAsync(owner, payload);
        var options = selection.PrintOptions
            ?? throw new InvalidOperationException("No CUPS printer was selected.");

        var path = Path.Combine(Path.GetTempPath(), $"ft-itc-print-{Guid.NewGuid():N}.pdf");
        try
        {
            var pdf = ComposePdf(payload, options);
            await File.WriteAllBytesAsync(path, pdf);
            await Task.Run(() => CupsNative.Submit(options, path, payload.JobName));
            return PrintOutcome.Printed;
        }
        finally
        {
            try { File.Delete(path); }
            catch { }
        }
    }

    static async Task<PrintOutcome> SavePdfAsync(Window owner, GraphPrintPayload payload)
    {
        var suggestedName = string.Concat(payload.JobName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Graph as PDF",
            SuggestedFileName = suggestedName + ".pdf",
            DefaultExtension = "pdf",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Vector PDF") { Patterns = new[] { "*.pdf" } },
                FilePickerFileTypes.All
            }
        });
        if (file == null) return PrintOutcome.Canceled;

        await using var output = await file.OpenWriteAsync();
        if (output.CanSeek) output.SetLength(0);
        await output.WriteAsync(payload.Pdf);
        await output.FlushAsync();
        return PrintOutcome.Saved;
    }

    static byte[] ComposePdf(GraphPrintPayload payload, LinuxPrintOptions options)
    {
        if (options.Media == null || payload.PreservePdf) return payload.Pdf;

        const double pointsPerHundredthMillimeter = 72.0 / 2540.0;
        var media = options.Media;
        var width = media.WidthHundredthsMillimeter * pointsPerHundredthMillimeter;
        var height = media.HeightHundredthsMillimeter * pointsPerHundredthMillimeter;
        var left = media.LeftMargin * pointsPerHundredthMillimeter;
        var right = media.RightMargin * pointsPerHundredthMillimeter;
        var top = media.TopMargin * pointsPerHundredthMillimeter;
        var bottom = media.BottomMargin * pointsPerHundredthMillimeter;

        if (options.Orientation == LinuxPrintOrientation.Landscape)
        {
            (width, height) = (height, width);
            (left, top, right, bottom) = (bottom, left, top, right);
        }

        return RasterPdfComposer.CreatePagePdf(
            payload.Bitmap,
            payload.JobName,
            new PrintSize(width, height),
            new PrintRect(left, top, Math.Max(1, width - left - right), Math.Max(1, height - top - bottom)));
    }
}

internal sealed record CupsPrinter(
    string Name,
    string DisplayName,
    bool IsDefault,
    IReadOnlyList<CupsMedia> Media,
    string? DefaultMedia,
    IReadOnlyList<CupsColorMode> ColorModes,
    string? DefaultColorMode)
{
    public override string ToString() => DisplayName;
}

internal sealed record CupsMedia(
    string Name,
    int WidthHundredthsMillimeter,
    int HeightHundredthsMillimeter,
    int BottomMargin,
    int LeftMargin,
    int RightMargin,
    int TopMargin)
{
    public override string ToString() => Name;
}

internal sealed record CupsColorMode(string Value, string Label)
{
    public override string ToString() => Label;
}

internal enum LinuxPrintOrientation
{
    Automatic,
    Portrait,
    Landscape
}

internal sealed record LinuxPrintOptions(
    CupsPrinter Printer,
    CupsMedia? Media,
    LinuxPrintOrientation Orientation,
    int Copies,
    CupsColorMode? ColorMode);

internal static class CupsNative
{
    const string Library = "libcups.so.2";

    public static IReadOnlyList<CupsPrinter> GetPrinters()
    {
        var count = cupsGetDests2(IntPtr.Zero, out var destinations);
        if (count < 0) ThrowLastError("CUPS could not enumerate printers");

        try
        {
            var printers = new List<CupsPrinter>();
            var size = Marshal.SizeOf<cups_dest_t>();
            for (var i = 0; i < count; i++)
            {
                var pointer = IntPtr.Add(destinations, i * size);
                var destination = Marshal.PtrToStructure<cups_dest_t>(pointer);
                var name = Utf8(destination.name);
                if (string.IsNullOrWhiteSpace(name)) continue;

                var instance = Utf8(destination.instance);
                var fullName = string.IsNullOrWhiteSpace(instance) ? name : $"{name}/{instance}";
                var description = Utf8(cupsGetOption("printer-info", destination.num_options, destination.options));
                var display = string.IsNullOrWhiteSpace(description) ? fullName : description;

                var media = new List<CupsMedia>();
                string? defaultMedia = null;
                var colors = new List<CupsColorMode>();
                string? defaultColor = Utf8(cupsGetOption("print-color-mode", destination.num_options, destination.options));
                var info = cupsCopyDestInfo(IntPtr.Zero, pointer);
                if (info != IntPtr.Zero)
                {
                    try
                    {
                        if (cupsGetDestMediaDefault(IntPtr.Zero, pointer, info, 0, out var defaultSize) != 0)
                            defaultMedia = defaultSize.MediaName;

                        var mediaCount = cupsGetDestMediaCount(IntPtr.Zero, pointer, info, 0);
                        for (var mediaIndex = 0; mediaIndex < mediaCount; mediaIndex++)
                        {
                            if (cupsGetDestMediaByIndex(IntPtr.Zero, pointer, info, mediaIndex, 0, out var sizeInfo) == 0)
                                continue;
                            if (string.IsNullOrWhiteSpace(sizeInfo.MediaName)) continue;
                            media.Add(new CupsMedia(
                                sizeInfo.MediaName,
                                sizeInfo.width,
                                sizeInfo.length,
                                sizeInfo.bottom,
                                sizeInfo.left,
                                sizeInfo.right,
                                sizeInfo.top));
                        }

                        if (cupsCheckDestSupported(IntPtr.Zero, pointer, info, "print-color-mode", "color") != 0)
                            colors.Add(new CupsColorMode("color", "Color"));
                        if (cupsCheckDestSupported(IntPtr.Zero, pointer, info, "print-color-mode", "monochrome") != 0)
                            colors.Add(new CupsColorMode("monochrome", "Monochrome"));
                    }
                    finally
                    {
                        cupsFreeDestInfo(info);
                    }
                }

                printers.Add(new CupsPrinter(
                    fullName,
                    display,
                    destination.is_default != 0,
                    media.GroupBy(item => item.Name).Select(group => group.First()).ToList(),
                    defaultMedia,
                    colors,
                    defaultColor));
            }

            return printers
                .OrderByDescending(printer => printer.IsDefault)
                .ThenBy(printer => printer.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        finally
        {
            if (destinations != IntPtr.Zero) cupsFreeDests(count, destinations);
        }
    }

    public static void Submit(LinuxPrintOptions settings, string path, string title)
    {
        var optionCount = 0;
        var options = IntPtr.Zero;
        try
        {
            if (settings.Media != null)
                optionCount = cupsAddOption("media", settings.Media.Name, optionCount, ref options);

            var landscape = settings.Orientation == LinuxPrintOrientation.Landscape;
            optionCount = cupsAddOption("orientation-requested", landscape ? "4" : "3", optionCount, ref options);
            optionCount = cupsAddOption("copies", Math.Max(1, settings.Copies).ToString(System.Globalization.CultureInfo.InvariantCulture), optionCount, ref options);
            optionCount = cupsAddOption("fit-to-page", "true", optionCount, ref options);
            if (settings.ColorMode != null)
                optionCount = cupsAddOption("print-color-mode", settings.ColorMode.Value, optionCount, ref options);

            var jobId = cupsPrintFile2(IntPtr.Zero, settings.Printer.Name, path, title, optionCount, options);
            if (jobId == 0) ThrowLastError("CUPS rejected the print job");
        }
        finally
        {
            if (options != IntPtr.Zero) cupsFreeOptions(optionCount, options);
        }
    }

    static void ThrowLastError(string prefix)
    {
        var detail = Utf8(cupsLastErrorString());
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix}: {detail}");
    }

    static string Utf8(IntPtr value) => value == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(value) ?? "";

    [StructLayout(LayoutKind.Sequential)]
    struct cups_dest_t
    {
        public IntPtr name;
        public IntPtr instance;
        public int is_default;
        public int num_options;
        public IntPtr options;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    struct cups_size_t
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string media;
        public int width;
        public int length;
        public int bottom;
        public int left;
        public int right;
        public int top;

        public string MediaName => media ?? "";
    }

    [DllImport(Library)] static extern int cupsGetDests2(IntPtr http, out IntPtr destinations);
    [DllImport(Library)] static extern void cupsFreeDests(int count, IntPtr destinations);
    [DllImport(Library)] static extern IntPtr cupsGetOption([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int count, IntPtr options);
    [DllImport(Library)] static extern IntPtr cupsCopyDestInfo(IntPtr http, IntPtr destination);
    [DllImport(Library)] static extern void cupsFreeDestInfo(IntPtr info);
    [DllImport(Library)] static extern int cupsGetDestMediaCount(IntPtr http, IntPtr destination, IntPtr info, uint flags);
    [DllImport(Library)] static extern int cupsGetDestMediaByIndex(IntPtr http, IntPtr destination, IntPtr info, int index, uint flags, out cups_size_t size);
    [DllImport(Library)] static extern int cupsGetDestMediaDefault(IntPtr http, IntPtr destination, IntPtr info, uint flags, out cups_size_t size);
    [DllImport(Library)] static extern int cupsCheckDestSupported(IntPtr http, IntPtr destination, IntPtr info, [MarshalAs(UnmanagedType.LPUTF8Str)] string option, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library)] static extern int cupsAddOption([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int count, ref IntPtr options);
    [DllImport(Library)] static extern void cupsFreeOptions(int count, IntPtr options);
    [DllImport(Library)] static extern int cupsPrintFile2(IntPtr http, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, [MarshalAs(UnmanagedType.LPUTF8Str)] string title, int numOptions, IntPtr options);
    [DllImport(Library)] static extern IntPtr cupsLastErrorString();
}
