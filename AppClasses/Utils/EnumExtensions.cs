using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Utilities
{
    public static partial class Extensions
    {
        private static Random rng = new Random();

        public static string GetEnumDescription(this Enum value)
        {
            // Get the Description attribute value for the enum value
            FieldInfo fi = value.GetType().GetField(value.ToString());
            try
            {
                DescriptionAttribute[] attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                if (attributes.Length > 0)
                    return attributes[0].Description;
                else
                    return value.ToString();
            }
            catch
            {
                return value.ToString();
            }
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

        public static AttributeKeyAttribute GetProperties(this AttributeKey value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(AttributeKeyAttribute), false).FirstOrDefault() as AttributeKeyAttribute;

            return attribute;
        }

        public static ITCInstrumentAttribute GetProperties(this ITCInstrument value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ITCInstrumentAttribute), false).FirstOrDefault() as ITCInstrumentAttribute;

            return attribute;
        }

        public static ConcentrationUnitAttribute GetProperties(this ConcentrationUnit value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ConcentrationUnitAttribute), false).FirstOrDefault() as ConcentrationUnitAttribute;

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

        public static ExportTypeAttribute GetProperties(this ExportType value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ExportTypeAttribute), false).FirstOrDefault() as ExportTypeAttribute;

            return attribute;
        }

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

        public static string ToReadableString(this TimeSpan span)
        {
            string formatted = string.Format("{0}{1}{2}{3}",
                span.Duration().Days > 0 ? string.Format("{0:0} day{1}, ", span.Days, span.Days == 1 ? string.Empty : "s") : string.Empty,
                span.Duration().Hours > 0 ? string.Format("{0:0} hour{1}, ", span.Hours, span.Hours == 1 ? string.Empty : "s") : string.Empty,
                span.Duration().Minutes > 0 ? string.Format("{0:0} minute{1}, ", span.Minutes, span.Minutes == 1 ? string.Empty : "s") : string.Empty,
                span.Duration().Seconds > 0 ? string.Format("{0:0} second{1}", span.Seconds, span.Seconds == 1 ? string.Empty : "s") : string.Empty);

            if (formatted.EndsWith(", ")) formatted = formatted.Substring(0, formatted.Length - 2);

            if (string.IsNullOrEmpty(formatted)) formatted = "0 seconds";

            return formatted;
        }

        public static string GetName(this ConcentrationUnit value)
        {
            return value.GetProperties().Name;
        }

        /// <summary>
        /// Factor to from Molar to the current unit (eg. 1 for 'M' and 1000 for 'mM')
        /// </summary>
        public static double GetMod(this ConcentrationUnit value)
        {
            return value.GetProperties().Mod;
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
