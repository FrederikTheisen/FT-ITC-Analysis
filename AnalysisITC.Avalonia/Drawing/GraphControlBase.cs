using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AnalysisITC.Avalonia
{
    public abstract class GraphControlBase : Control
    {
        protected static double Crisp(double value) => Math.Round(value) + 0.5;

        protected static FormattedText CreateText(string text, double size, FontWeight weight, IBrush brush)
        {
            return new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, weight),
                size,
                brush);
        }

        protected static void DrawText(DrawingContext context, string text, Point point, double size, FontWeight weight, IBrush brush)
        {
            context.DrawText(CreateText(text, size, weight, brush), point);
        }

        protected static void DrawCenteredText(DrawingContext context, string text, Point point, double size, IBrush brush)
        {
            var formatted = CreateText(text, size, FontWeight.Normal, brush);
            context.DrawText(formatted, new Point(point.X - formatted.Width / 2, point.Y));
        }

        protected static void DrawRightAlignedText(DrawingContext context, string text, Point point, double size, IBrush brush)
        {
            var formatted = CreateText(text, size, FontWeight.Normal, brush);
            context.DrawText(formatted, new Point(point.X - formatted.Width, point.Y));
        }

        protected readonly struct AxisTicks
        {
            public IReadOnlyList<double> Major { get; }
            public IReadOnlyList<double> Minor { get; }
            readonly double step;

            AxisTicks(IReadOnlyList<double> major, IReadOnlyList<double> minor, double step)
            {
                Major = major;
                Minor = minor;
                this.step = step;
            }

            public static AxisTicks Create(double min, double max, int maxTicks)
            {
                var range = max - min;
                if (!double.IsFinite(range) || Math.Abs(range) < double.Epsilon)
                {
                    range = 1;
                    min -= 0.5;
                    max += 0.5;
                }

                var step = NiceNumber(range / Math.Max(1, maxTicks), round: true);
                if (!double.IsFinite(step) || step <= 0) step = 1;

                var first = Math.Floor(min / step) * step;
                var last = Math.Ceiling(max / step) * step;
                var major = new List<double>();
                var minor = new List<double>();
                var guard = 0;

                for (var value = first; value <= last + step * 0.5 && guard++ < 1000; value += step)
                {
                    if (value >= min - step * 0.001 && value <= max + step * 0.001)
                    {
                        major.Add(NormalizeZero(value));
                    }

                    var half = value + step / 2;
                    if (half >= min && half <= max)
                    {
                        minor.Add(NormalizeZero(half));
                    }
                }

                return new AxisTicks(major, minor, step);
            }

            public string Format(double value)
            {
                var absStep = Math.Abs(step);

                if (absStep >= 1000) return value.ToString("G4", CultureInfo.CurrentCulture);
                if (absStep >= 1) return value.ToString("0.#", CultureInfo.CurrentCulture);

                var decimals = Math.Min(6, Math.Max(1, (int)Math.Ceiling(-Math.Log10(absStep)) + 1));
                return value.ToString("0." + new string('#', decimals), CultureInfo.CurrentCulture);
            }

            static double NiceNumber(double value, bool round)
            {
                if (!double.IsFinite(value) || value <= 0) return 1;

                var exponent = Math.Floor(Math.Log10(value));
                var fraction = value / Math.Pow(10, exponent);
                double niceFraction;

                if (round)
                {
                    if (fraction < 1.5) niceFraction = 1;
                    else if (fraction < 3) niceFraction = 2;
                    else if (fraction < 7) niceFraction = 5;
                    else niceFraction = 10;
                }
                else
                {
                    if (fraction <= 1) niceFraction = 1;
                    else if (fraction <= 2) niceFraction = 2;
                    else if (fraction <= 5) niceFraction = 5;
                    else niceFraction = 10;
                }

                return niceFraction * Math.Pow(10, exponent);
            }

            static double NormalizeZero(double value) => Math.Abs(value) < 1E-12 ? 0 : value;
        }
    }
}
