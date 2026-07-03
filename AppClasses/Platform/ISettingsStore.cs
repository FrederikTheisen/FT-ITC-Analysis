using System;


namespace AnalysisITC.Platform
{
    public interface ISettingsStore
    {
        int Count { get; }

        bool Contains(string key);

        bool GetBool(string key, bool defaultValue = false);
        int GetInt(string key, int defaultValue = 0);
        double GetDouble(string key, double defaultValue = 0);
        string GetString(string key, string defaultValue = null);
        double[] GetDoubleArray(string key, double[] defaultValue = null);
        string[] GetStringArray(string key, string[] defaultValue = null);

        void SetBool(string key, bool value);
        void SetInt(string key, int value);
        void SetDouble(string key, double value);
        void SetString(string key, string value);
        void SetDoubleArray(string key, double[] value);
        void SetStringArray(string key, string[] value);

        void Synchronize();
    }
}
