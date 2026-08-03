using System;
using System.Linq;
using AnalysisITC.Platform;
using Foundation;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Platform.MacOS
{
    public sealed class MacUserDefaultsSettingsStore : ISettingsStore
    {
        NSUserDefaults Storage => NSUserDefaults.StandardUserDefaults;

        public int Count => (int)Storage.ToDictionary().Count;

        public bool Contains(string key)
        {
            if (key == null) return false;

            return Storage.ToDictionary().ContainsKey(NSObject.FromObject(key));
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            return Contains(key) ? Storage.BoolForKey(key) : defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            return Contains(key) ? (int)Storage.IntForKey(key) : defaultValue;
        }

        public double GetDouble(string key, double defaultValue = 0)
        {
            return Contains(key) ? Storage.DoubleForKey(key) : defaultValue;
        }

        public string GetString(string key, string defaultValue = null)
        {
            if (!Contains(key)) return defaultValue;

            var value = Storage.ToDictionary()[NSObject.FromObject(key)];
            return StringFromObject(value) ?? defaultValue;
        }

        public double[] GetDoubleArray(string key, double[] defaultValue = null)
        {
            if (!Contains(key)) return defaultValue;

            var array = Storage.ArrayForKey(key);
            if (array == null) return defaultValue;

            var result = new double[array.Count()];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = array.GetValue(i) is NSNumber number
                    ? number.DoubleValue
                    : 0;
            }

            return result;
        }

        public string[] GetStringArray(string key, string[] defaultValue = null)
        {
            if (!Contains(key)) return defaultValue;

            var array = Storage.ArrayForKey(key);
            if (array == null) return defaultValue;

            var result = new string[array.Count()];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = StringFromObject(array.GetValue(i));
            }

            return result;
        }

        public void SetBool(string key, bool value) => Storage.SetBool(value, key);

        public void SetInt(string key, int value) => Storage.SetInt(value, key);

        public void SetDouble(string key, double value) => Storage.SetDouble(value, key);

        public void SetString(string key, string value)
        {
            if (value == null) Storage.RemoveObject(key);
            else Storage.SetString(value, key);
        }

        public void SetDoubleArray(string key, double[] value)
        {
            if (value == null)
            {
                Storage.RemoveObject(key);
                return;
            }

            var array = new NSMutableArray();
            foreach (var item in value) array.Add(new NSNumber(item));

            Storage.SetValueForKey(array, new NSString(key));
        }

        public void SetStringArray(string key, string[] value)
        {
            if (value == null)
            {
                Storage.RemoveObject(key);
                return;
            }

            var array = new NSMutableArray();
            foreach (var item in value.Where(item => item != null)) array.Add(new NSString(item));

            Storage.SetValueForKey(array, new NSString(key));
        }

        public void Synchronize() => Storage.Synchronize();

        static string StringFromObject(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case NSUrl url:
                    return url.Path ?? url.ToString();
                case NSString str:
                    return str.ToString();
                case NSData data:
                    return StringFromData(data);
                case NSObject:
                    // NSObject.ToString() exposes diagnostic descriptions such
                    // as "{length = …, bytes = …}" for archived preferences.
                    // That is never a valid stored string.
                    return null;
                default:
                    return value.ToString();
            }
        }

        static string StringFromData(NSData data)
        {
            if (data == null || data.Length == 0) return null;

            // Older builds stored recent document URLs as bookmark data.
            try
            {
                bool isStale;
                NSError error;
                using (var url = NSUrl.FromBookmarkData(
                           data,
                           NSUrlBookmarkResolutionOptions.WithoutUI
                           | NSUrlBookmarkResolutionOptions.WithoutMounting,
                           null,
                           out isStale,
                           out error))
                {
                    if (url != null)
                        return url.Path ?? url.AbsoluteString;
                }
            }
            catch
            {
                // Try the older keyed-archive representation next.
            }

            try
            {
#pragma warning disable CS0618
                var archivedObject =
                    NSKeyedUnarchiver.UnarchiveObject(data);
#pragma warning restore CS0618
                if (archivedObject != null
                    && !(archivedObject is NSData))
                {
                    return StringFromObject(archivedObject);
                }
            }
            catch
            {
                // Invalid or unrelated binary data is not a string setting.
            }

            return null;
        }
    }
}
