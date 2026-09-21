using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AnalysisITC.Platform.Avalonia
{
    static class NativeSystemNotification
    {
        static readonly object MacDelegateLock = new object();
        static readonly ShouldPresentNotificationDelegate MacShouldPresentNotification =
            (_, _, _, _) => 1;
        static IntPtr macNotificationDelegate;

        public static bool TryShow(string title, string message)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                    return TryShowMacNotification(title, message);

                if (OperatingSystem.IsWindows())
                    return TryShowWindowsNotification(title, message);

                if (OperatingSystem.IsLinux())
                    return TryShowLinuxNotification(title, message);
            }
            catch
            {
                return false;
            }

            return false;
        }

        static bool TryShowMacNotification(string title, string message)
        {
            // macOS notifications require an application bundle identity. Debug and
            // Release executables launched directly from bin/ are intentionally not
            // given an automation-based fallback.
            if (!HasMacBundleIdentifier()) return false;

            var notificationClass = objc_getClass("NSUserNotification");
            var centerClass = objc_getClass("NSUserNotificationCenter");
            if (notificationClass == IntPtr.Zero || centerClass == IntPtr.Zero) return false;

            var center = Send(centerClass, "defaultUserNotificationCenter");
            var notification = Send(Send(notificationClass, "alloc"), "init");
            if (center == IntPtr.Zero || notification == IntPtr.Zero)
            {
                Release(notification);
                return false;
            }

            var notificationDelegate = EnsureMacNotificationDelegate();
            if (notificationDelegate == IntPtr.Zero)
            {
                Release(notification);
                return false;
            }

            var titleValue = CreateMacString(title ?? string.Empty);
            var messageValue = CreateMacString(message ?? string.Empty);
            try
            {
                // The center suppresses banners while the app is active unless its
                // delegate explicitly opts in. This was the missing Avalonia behavior.
                Send(center, "setDelegate:", notificationDelegate);
                Send(notification, "setTitle:", titleValue);
                Send(notification, "setInformativeText:", messageValue);
                Send(center, "deliverNotification:", notification);
                return true;
            }
            finally
            {
                Release(messageValue);
                Release(titleValue);
                Release(notification);
            }
        }

        static bool HasMacBundleIdentifier()
        {
            var bundleClass = objc_getClass("NSBundle");
            if (bundleClass == IntPtr.Zero) return false;

            var bundle = Send(bundleClass, "mainBundle");
            return bundle != IntPtr.Zero && Send(bundle, "bundleIdentifier") != IntPtr.Zero;
        }

        static bool TryShowWindowsNotification(string title, string message)
        {
            // Keep the cross-platform application independent of the Windows App SDK
            // runtime while still using the native Windows notification area.
            var windowHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (windowHandle == IntPtr.Zero) windowHandle = GetForegroundWindow();
            if (windowHandle == IntPtr.Zero) return false;

            var notification = new NotifyIconData
            {
                Size = Marshal.SizeOf<NotifyIconData>(),
                WindowHandle = windowHandle,
                Id = NotificationIconId,
                Flags = NotifyIconFlags.Icon | NotifyIconFlags.Tip,
                IconHandle = LoadIcon(IntPtr.Zero, new IntPtr(DefaultApplicationIcon)),
                Tip = "FT-ITC Analysis",
                Info = Limit(message, 255),
                InfoTitle = Limit(title, 63),
                InfoFlags = NotifyInfoFlags.Info
            };

            if (!ShellNotifyIcon(NotifyIconMessage.Add, ref notification)) return false;

            notification.TimeoutOrVersion = NotifyIconVersion4;
            ShellNotifyIcon(NotifyIconMessage.SetVersion, ref notification);
            notification.Flags = NotifyIconFlags.Info;
            if (!ShellNotifyIcon(NotifyIconMessage.Modify, ref notification))
            {
                ShellNotifyIcon(NotifyIconMessage.Delete, ref notification);
                return false;
            }

            _ = RemoveWindowsNotificationIconAsync(notification);
            return true;
        }

        static bool TryShowLinuxNotification(string title, string message)
        {
            var executable = File.Exists("/usr/bin/notify-send")
                ? "/usr/bin/notify-send"
                : File.Exists("/bin/notify-send") ? "/bin/notify-send" : null;
            if (executable == null) return false;

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(title ?? string.Empty);
            startInfo.ArgumentList.Add(message ?? string.Empty);
            Process.Start(startInfo);
            return true;
        }

        static async Task RemoveWindowsNotificationIconAsync(NotifyIconData notification)
        {
            await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            ShellNotifyIcon(NotifyIconMessage.Delete, ref notification);
        }

        static string Limit(string value, int maximumLength) =>
            string.IsNullOrEmpty(value) || value.Length <= maximumLength
                ? value ?? string.Empty
                : value.Substring(0, maximumLength);

        static IntPtr EnsureMacNotificationDelegate()
        {
            if (macNotificationDelegate != IntPtr.Zero) return macNotificationDelegate;

            lock (MacDelegateLock)
            {
                if (macNotificationDelegate != IntPtr.Zero) return macNotificationDelegate;

                const string className = "FTITCNotificationCenterDelegate";
                var delegateClass = objc_getClass(className);
                if (delegateClass == IntPtr.Zero)
                {
                    delegateClass = objc_allocateClassPair(
                        objc_getClass("NSObject"), className, UIntPtr.Zero);
                    if (delegateClass == IntPtr.Zero) return IntPtr.Zero;

                    var implementation = Marshal.GetFunctionPointerForDelegate(MacShouldPresentNotification);
                    if (!class_addMethod(
                        delegateClass,
                        sel_registerName("userNotificationCenter:shouldPresentNotification:"),
                        implementation,
                        "c@:@@"))
                        return IntPtr.Zero;

                    objc_registerClassPair(delegateClass);
                }

                macNotificationDelegate = Send(Send(delegateClass, "alloc"), "init");
                return macNotificationDelegate;
            }
        }

        static IntPtr CreateMacString(string value)
        {
            var instance = Send(objc_getClass("NSString"), "alloc");
            return objc_msgSend_string(
                instance, sel_registerName("initWithUTF8String:"), value);
        }

        static void Release(IntPtr instance)
        {
            if (instance != IntPtr.Zero) Send(instance, "release");
        }

        static IntPtr Send(IntPtr receiver, string selector) =>
            objc_msgSend(receiver, sel_registerName(selector));

        static IntPtr Send(IntPtr receiver, string selector, IntPtr argument) =>
            objc_msgSend(receiver, sel_registerName(selector), argument);

        const uint NotificationIconId = 0x46544954;
        const int DefaultApplicationIcon = 32512;
        const uint NotifyIconVersion4 = 4;

        enum NotifyIconMessage : uint
        {
            Add = 0,
            Modify = 1,
            Delete = 2,
            SetVersion = 4
        }

        [Flags]
        enum NotifyIconFlags : uint
        {
            Icon = 0x2,
            Tip = 0x4,
            Info = 0x10
        }

        [Flags]
        enum NotifyInfoFlags : uint
        {
            Info = 0x1
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NotifyIconData
        {
            public int Size;
            public IntPtr WindowHandle;
            public uint Id;
            public NotifyIconFlags Flags;
            public uint CallbackMessage;
            public IntPtr IconHandle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State;
            public uint StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint TimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public NotifyInfoFlags InfoFlags;
            public Guid ItemGuid;
            public IntPtr BalloonIconHandle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool Shell_NotifyIcon(NotifyIconMessage message, ref NotifyIconData data);

        static bool ShellNotifyIcon(NotifyIconMessage message, ref NotifyIconData data) =>
            Shell_NotifyIcon(message, ref data);

        [DllImport("user32.dll")]
        static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate byte ShouldPresentNotificationDelegate(
            IntPtr instance,
            IntPtr selector,
            IntPtr center,
            IntPtr notification);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        static extern IntPtr objc_allocateClassPair(
            IntPtr superclass,
            string name,
            UIntPtr extraBytes);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        static extern void objc_registerClassPair(IntPtr cls);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        [return: MarshalAs(UnmanagedType.I1)]
        static extern bool class_addMethod(
            IntPtr cls,
            IntPtr name,
            IntPtr implementation,
            string types);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        static extern IntPtr objc_msgSend(
            IntPtr receiver,
            IntPtr selector,
            IntPtr argument);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        static extern IntPtr objc_msgSend_string(
            IntPtr receiver,
            IntPtr selector,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);
    }
}
