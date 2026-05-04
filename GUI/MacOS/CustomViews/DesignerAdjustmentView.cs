using System;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
    public enum AdjustmentViewMode
    {
        Analysis,
        Designer
    }

    public interface IDesignerAdjustmentView
    {
        event EventHandler ValueChanged;
    }

    public struct AdjustmentSliderRange
    {
        public double Min { get; }
        public double Max { get; }

        public AdjustmentSliderRange(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }

    public static class AdjustmentSliderHelper
    {
        public static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        public static double FromSliderValue(double sliderValue, AdjustmentSliderRange range)
        {
            var slider = Clamp(sliderValue, 0, 1);

            return range.Min + slider * (range.Max - range.Min);
        }

        public static double ToSliderValue(double value, AdjustmentSliderRange range)
        {
            if (range.Max <= range.Min) return 0;

            return Clamp((value - range.Min) / (range.Max - range.Min), 0, 1);
        }
    }
}
