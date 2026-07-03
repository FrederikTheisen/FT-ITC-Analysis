using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.Platform
{
    public sealed class InMemorySettingsStore : ISettingsStore
    {
        readonly Dictionary<string, object> values = new Dictionary<string, object>();

        public int Count => values.Count;

        public bool Contains(string key) => key != null && values.ContainsKey(key);

        public bool GetBool(string key, bool defaultValue = false)
        {
            return Contains(key) && values[key] is bool value ? value : defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            return Contains(key) && values[key] is int value ? value : defaultValue;
        }

        public double GetDouble(string key, double defaultValue = 0)
        {
            return Contains(key) && values[key] is double value ? value : defaultValue;
        }

        public string GetString(string key, string defaultValue = null)
        {
            return Contains(key) && values[key] is string value ? value : defaultValue;
        }

        public double[] GetDoubleArray(string key, double[] defaultValue = null)
        {
            if (!Contains(key) || values[key] is not double[] value) return defaultValue;

            return value.ToArray();
        }

        public string[] GetStringArray(string key, string[] defaultValue = null)
        {
            if (!Contains(key) || values[key] is not string[] value) return defaultValue;

            return value.ToArray();
        }

        public void SetBool(string key, bool value) => values[key] = value;

        public void SetInt(string key, int value) => values[key] = value;

        public void SetDouble(string key, double value) => values[key] = value;

        public void SetString(string key, string value)
        {
            if (value == null) values.Remove(key);
            else values[key] = value;
        }

        public void SetDoubleArray(string key, double[] value)
        {
            if (value == null) values.Remove(key);
            else values[key] = value.ToArray();
        }

        public void SetStringArray(string key, string[] value)
        {
            if (value == null) values.Remove(key);
            else values[key] = value.ToArray();
        }

        public void Synchronize()
        {
        }
    }
}
