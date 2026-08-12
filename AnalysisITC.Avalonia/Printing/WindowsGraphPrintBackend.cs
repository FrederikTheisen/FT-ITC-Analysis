using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Avalonia.Controls;

using SkiaSharp;

namespace AnalysisITC.Avalonia.Printing;

internal sealed class WindowsGraphPrintBackend : IGraphPrintBackend
{
    public Task<PrintOutcome> PrintAsync(Window owner, GraphPrintPayload payload)
    {
        var dialog = PRINTDLGEX.Create(owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        var result = PrintDlgExW(ref dialog);
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);

        try
        {
            if (dialog.dwResultAction != PD_RESULT_PRINT)
                return Task.FromResult(PrintOutcome.Canceled);
            if (dialog.hDC == IntPtr.Zero)
                throw new InvalidOperationException("Windows did not return a printer device context.");

            PrintBitmap(dialog.hDC, payload);
            return Task.FromResult(PrintOutcome.Printed);
        }
        finally
        {
            if (dialog.hDC != IntPtr.Zero) DeleteDC(dialog.hDC);
            if (dialog.hDevMode != IntPtr.Zero) GlobalFree(dialog.hDevMode);
            if (dialog.hDevNames != IntPtr.Zero) GlobalFree(dialog.hDevNames);
        }
    }

    static void PrintBitmap(IntPtr dc, GraphPrintPayload payload)
    {
        var info = new DOCINFO
        {
            cbSize = Marshal.SizeOf<DOCINFO>(),
            lpszDocName = payload.JobName
        };
        if (StartDocW(dc, ref info) <= 0) throw new Win32Exception();

        var pageStarted = false;
        try
        {
            if (StartPage(dc) <= 0) throw new Win32Exception();
            pageStarted = true;

            var pageWidth = GetDeviceCaps(dc, HORZRES);
            var pageHeight = GetDeviceCaps(dc, VERTRES);
            var target = PrintGeometry.Fit(payload.SourceSize, new PrintRect(0, 0, pageWidth, pageHeight));

            using var bitmap = new SKBitmap(new SKImageInfo(
                payload.Bitmap.Width,
                payload.Bitmap.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));
            if (!payload.Bitmap.CopyTo(bitmap, SKColorType.Bgra8888))
                throw new InvalidOperationException("The graph pixels could not be prepared for Windows printing.");

            var bitmapInfo = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = bitmap.Width,
                    biHeight = -bitmap.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                    biSizeImage = (uint)(bitmap.RowBytes * bitmap.Height)
                }
            };

            SetStretchBltMode(dc, HALFTONE);
            var copied = StretchDIBits(
                dc,
                (int)Math.Round(target.X),
                (int)Math.Round(target.Y),
                (int)Math.Round(target.Width),
                (int)Math.Round(target.Height),
                0,
                0,
                bitmap.Width,
                bitmap.Height,
                bitmap.GetPixels(),
                ref bitmapInfo,
                DIB_RGB_COLORS,
                SRCCOPY);
            if (copied == 0 || copied == GDI_ERROR) throw new Win32Exception();

            if (EndPage(dc) <= 0) throw new Win32Exception();
            pageStarted = false;
            if (EndDoc(dc) <= 0) throw new Win32Exception();
        }
        catch
        {
            if (pageStarted) EndPage(dc);
            AbortDoc(dc);
            throw;
        }
    }

    const uint PD_RETURNDC = 0x00000100;
    const uint PD_NOSELECTION = 0x00000004;
    const uint PD_NOPAGENUMS = 0x00000008;
    const uint PD_USEDEVMODECOPIESANDCOLLATE = 0x00040000;
    const uint PD_RESULT_PRINT = 1;
    const uint START_PAGE_GENERAL = 0xffffffff;
    const int HORZRES = 8;
    const int VERTRES = 10;
    const int HALFTONE = 4;
    const uint BI_RGB = 0;
    const uint DIB_RGB_COLORS = 0;
    const uint SRCCOPY = 0x00CC0020;
    const int GDI_ERROR = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PRINTDLGEX
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hDevMode;
        public IntPtr hDevNames;
        public IntPtr hDC;
        public uint Flags;
        public uint Flags2;
        public uint ExclusionFlags;
        public uint nPageRanges;
        public uint nMaxPageRanges;
        public IntPtr lpPageRanges;
        public uint nMinPage;
        public uint nMaxPage;
        public uint nCopies;
        public IntPtr hInstance;
        public IntPtr lpPrintTemplateName;
        public IntPtr lpCallback;
        public uint nPropertyPages;
        public IntPtr lphPropertyPages;
        public uint nStartPage;
        public uint dwResultAction;

        public static PRINTDLGEX Create(IntPtr owner) => new()
        {
            lStructSize = (uint)Marshal.SizeOf<PRINTDLGEX>(),
            hwndOwner = owner,
            Flags = PD_RETURNDC | PD_NOSELECTION | PD_NOPAGENUMS | PD_USEDEVMODECOPIESANDCOLLATE,
            nMinPage = 1,
            nMaxPage = 1,
            nCopies = 1,
            nStartPage = START_PAGE_GENERAL
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DOCINFO
    {
        public int cbSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszDocName;
        public IntPtr lpszOutput;
        public IntPtr lpszDatatype;
        public uint fwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] static extern int PrintDlgExW(ref PRINTDLGEX dialog);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int StartDocW(IntPtr dc, ref DOCINFO info);
    [DllImport("gdi32.dll")] static extern int EndDoc(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int AbortDoc(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int StartPage(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int EndPage(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern int StretchDIBits(IntPtr dc, int xDest, int yDest, int destWidth, int destHeight, int xSrc, int ySrc, int srcWidth, int srcHeight, IntPtr bits, ref BITMAPINFO info, uint usage, uint rasterOperation);
}
