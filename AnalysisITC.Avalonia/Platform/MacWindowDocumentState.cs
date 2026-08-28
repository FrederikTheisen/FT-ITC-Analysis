using System;
using System.Runtime.InteropServices;

using Avalonia.Controls;

namespace AnalysisITC.Platform.Avalonia;

internal static class MacWindowDocumentState
{
    const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
    const string NativeWindowDescriptor = "NSWindow";

    public static void SetDocumentEdited(Window window, bool edited)
    {
        if (!OperatingSystem.IsMacOS()) return;

        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle == null
            || platformHandle.Handle == IntPtr.Zero
            || !string.Equals(platformHandle.HandleDescriptor, NativeWindowDescriptor, StringComparison.Ordinal))
        {
            return;
        }

        objc_msgSend_Bool(
            platformHandle.Handle,
            sel_registerName("setDocumentEdited:"),
            edited);
    }

    [DllImport(ObjectiveC)]
    static extern IntPtr sel_registerName(string name);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    static extern void objc_msgSend_Bool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool argument);
}
