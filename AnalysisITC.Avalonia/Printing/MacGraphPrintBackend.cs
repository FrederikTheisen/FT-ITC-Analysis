using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Avalonia.Controls;

namespace AnalysisITC.Avalonia.Printing;

internal sealed class MacGraphPrintBackend : IGraphPrintBackend
{
    static readonly object frameworkLock = new();
    static IntPtr pdfKitHandle;

    public Task<PrintOutcome> PrintAsync(Window owner, GraphPrintPayload payload)
    {
        EnsurePdfKitLoaded();

        var data = IntPtr.Zero;
        var document = IntPtr.Zero;
        var operation = IntPtr.Zero;
        var printInfo = IntPtr.Zero;
        var title = IntPtr.Zero;
        try
        {
            data = ObjC.Send(ObjC.Send(ObjC.GetClass("NSData"), "alloc"), "initWithBytes:length:", payload.Pdf, (nuint)payload.Pdf.Length);
            document = ObjC.Send(ObjC.Send(ObjC.GetClass("PDFDocument"), "alloc"), "initWithData:", data);
            if (document == IntPtr.Zero)
                throw new InvalidOperationException("PDFKit could not open the prepared graph.");

            printInfo = ObjC.Send(ObjC.Send(ObjC.GetClass("NSPrintInfo"), "sharedPrintInfo"), "copy");
            operation = ObjC.Send(document, "printOperationForPrintInfo:scalingMode:autoRotate:", printInfo, 1, false);
            if (operation == IntPtr.Zero)
                throw new InvalidOperationException("macOS could not create a print operation.");

            ObjC.Send(operation, "retain");
            title = ObjC.CreateString(payload.JobName);
            ObjC.Send(operation, "setJobTitle:", title);
            var printed = ObjC.SendBool(operation, "runOperation");
            return Task.FromResult(printed ? PrintOutcome.Printed : PrintOutcome.Canceled);
        }
        finally
        {
            ObjC.Release(title);
            ObjC.Release(operation);
            ObjC.Release(printInfo);
            ObjC.Release(document);
            ObjC.Release(data);
        }
    }

    static void EnsurePdfKitLoaded()
    {
        if (pdfKitHandle != IntPtr.Zero) return;
        lock (frameworkLock)
        {
            if (pdfKitHandle != IntPtr.Zero) return;
            pdfKitHandle = NativeLibrary.Load("/System/Library/Frameworks/PDFKit.framework/PDFKit");
        }
    }

    static class ObjC
    {
        const string Library = "/usr/lib/libobjc.A.dylib";

        public static IntPtr GetClass(string name) => objc_getClass(name);
        static IntPtr Selector(string name) => sel_registerName(name);

        public static IntPtr Send(IntPtr receiver, string selector) =>
            objc_msgSend(receiver, Selector(selector));

        public static IntPtr Send(IntPtr receiver, string selector, IntPtr argument) =>
            objc_msgSend_IntPtr(receiver, Selector(selector), argument);

        public static IntPtr Send(IntPtr receiver, string selector, IntPtr argument, int scaleMode, bool autoRotate) =>
            objc_msgSend_PrintOperation(receiver, Selector(selector), argument, scaleMode, autoRotate);

        public static IntPtr Send(IntPtr receiver, string selector, byte[] bytes, nuint length)
        {
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return objc_msgSend_Bytes(receiver, Selector(selector), pinned.AddrOfPinnedObject(), length);
            }
            finally
            {
                pinned.Free();
            }
        }

        public static bool SendBool(IntPtr receiver, string selector) =>
            objc_msgSend_Bool(receiver, Selector(selector));

        public static IntPtr CreateString(string value)
        {
            var allocated = Send(GetClass("NSString"), "alloc");
            return objc_msgSend_Utf8(allocated, Selector("initWithUTF8String:"), value);
        }

        public static void Release(IntPtr value)
        {
            if (value != IntPtr.Zero) Send(value, "release");
        }

        [DllImport(Library)] static extern IntPtr objc_getClass(string name);
        [DllImport(Library)] static extern IntPtr sel_registerName(string name);
        [DllImport(Library, EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
        [DllImport(Library, EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);
        [DllImport(Library, EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_Bytes(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);
        [DllImport(Library, EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_PrintOperation(IntPtr receiver, IntPtr selector, IntPtr printInfo, int scaleMode, [MarshalAs(UnmanagedType.I1)] bool autoRotate);
        [DllImport(Library, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool objc_msgSend_Bool(IntPtr receiver, IntPtr selector);
        [DllImport(Library, EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_Utf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    }
}
