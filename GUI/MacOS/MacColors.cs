using System;
using System.Collections.Generic;
using System.Drawing;
using AppKit;
using CoreGraphics;

namespace AnalysisITC.GUI.MacOS
{
	public class MacColors : AppColors
	{
		//public static List<NSColor[]> Floral { get; } = ToNSColorArray(AppColors.Floral);
		//public static List<NSColor[]> Waves { get; } = ToNSColorArray(AppColors.Waves);
		//public static List<NSColor[]> Viridis { get; } = ToNSColorArray(AppColors.Viridis);

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

			switch (AppSettings.ColorShcemeGradientMode)
			{
				default:
				case ColorShcemeGradientMode.Stepwise: return GetColorFromTheme(theme, index);
				case ColorShcemeGradientMode.Smooth: return GetColorFromTheme(theme, (float)index / count);
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
    }
}

