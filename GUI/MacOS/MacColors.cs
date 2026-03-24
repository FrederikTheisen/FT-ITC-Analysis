using System;
using System.Collections.Generic;
using System.Drawing;
using AppKit;
using CoreGraphics;
using ObjCRuntime;

namespace AnalysisITC.GUI.MacOS
{
	public class MacColors : AppColors
	{
		//public static List<NSColor[]> Floral { get; } = ToNSColorArray(AppColors.Floral);
		//public static List<NSColor[]> Waves { get; } = ToNSColorArray(AppColors.Waves);
		//public static List<NSColor[]> Viridis { get; } = ToNSColorArray(AppColors.Viridis);

		public static readonly NSColor FadeDark = NSColor.FromCalibratedRgb(35, 35, 35);
        public static readonly NSColor FadeLight = NSColor.Grid;

        public static CGColor ColorToCG(Color color) => new(color.R/255f, color.G/255f, color.B/255f, color.A/255f);

        public static NSColor ResolveAdaptive(NSColor light, NSColor dark)
        {
			bool b = NSAppearance.CurrentAppearance.Name == NSAppearance.NameDarkAqua;

            return b ? dark : light;
        }

        static List<CGColor[]> ToNSColorArray(List<Color[]> colors)
		{
			List<CGColor[]> list = new List<CGColor[]>();

			foreach (var color in colors)
			{
				var c1 = color[0];
				var c2 = color[1];
				list.Add(new CGColor[] { NSColor.FromCalibratedRgb(c1.R, c1.G, c1.B).CGColor, NSColor.FromCalibratedRgb(c2.R, c2.G, c2.B).CGColor });
			}

			return list;
		}

		public static CGColor[] GetColor(int index, int count)
		{
			count = count - 1;
            List<Color[]> theme;

            switch (AppSettings.ColorScheme)
			{
				default:
				case ColorSchemes.Default: return null;
				case ColorSchemes.Viridis: theme = Viridis; break;
				case ColorSchemes.Waves: theme = Waves; break;
				case ColorSchemes.Floral: theme = Floral; break;
			}

			switch (AppSettings.ColorSchemeGradientMode)
			{
				default:
				case ColorSchemeGradientMode.Stepwise: return GetColorFromTheme(theme, index);
				case ColorSchemeGradientMode.Smooth: return GetColorFromTheme(theme, (float)index / count);
            }
		}

		public static CGColor[] GetColorFromTheme(List<Color[]> theme, int index)
		{
			while (index >= theme.Count)
			{
				index -= theme.Count;
            }

			return ToNSColorArray(theme)[index];
		}

        public static CGColor[] GetColorFromTheme(List<Color[]> theme, float fraction)
        {
			var color = AppColors.GetColorFromGradient(theme, fraction);
			var c1 = color[0];
			var c2 = color[1];

			return new CGColor[] { NSColor.FromCalibratedRgb(c1.R, c1.G, c1.B).CGColor, NSColor.FromCalibratedRgb(c2.R, c2.G, c2.B).CGColor };
        }

		public static CGColor Add(CGColor c1, CGColor c2)
		{
			var red = (float)Math.Clamp(c1.Components[0] + c2.Components[0], 0, 1);
            var green = (float)Math.Clamp(c1.Components[1] + c2.Components[1], 0, 1);
            var blue = (float)Math.Clamp(c1.Components[2] + c2.Components[2], 0, 1);

			return new CGColor(red, green, blue);
        }

        public static CGColor Subtract(CGColor c1, CGColor c2)
        {
            var red = (float)Math.Clamp(c1.Components[0] - c2.Components[0], 0, 1);
            var green = (float)Math.Clamp(c1.Components[1] - c2.Components[1], 0, 1);
            var blue = (float)Math.Clamp(c1.Components[2] - c2.Components[2], 0, 1);

            return new CGColor(red, green, blue);
        }

        public static CGColor Adjust(CGColor c1, float value)
        {
			if (value < 0) return Subtract(c1, new CGColor(-value, -value, -value));
			else return Add(c1, new CGColor(value, value, value));
        }

        public static CGColor Adjust(CGColor c1, int value)
        {
			return Adjust(c1, value / 255f);
        }

        public static CGColor WithAlpha(CGColor color, float alpha)
		{
			return new CGColor(color, alpha);
		}
    }
}

