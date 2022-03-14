using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC;
using DataReaders;

namespace Utilities
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
    }
        
}
