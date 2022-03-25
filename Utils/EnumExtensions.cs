using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC;
using DataReaders;

namespace AnalysisITC
{
    public static class EnumExtensions
    {
        public static ITCFormatAttribute GetProperties(this ITCDataFormat value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ITCFormatAttribute), false).FirstOrDefault() as ITCFormatAttribute;

            return attribute;
        }
    }

    public static class NumberExtensions
    {
        public static Energy Average(this IEnumerable<Energy> list)
        {
            double sum = list.Sum(o => o.Value.Value);
            double sd = list.Sum(o => o.Value.SD);
            int count = list.Count();

            return new Energy(new FloatWithError(sum / count, sd / count));
        }

        public static Energy Min(this IEnumerable<Energy> list)
        {
            var v = list.Min(o => o.Value);

            return new Energy(v);
        }

        public static Energy Max(this IEnumerable<Energy> list)
        {
            var v = list.Max(o => o.Value);

            return new Energy(v);
        }

        public static Energy Min(this IEnumerable<DataPoint> list, Func<DataPoint, Energy> selector)
        {
            var v = list.Min(o => o.Power.Value.Value);

            return new Energy(v);
        }

        public static Energy Max(this IEnumerable<DataPoint> list, Func<DataPoint, Energy> selector)
        {
            var v = list.Max(o => o.Power.Value.Value);

            return new Energy(v);
        }

        public static IEnumerable<Energy> OrderBy(this IEnumerable<Energy> list, Func<Energy, Energy> selector)
        {
            return list.OrderBy(o => o.Value.Value);
        }
    }
        
}
