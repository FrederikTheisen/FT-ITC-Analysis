using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;

namespace AnalysisITC.GUI
{
	public class AppColors
	{
		public static List<Color[]> Floral { get; } = new List<Color[]>(){
			new Color[] { Color.FromArgb(124, 160, 212), Color.FromArgb(40, 82, 145) },
            new Color[] { Color.FromArgb(164, 138, 211), Color.FromArgb(79, 43, 142) },
            new Color[] { Color.FromArgb(233, 149, 235), Color.FromArgb(145, 24, 142) },
            new Color[] { Color.FromArgb(186, 222, 134), Color.FromArgb(83, 144, 39) },
            new Color[] { Color.FromArgb(43, 138, 174), Color.FromArgb(13, 64, 91) },
            new Color[] { Color.FromArgb(52, 39, 77), Color.FromArgb(98, 72, 148) },
            new Color[] { Color.FromArgb(222, 117, 123), Color.FromArgb(145, 24, 29) },
            new Color[] { Color.FromArgb(139, 165, 111), Color.FromArgb(44, 51, 36) },
            new Color[] { Color.FromArgb(7, 63, 128), Color.FromArgb(2, 34, 64) },
            new Color[] { Color.FromArgb(64, 0, 127), Color.FromArgb(33, 6, 63) },
            new Color[] { Color.FromArgb(128, 0, 63), Color.FromArgb(61, 6, 32) },
            new Color[] { Color.FromArgb(10, 82, 42), Color.FromArgb(2, 32, 19) },
        };

        public static List<Color[]> Waves { get; } = new List<Color[]>(){
            new Color[] { Color.FromArgb(46, 88, 164), Color.FromArgb(3, 20, 49) },
            new Color[] { Color.FromArgb(182, 157, 113), Color.FromArgb(111, 90, 53) },
            new Color[] { Color.FromArgb(227, 222, 212), Color.FromArgb(99, 97, 93) },
            new Color[] { Color.FromArgb(112, 175, 199), Color.FromArgb(51, 86, 95) },
            new Color[] { Color.FromArgb(79, 83, 87), Color.FromArgb(39, 41, 43) },
        };

        public static List<Color[]> Viridis { get; } = new List<Color[]>(){
            new Color[] { Color.FromArgb(68, 1, 84), Color.FromArgb(0, 0, 0) },
            new Color[] { Color.FromArgb(65, 68, 135), Color.FromArgb(0, 0, 0) },
            new Color[] { Color.FromArgb(42, 120, 142), Color.FromArgb(0, 0, 0) },
            new Color[] { Color.FromArgb(34, 168, 132), Color.FromArgb(0, 0, 0) },
            new Color[] { Color.FromArgb(122, 209, 81), Color.FromArgb(0, 0, 0) },
            new Color[] { Color.FromArgb(253, 231, 37), Color.FromArgb(0, 0, 0) },
        };

        public AppColors()
		{
		}

        public static Color[] GetColorFromGradient(List<Color[]> theme, float fraction)
        {
            if (fraction == 1) return theme.Last();
            if (fraction == 0) return theme.First();
            if (theme.Count == 2) return new Color[]
                    {
                        TwoColorGradientPick(fraction, theme[0][0], theme[1][0]),
                        TwoColorGradientPick(fraction, theme[0][1], theme[1][1])
                    };
            if (theme.Count == 3) return new Color[]
                    {
                        ThreeColorGradientPick(fraction, theme[0][0], theme[1][0], theme[2][0]),
                        ThreeColorGradientPick(fraction, theme[0][1], theme[1][1], theme[2][1])
                    };

            int index = (int)(fraction * (theme.Count - 1));

            var c1 = theme[index];
            var c2 = theme[index + 1];

            var gradientposition = (fraction * (theme.Count - 1) - index);

            Console.WriteLine(gradientposition.ToString());

            return new Color[]
                {
                    TwoColorGradientPick(gradientposition, c1[0], c2[0]),
                    TwoColorGradientPick(gradientposition, c1[1], c2[1]),
                };
        }

        static int LinearInterp(int start, int end, double percentage) => start + (int)Math.Round(percentage * (end - start));
        static Color TwoColorGradientPick(double percentage, Color start, Color end) =>
            Color.FromArgb(LinearInterp(start.A, end.A, percentage),
                           LinearInterp(start.R, end.R, percentage),
                           LinearInterp(start.G, end.G, percentage),
                           LinearInterp(start.B, end.B, percentage));
        static Color ThreeColorGradientPick(double percentage, Color start, Color center, Color end)
        {
            if (percentage < 0.5)
                return TwoColorGradientPick(percentage / 0.5, start, center);
            else if (percentage == 0.5)
                return center;
            else
                return TwoColorGradientPick((percentage - 0.5) / 0.5, center, end);
        }

        public static List<ColorSchemes> GetColorSchemes()
        {
            return new List<ColorSchemes>()
            {
                ColorSchemes.Default,
                ColorSchemes.Viridis,
                ColorSchemes.Waves,
                ColorSchemes.Floral
            };
        }
    }

    public enum ColorSchemes
    {
        [Description("Default system color scheme")]
        Default,
        Viridis,
        Waves,
        Floral,
    }
    
    public enum ColorShcemeGradientMode
    {
        [Description("Only pick defined colors")]
        Stepwise,
        [Description("Smooth gradient between each defined color")]
        Smooth
    }
}

