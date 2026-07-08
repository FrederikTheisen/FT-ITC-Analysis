using System;
using System.IO;
using System.Runtime.InteropServices;

using AnalysisITC.Core.Application;

namespace AnalysisITC.Platform.Avalonia
{
    static class MacDockIcon
    {
        public static void Apply()
        {
            if (!OperatingSystem.IsMacOS()) return;

            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.icns");
                if (!File.Exists(iconPath))
                {
                    AppEventHandler.PrintAndLog($"MacDockIcon: icon not found at {iconPath}");
                    return;
                }

                using var path = NSObject.FromString(iconPath);
                using var image = NSObject.Create("NSImage", "initWithContentsOfFile:", path.Handle);
                if (image.Handle == IntPtr.Zero)
                {
                    AppEventHandler.PrintAndLog("MacDockIcon: could not load AppIcon.icns");
                    return;
                }

                var app = NSObject.Send(NSObject.GetClass("NSApplication"), "sharedApplication");
                NSObject.Send(app, "setApplicationIconImage:", image.Handle);
            }
            catch (Exception ex)
            {
                AppEventHandler.AddLog(ex);
            }
        }

        sealed class NSObject : IDisposable
        {
            public IntPtr Handle { get; private set; }

            NSObject(IntPtr handle)
            {
                Handle = handle;
            }

            public static NSObject FromString(string value)
            {
                var handle = Send(GetClass("NSString"), "alloc");
                return new NSObject(Send(handle, "initWithUTF8String:", value));
            }

            public static NSObject Create(string className, string initializer, IntPtr argument)
            {
                var handle = Send(GetClass(className), "alloc");
                return new NSObject(Send(handle, initializer, argument));
            }

            public static IntPtr GetClass(string className) => objc_getClass(className);

            public static IntPtr Send(IntPtr receiver, string selector) =>
                objc_msgSend(receiver, sel_registerName(selector));

            public static IntPtr Send(IntPtr receiver, string selector, IntPtr argument) =>
                objc_msgSend(receiver, sel_registerName(selector), argument);

            static IntPtr Send(IntPtr receiver, string selector, string argument) =>
                objc_msgSend(receiver, sel_registerName(selector), argument);

            public void Dispose()
            {
                if (Handle == IntPtr.Zero) return;

                Send(Handle, "release");
                Handle = IntPtr.Zero;
            }

            [DllImport("/usr/lib/libobjc.A.dylib")]
            static extern IntPtr objc_getClass(string name);

            [DllImport("/usr/lib/libobjc.A.dylib")]
            static extern IntPtr sel_registerName(string name);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr argument);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            static extern IntPtr objc_msgSend(
                IntPtr receiver,
                IntPtr selector,
                [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);
        }
    }
}
