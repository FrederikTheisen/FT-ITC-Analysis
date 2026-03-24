using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
    public enum FadeEdge
    {
        Top,
        Bottom
    }

    public partial class ScrollHintFadeView : AppKit.NSView
	{
        public FadeEdge Edge { get; set; } = FadeEdge.Bottom;

        // Use a semantic system color so it matches light/dark mode reasonably well.
        public NSColor BaseColor { get; set; } = NSColor.UnderPageBackground; //= NSColor.FromName("quinarySystemFill"); // MacColors.ResolveAdaptive(MacColors.FadeLight, MacColors.FadeDark);

        // How strong the fade should be at the solid edge.
        public nfloat MaxAlpha { get; set; } = 1f;

        #region Constructors

        // Called when created from unmanaged code
        public ScrollHintFadeView (IntPtr handle) : base (handle)
		{
			Initialize ();
		}

		// Called when created directly from a XIB file
		[Export ("initWithCoder:")]
		public ScrollHintFadeView (NSCoder coder) : base (coder)
		{
			Initialize ();
		}

		// Shared initialization code
		void Initialize ()
		{
            WantsLayer = true;
        }

        #endregion

        public override bool IsOpaque => false;

        public override NSView HitTest(CGPoint aPoint)
        {
            return null;
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            base.DrawRect(dirtyRect);

            if (Bounds.Height <= 0 || Bounds.Width <= 0) return;

            var context = NSGraphicsContext.CurrentContext?.CGContext;
            if (context == null) return;

            using var colorSpace = CGColorSpace.CreateDeviceRGB();

            var solid = BaseColor.ColorWithAlphaComponent(MaxAlpha).CGColor;
            var clear = BaseColor.ColorWithAlphaComponent(0).CGColor;

            using var gradient = new CGGradient(
                colorSpace,
                new CGColor[] { solid, clear },
                new nfloat[] { 0f, 1f });

            CGPoint start;
            CGPoint end;

            if (Edge == FadeEdge.Bottom)
            {
                // Solid at bottom, fading upward.
                start = new CGPoint(0, 0);
                end = new CGPoint(0, Bounds.Height);
            }
            else
            {
                // Solid at top, fading downward.
                start = new CGPoint(0, Bounds.Height);
                end = new CGPoint(0, 0);
            }

            context.SaveState();
            context.AddRect(Bounds);
            context.Clip();
            context.DrawLinearGradient(
                gradient,
                start,
                end,
                CGGradientDrawingOptions.DrawsBeforeStartLocation |
                CGGradientDrawingOptions.DrawsAfterEndLocation);
            context.RestoreState();
        }
    }
}
