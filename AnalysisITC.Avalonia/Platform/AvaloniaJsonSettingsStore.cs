using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using AnalysisITC.Core.Application;
using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaJsonSettingsStore : ISettingsStore
    {
        readonly string path;
        readonly string backupPath;
        readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        Dictionary<string, JsonElement> values = new Dictionary<string, JsonElement>();
        bool primaryFileIsValid;

        public AvaloniaJsonSettingsStore(string directory)
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "settings.json");
            backupPath = path + ".bak";
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

            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, values, serializerOptions);
                    stream.Flush(flushToDisk: true);
                }

                if (primaryFileIsValid && File.Exists(path))
                    File.Replace(temporaryPath, path, backupPath);
                else
                    // Startup immediately synchronizes a migrated preference. Do not
                    // replace a recovered backup with the invalid primary file.
                    File.Move(temporaryPath, path, overwrite: true);

                primaryFileIsValid = true;
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    AppEventHandler.PrintAndLog("Could not remove temporary settings file: " + ex.Message);
                }
            }
        }

        void Load()
        {
            primaryFileIsValid = TryLoad(path);
            if (primaryFileIsValid) return;

            if (TryLoad(backupPath))
                AppEventHandler.PrintAndLog("Recovered application settings from " + backupPath);
            else if (File.Exists(path) || File.Exists(backupPath))
                AppEventHandler.PrintAndLog("No valid application settings were found; using defaults.");
        }

        bool TryLoad(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(filePath));
                if (loaded == null) throw new JsonException("Settings must contain a JSON object.");

                values = loaded.ToDictionary(item => item.Key, item => item.Value.Clone());
                return true;
            }
            catch (JsonException ex)
            {
                AppEventHandler.PrintAndLog("Ignoring invalid application settings in " + filePath + ": " + ex.Message);
                return false;
            }
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
