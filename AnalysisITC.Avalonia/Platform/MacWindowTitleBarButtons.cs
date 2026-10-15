using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AnalysisITC.Platform.Avalonia;

/// <summary>Positions AppKit's own window buttons in the integrated workflow header.</summary>
internal sealed class MacWindowTitleBarButtons : IDisposable
{
    static readonly Dictionary<IntPtr, MacWindowTitleBarButtons> Observers = new();
    static readonly NotificationCallback Updated = (self, _, _) =>
    {
        if (Observers.TryGetValue(self, out var owner)) owner.QueueLayout();
    };
    static readonly NotificationCallback TransitionStarted = (self, _, _) =>
    {
        if (Observers.TryGetValue(self, out var owner)) owner.transitioning = true;
    };
    static readonly NotificationCallback TransitionEnded = (self, _, _) =>
    {
        if (!Observers.TryGetValue(self, out var owner)) return;
        owner.transitioning = false;
        owner.QueueLayout();
    };
    static IntPtr observerClass;

    readonly Window window;
    readonly Control header;
    readonly double leftInset;
    readonly IntPtr observer;
    readonly IntPtr notificationCenter;
    readonly List<(IntPtr View, bool PostedFrameChanges)> observedViews = new();
    bool transitioning;
    bool layoutQueued;
    bool applyingLayout;
    bool disposed;

    MacWindowTitleBarButtons(Window window, Control header, double leftInset, IntPtr nativeWindow, IntPtr observer)
    {
        this.window = window;
        this.header = header;
        this.leftInset = leftInset;
        this.observer = observer;
        notificationCenter = Native.Send(Native.objc_getClass("NSNotificationCenter"), "defaultCenter");
        Observers.Add(observer, this);

        // AppKit can lay out the standard buttons again after a resize, theme change,
        // or title update. Observe this window only, without replacing its delegate.
        Observe(nativeWindow, "NSWindowDidUpdateNotification", "windowUpdated:");
        Observe(nativeWindow, "NSWindowWillEnterFullScreenNotification", "transitionStarted:");
        Observe(nativeWindow, "NSWindowWillExitFullScreenNotification", "transitionStarted:");
        Observe(nativeWindow, "NSWindowDidEnterFullScreenNotification", "transitionEnded:");
        Observe(nativeWindow, "NSWindowDidExitFullScreenNotification", "transitionEnded:");
        QueueLayout();
    }

