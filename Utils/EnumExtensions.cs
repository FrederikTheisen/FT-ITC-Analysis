using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC;
using CoreGraphics;
using DataReaders;

namespace AnalysisITC
{
    public static class Extensions
    {
        public static ITCFormatAttribute GetProperties(this ITCDataFormat value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ITCFormatAttribute), false).FirstOrDefault() as ITCFormatAttribute;

            return attribute;
        }

        public static EnergyUnitAttribute GetProperties(this EnergyUnit value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(EnergyUnitAttribute), false).FirstOrDefault() as EnergyUnitAttribute;

            return attribute;
        }

        public static string GetUnit(this EnergyUnit value) => value.GetProperties().Unit;

        public static CGRect WithMargin(this CGRect box, CGEdgeMargin margin, float mod = 1)
        {
            return margin.BoxWithMargin(box, mod);
        }

        public static CGRect WithMargin(this CGRect box, CGEdgeMargin margin)
        {
            return margin.BoxWithMargin(box);
        }

        public static CGSize ScaleBy(this CGSize box, float value)
        {
            return new CGSize(box.Width * value, box.Height * value);
        }

        public static CGSize AbsoluteValueSize(this CGSize box)
        {
            return new CGSize(Math.Abs(box.Width), Math.Abs(box.Height));
        }
    }

    public static class FWEMath
    {
        public static FloatWithError Log(FloatWithError number)
        {
            return new FloatWithError(Math.Log(number.Value), number.SD / number.Value);
        }
    }

    public struct CGEdgeMargin
    {
        public nfloat Left { get; private set; }
        public nfloat Right { get; private set; }
        public nfloat Top { get; private set; }
        public nfloat Bottom { get; private set; }

        public nfloat Width => Left + Right;
        public nfloat Height => Top + Bottom;

        public CGEdgeMargin(float left, float right, float top, float bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        public CGRect BoxWithMargin(CGRect box, float mod = 1)
        {
            return new CGRect(box.X - Left * mod, box.Y - Bottom * mod, box.Width + Width * mod, box.Height + Height * mod);
        }

        public CGRect BoxWithMargin(CGRect box)
        {
            return new CGRect(box.X - Left, box.Y - Bottom, box.Width + Width, box.Height + Height);
        }
    }

    public static class NumberExtensions
    {
        public static Energy Average(this IEnumerable<Energy> list)
        {
            double sum = list.Sum(o => o.FloatWithError.Value);
            double sd = list.Sum(o => o.FloatWithError.SD);
            int count = list.Count();

            return new Energy(new FloatWithError(sum / count, sd / count));
        }

        public static Energy Min(this IEnumerable<Energy> list)
        {
            var v = list.Min(o => o.FloatWithError);

            return new Energy(v);
        }

        public static Energy Max(this IEnumerable<Energy> list)
        {
            var v = list.Max(o => o.FloatWithError);

            return new Energy(v);
        }

        public static Energy Min(this IEnumerable<DataPoint> list, Func<DataPoint, Energy> selector)
        {
            var v = list.Min(o => o.Power);

            return new Energy(v);
        }

        public static Energy Max(this IEnumerable<DataPoint> list, Func<DataPoint, Energy> selector)
        {
            var v = list.Max(o => o.Power);

            return new Energy(v);
        }

        public static IEnumerable<Energy> OrderBy(this IEnumerable<Energy> list, Func<Energy, Energy> selector)
        {
            return list.OrderBy(o => o.FloatWithError.Value);
        }
    }
        
}
