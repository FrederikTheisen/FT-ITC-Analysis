using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using AnalysisITC;
using CoreGraphics;
using DataReaders;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.Analysis2.Models;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC
{
    public static partial class Extensions
    {
        private static Random rng = new Random();

        public static string GetEnumDescription(this Enum value)
        {
            // Get the Description attribute value for the enum value
            FieldInfo fi = value.GetType().GetField(value.ToString());
            DescriptionAttribute[] attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

            if (attributes.Length > 0)
                return attributes[0].Description;
            else
                return value.ToString();
        }

        public static string Description(this SolverAlgorithm alg) => GetEnumDescription(alg);
        public static string Description(this ErrorEstimationMethod alg) => GetEnumDescription(alg);

        public static SolverAlgorithmAttribute GetProperties(this SolverAlgorithm value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(SolverAlgorithmAttribute), false).FirstOrDefault() as SolverAlgorithmAttribute;

            return attribute;
        }

        public static ITCFormatAttribute GetProperties(this ITCDataFormat value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ITCFormatAttribute), false).FirstOrDefault() as ITCFormatAttribute;

            return attribute;
        }

        public static AnalysisModelAttribute GetProperties(this AnalysisModel value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(AnalysisModelAttribute), false).FirstOrDefault() as AnalysisModelAttribute;

            return attribute;
        }

        public static EnergyUnitAttribute GetProperties(this EnergyUnit value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(EnergyUnitAttribute), false).FirstOrDefault() as EnergyUnitAttribute;

            return attribute;
        }

        public static ModelOptionKeyAttribute GetProperties(this ModelOptionKey value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ModelOptionKeyAttribute), false).FirstOrDefault() as ModelOptionKeyAttribute;

            return attribute;
        }

        public static ITCInstrumentAttribute GetProperties(this ITCInstrument value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ITCInstrumentAttribute), false).FirstOrDefault() as ITCInstrumentAttribute;

            return attribute;
        }

        public static ParameterTypeAttribute GetProperties(this ParameterType value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ParameterTypeAttribute), false).FirstOrDefault() as ParameterTypeAttribute;

            return attribute;
        }

        public static TimeUnitAttribute GetProperties(this TimeUnit value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(TimeUnitAttribute), false).FirstOrDefault() as TimeUnitAttribute;

            return attribute;
        }

        public static FeedbackModeAttribute GetProperties(this FeedbackMode value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(FeedbackModeAttribute), false).FirstOrDefault() as FeedbackModeAttribute;

            return attribute;
        }

        

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

        public static CGPoint Add(this CGPoint p, float x, float y) => new CGPoint(p.X + x, p.Y + y);

        public static CGPoint Add(this CGPoint p1, CGPoint p2) => new CGPoint(p1.X + p2.X, p1.Y + p2.Y);

        public static CGPoint Subtract(this CGPoint p, float x, float y) => new CGPoint(p.X - x, p.Y - y);

        public static CGPoint Subtract(this CGPoint p1, CGPoint p2) => new CGPoint(p1.X - p2.X, p1.Y - p2.Y);

        public static void Shuffle<T>(this IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
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

    public class FeedbackModeAttribute : Attribute
    {
        public string Name { get; set; }

        public FeedbackModeAttribute(string name)
        {
            Name = name;
        }
    }

}