    public static MacWindowTitleBarButtons? Attach(Window window, Control header, double leftInset)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero || handle.HandleDescriptor != "NSWindow") return null;

        if (observerClass == IntPtr.Zero)
        {
            observerClass = Native.objc_allocateClassPair(Native.objc_getClass("NSObject"),
                "FTITCWindowTitleBarObserver", UIntPtr.Zero);
            if (observerClass == IntPtr.Zero) return null;
            if (!AddMethod("windowUpdated:", Updated)
                || !AddMethod("transitionStarted:", TransitionStarted)
                || !AddMethod("transitionEnded:", TransitionEnded))
            {
                Native.objc_disposeClassPair(observerClass);
                observerClass = IntPtr.Zero;
                return null;
            }
            Native.objc_registerClassPair(observerClass);
        }

        var observer = Native.Send(Native.Send(observerClass, "alloc"), "init");
        return observer == IntPtr.Zero ? null : new(window, header, leftInset, handle.Handle, observer);
    }

    static bool AddMethod(string selector, NotificationCallback callback) =>
        Native.class_addMethod(observerClass, Native.sel_registerName(selector),
            Marshal.GetFunctionPointerForDelegate(callback), "v@:@");

    void Observe(IntPtr source, string notification, string selector)
    {
        var name = Native.CreateString(notification);
        try
        {
            Native.AddObserver(notificationCenter, Native.sel_registerName("addObserver:selector:name:object:"),
                observer, Native.sel_registerName(selector), name, source);
        }
        finally
        {
            Native.Send(name, "release");
        }
    }

    void QueueLayout()
    {
        if (disposed || layoutQueued || applyingLayout) return;
        layoutQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            layoutQueued = false;
            if (disposed || transitioning) return;
            applyingLayout = true;
            try { Layout(); }
            finally { applyingLayout = false; }
        }, DispatcherPriority.Loaded);
    }

    void Layout()
    {
        var nativeWindow = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var height = header.Bounds.Height;
        if (nativeWindow == IntPtr.Zero || height <= 0 || !header.IsVisible) return;

        // Full-screen chrome belongs to AppKit, including its reveal animation.
        // Restore our layout after the exit transition has finished.
        const long fullScreenStyleMask = 1L << 14;
        if ((Native.Send(nativeWindow, "styleMask").ToInt64() & fullScreenStyleMask) != 0) return;

        var close = Native.GetButton(nativeWindow, 0);
        var minimize = Native.GetButton(nativeWindow, 1);
        var zoom = Native.GetButton(nativeWindow, 2);
        if (close == IntPtr.Zero || minimize == IntPtr.Zero || zoom == IntPtr.Zero) return;
        var titleBar = Native.Send(close, "superview");
        var container = Native.Send(titleBar, "superview");
        var root = Native.Send(Native.Send(nativeWindow, "contentView"), "superview");

        // Do not alter an unfamiliar AppKit view hierarchy. The three standard
        // buttons must share a title-bar view inside a separate chrome container.
        if (titleBar == IntPtr.Zero || container == IntPtr.Zero || root == IntPtr.Zero
            || Native.Send(minimize, "superview") != titleBar
            || Native.Send(zoom, "superview") != titleBar
            || Native.Send(container, "superview") != root) return;

        ObserveFrameChanges(close, minimize, zoom, titleBar, container);

        var containerFrame = Native.GetRect(container, "frame");
        var rootFlipped = Native.GetBool(root, "isFlipped");
        Native.SetFrame(container, new Rect(containerFrame.X,
            rootFlipped ? containerFrame.Y : containerFrame.Bottom - height,
            containerFrame.Width, height));
        var titleBarFrame = Native.GetRect(titleBar, "frame");
        Native.SetFrame(titleBar, new Rect(titleBarFrame.X, 0, titleBarFrame.Width, height));

        // Grow both ancestors as well as moving the buttons, so the full native
        // hit targets remain inside their parents. Keep native sizes and spacing.
        var rootBounds = Native.GetRect(root, "bounds");
        var centre = new NativePoint(rootBounds.X + leftInset,
            rootFlipped ? rootBounds.Y + height / 2 : rootBounds.Bottom - height / 2);
        centre = Native.ConvertPoint(titleBar, Native.sel_registerName("convertPoint:fromView:"), centre, root);
        var closeFrame = Native.GetRect(close, "frame");
        Position(close, closeFrame, closeFrame.X, centre);
        Position(minimize, Native.GetRect(minimize, "frame"), closeFrame.X, centre);
        Position(zoom, Native.GetRect(zoom, "frame"), closeFrame.X, centre);
    }

    static void Position(IntPtr button, Rect frame, double originalLeft, NativePoint centre) =>
        Native.SetFrame(button, new Rect(centre.X + frame.X - originalLeft,
            centre.Y - frame.Height / 2, frame.Width, frame.Height));

    void ObserveFrameChanges(params IntPtr[] views)
    {
        if (observedViews.Count == views.Length
            && observedViews.TrueForAll(entry => Array.IndexOf(views, entry.View) >= 0)) return;

        ClearFrameObservers();
        foreach (var view in views)
        {
            // Retain while subscribed: AppKit can replace/reparent its chrome when
            // leaving full screen. Rebind on the next layout and release on close.
            Native.Send(view, "retain");
            observedViews.Add((view, Native.GetBool(view, "postsFrameChangedNotifications")));
            Observe(view, "NSViewFrameDidChangeNotification", "windowUpdated:");
            Native.SetBool(view, Native.sel_registerName("setPostsFrameChangedNotifications:"), true);
        }
    }

    void ClearFrameObservers()
    {
        foreach (var (view, postedFrameChanges) in observedViews)
        {
            Native.RemoveObserver(notificationCenter, Native.sel_registerName("removeObserver:name:object:"),
                observer, IntPtr.Zero, view);
            Native.SetBool(view, Native.sel_registerName("setPostsFrameChangedNotifications:"), postedFrameChanges);
            Native.Send(view, "release");
        }
        observedViews.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Observers.Remove(observer);
        ClearFrameObservers();
        Native.Send(notificationCenter, "removeObserver:", observer);
        Native.Send(observer, "release");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void NotificationCallback(IntPtr self, IntPtr selector, IntPtr notification);

    [StructLayout(LayoutKind.Sequential)]
    readonly struct NativePoint(double x, double y)
    {
        public readonly double X = x;
        public readonly double Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly struct NativeRect(Rect rect)
    {
        readonly double x = rect.X;
        readonly double y = rect.Y;
        readonly double width = rect.Width;
        readonly double height = rect.Height;
        public Rect ToRect() => new(x, y, width, height);
    }

    static class Native
    {
        const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

        public static IntPtr Send(IntPtr receiver, string selector) => SendPointer(receiver, sel_registerName(selector));
        public static IntPtr Send(IntPtr receiver, string selector, IntPtr argument) =>
            SendPointer(receiver, sel_registerName(selector), argument);
        public static IntPtr GetButton(IntPtr window, int index) => Send(window, "standardWindowButton:", (IntPtr)index);
        public static bool GetBool(IntPtr receiver, string selector) => SendBool(receiver, sel_registerName(selector));
        public static IntPtr CreateString(string value) =>
            SendString(Send(objc_getClass("NSString"), "alloc"), sel_registerName("initWithUTF8String:"), value);

        public static Rect GetRect(IntPtr receiver, string selector)
        {
            var method = sel_registerName(selector);
            // CGRect is returned indirectly on Intel, but directly on Apple silicon.
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                SendRectIntel(out var rect, receiver, method);
                return rect.ToRect();
            }
            return SendRect(receiver, method).ToRect();
        }

        public static void SetFrame(IntPtr view, Rect frame)
        {
            // Avoid repeatedly invalidating AppKit layout from its update notification.
            if (GetRect(view, "frame") != frame)
                SendRectArgument(view, sel_registerName("setFrame:"), new NativeRect(frame));
        }

        [DllImport(ObjectiveC)] public static extern IntPtr sel_registerName(string name);
        [DllImport(ObjectiveC)] public static extern IntPtr objc_getClass(string name);
        [DllImport(ObjectiveC)] public static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, UIntPtr extraBytes);
        [DllImport(ObjectiveC)] public static extern void objc_registerClassPair(IntPtr cls);
        [DllImport(ObjectiveC)] public static extern void objc_disposeClassPair(IntPtr cls);
        [DllImport(ObjectiveC)] [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector, IntPtr argument);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        static extern IntPtr SendString(IntPtr receiver, IntPtr selector, string argument);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
        static extern bool SendBool(IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        public static extern void SetBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        static extern NativeRect SendRect(IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend_stret")]
        static extern void SendRectIntel(out NativeRect result, IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        static extern void SendRectArgument(IntPtr receiver, IntPtr selector, NativeRect argument);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        public static extern NativePoint ConvertPoint(IntPtr receiver, IntPtr selector, NativePoint point, IntPtr fromView);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        public static extern void AddObserver(IntPtr receiver, IntPtr selector, IntPtr observer,
            IntPtr callback, IntPtr name, IntPtr obj);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        public static extern void RemoveObserver(IntPtr receiver, IntPtr selector, IntPtr observer, IntPtr name, IntPtr obj);
    }
}
