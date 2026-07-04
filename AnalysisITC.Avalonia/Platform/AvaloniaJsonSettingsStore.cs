using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaJsonSettingsStore : ISettingsStore
    {
        readonly string path;
        readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        Dictionary<string, JsonElement> values = new Dictionary<string, JsonElement>();

        public AvaloniaJsonSettingsStore(string directory)
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "settings.json");
            Load();
        }

        public int Count => values.Count;

        public bool Contains(string key) => key != null && values.ContainsKey(key);

        public bool GetBool(string key, bool defaultValue = false)
        {
            return TryGet(key, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            return TryGet(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
                ? result
                : defaultValue;
        }

        public double GetDouble(string key, double defaultValue = 0)
        {
            return TryGet(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result)
                ? result
                : defaultValue;
        }

        public string? GetString(string key, string? defaultValue = null)
        {
            return TryGet(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? defaultValue
                : defaultValue;
        }

        public double[]? GetDoubleArray(string key, double[]? defaultValue = null)
        {
            if (!TryGet(key, out var value) || value.ValueKind != JsonValueKind.Array) return defaultValue;

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Number)
                .Select(item => item.TryGetDouble(out var result) ? result : 0)
                .ToArray();
        }

        public string[]? GetStringArray(string key, string[]? defaultValue = null)
        {
            if (!TryGet(key, out var value) || value.ValueKind != JsonValueKind.Array) return defaultValue;

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => item != null)
                .Select(item => item!)
                .ToArray();
        }

        public void SetBool(string key, bool value) => Set(key, value);

        public void SetInt(string key, int value) => Set(key, value);

        public void SetDouble(string key, double value) => Set(key, value);

        public void SetString(string key, string? value)
        {
            if (value == null) values.Remove(key);
            else Set(key, value);
        }

        public void SetDoubleArray(string key, double[] value)
        {
            if (value == null) values.Remove(key);
            else Set(key, value);
        }

        public void SetStringArray(string key, string[] value)
        {
            if (value == null) values.Remove(key);
            else Set(key, value);
        }

        public void Synchronize()
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(values, serializerOptions);
            File.WriteAllText(path, json);
        }

        void Load()
        {
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            values = loaded?.ToDictionary(item => item.Key, item => item.Value.Clone())
                ?? new Dictionary<string, JsonElement>();
        }

        bool TryGet(string key, out JsonElement value)
        {
            if (key == null)
            {
                value = default;
                return false;
            }

            return values.TryGetValue(key, out value);
        }

        void Set<T>(string key, T value)
        {
            if (key == null) return;

            values[key] = JsonSerializer.SerializeToElement(value);
        }
    }
}
