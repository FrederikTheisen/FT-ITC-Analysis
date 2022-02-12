using System;
using System.Linq;
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
}
