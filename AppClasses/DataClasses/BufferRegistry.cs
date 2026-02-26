using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnalysisITC
{
    /// <summary>
    /// Loads buffer thermodynamic/acid-base properties from JSON and provides lookup by id/alias/legacy enum.
    /// Convention: all thermochemistry in the JSON is for the ionization (deprotonation) reaction:
    ///     HL ⇌ H+ + L
    /// Protonation enthalpy is therefore the negative of ionization enthalpy.
    /// </summary>
    public sealed class BufferRegistry
    {
        public const double ReferenceTemperatureK = 298.15;

        public static BufferRegistry Registry { get; set; } = null;

        public BufferRegistryModel Model { get; }
        private readonly Dictionary<string, BufferSpec> _byId;
        private readonly Dictionary<string, BufferSpec> _byAlias;
        private readonly Dictionary<int, BufferSpec> _byLegacyEnum;

        private BufferRegistry(BufferRegistryModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));

            _byId = new Dictionary<string, BufferSpec>(StringComparer.OrdinalIgnoreCase);
            _byAlias = new Dictionary<string, BufferSpec>(StringComparer.OrdinalIgnoreCase);
            _byLegacyEnum = new Dictionary<int, BufferSpec>();

            foreach (var b in model.Buffers ?? Enumerable.Empty<BufferSpec>())
            {
                if (string.IsNullOrWhiteSpace(b.Id)) continue;

                _byId[b.Id] = b;

                if (b.Aliases != null)
                {
                    foreach (var a in b.Aliases.Where(s => !string.IsNullOrWhiteSpace(s)))
                        _byAlias[a.Trim()] = b;
                }

                if (b.LegacyEnum.HasValue)
                    _byLegacyEnum[b.LegacyEnum.Value] = b;
            }
        }

        public static BufferRegistry LoadFromFile(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath)) throw new ArgumentException("Path is null/empty.", nameof(jsonPath));
            using var fs = File.OpenRead(jsonPath);
            return LoadFromStream(fs);
        }

        public static BufferRegistry LoadFromStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var model = JsonSerializer.Deserialize<BufferRegistryModel>(stream, options);
            if (model == null) throw new InvalidDataException("Failed to parse buffer registry JSON.");

            if (model.SchemaVersion <= 0)
                throw new InvalidDataException("Invalid or missing schemaVersion in buffer registry JSON.");

            return new BufferRegistry(model);
        }

        /// <summary>
        /// Resolve a buffer by id or alias (case-insensitive).
        /// Returns null if not found.
        /// </summary>
        public BufferSpec Resolve(string idOrAlias)
        {
            if (string.IsNullOrWhiteSpace(idOrAlias)) return null;

            if (_byId.TryGetValue(idOrAlias.Trim(), out var b)) return b;
            if (_byAlias.TryGetValue(idOrAlias.Trim(), out b)) return b;

            return null;
        }

        public BufferSpec GetById(string id) => Resolve(id);

        public BufferSpec GetByLegacyEnum(int legacyEnumValue)
        {
            _byLegacyEnum.TryGetValue(legacyEnumValue, out var b);
            return b;
        }

        /// <summary>
        /// Returns the index of the pKa step that is "active" at a given pH.
        /// </summary>
        public static int GetEffectiveStepIndex(BufferSpec buffer, double pH)
        {
            if (buffer?.PKa == null || buffer.PKa.Length == 0) return 0;

            int best = 0;
            double bestDist = double.PositiveInfinity;

            for (int i = 0; i < buffer.PKa.Length; i++)
            {
                var dist = Math.Abs(buffer.PKa[i] - pH);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }

            return best;
        }

        public static int GetAcidCharge(BufferSpec buffer, double pH)
        {
            if (buffer?.AcidCharge == null || buffer.AcidCharge.Length == 0) return 0;
            var idx = GetEffectiveStepIndex(buffer, pH);
            idx = Math.Min(idx, buffer.AcidCharge.Length - 1);
            return buffer.AcidCharge[idx];
        }

        /// <summary>
        /// Ionization (deprotonation) enthalpy ΔH° for the step chosen at the provided pH.
        /// Returns 0 if missing.
        /// </summary>
        public static double GetIonizationEnthalpy(BufferSpec buffer, double temperatureK, double pH)
        {
            if (buffer?.ThermoSteps == null || buffer.ThermoSteps.Length == 0) return 0.0;

            var idx = GetEffectiveStepIndex(buffer, pH);
            idx = Math.Min(idx, buffer.ThermoSteps.Length - 1);

            var step = buffer.ThermoSteps[idx];
            if (step == null) return 0.0;

            var dH298 = step.DH298_Jmol;
            var dCp = step.DCp_JmolK;

            return dH298 + dCp * (temperatureK - ReferenceTemperatureK);
        }

        /// <summary>
        /// Protonation enthalpy for the buffer step at the provided pH.
        /// Convention: ΔH_protonation = -ΔH_ionization.
        /// </summary>
        public static double GetProtonationEnthalpy(BufferSpec buffer, double temperatureK, double pH)
            => -GetIonizationEnthalpy(buffer, temperatureK, pH);
    }

    public sealed class BufferRegistryModel
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("convention")]
        public string Convention { get; set; } // expected: "ionization"

        [JsonPropertyName("referenceTemperature_K")]
        public double ReferenceTemperature_K { get; set; } = BufferRegistry.ReferenceTemperatureK;

        [JsonPropertyName("buffers")]
        public BufferSpec[] Buffers { get; set; }
    }

    public sealed class BufferSpec
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("enum")]
        public string EnumName { get; set; }

        [JsonPropertyName("legacyEnum")]
        public int? LegacyEnum { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("aliases")]
        public string[] Aliases { get; set; }

        [JsonPropertyName("pKa")]
        public double[] PKa { get; set; }

        [JsonPropertyName("dPka_dT")]
        public double[] DPka_dT { get; set; }

        [JsonPropertyName("acidCharge")]
        public int[] AcidCharge { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("thermoSteps")]
        public BufferThermoStep[] ThermoSteps { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("composition")]
        public BufferMixture[] Composition { get; set; }

        public override string ToString() => $"{Name} ({Id})";
    }

    public sealed class BufferThermoStep
    {
        [JsonPropertyName("dH298_Jmol")]
        public double DH298_Jmol { get; set; }

        [JsonPropertyName("dCp_JmolK")]
        public double DCp_JmolK { get; set; }
    }

    /// <summary>
    /// Optional: describes composite buffers (e.g., PBS/TBS).
    /// BaseBufferId points to the parent buffer.
    /// stepIndex selects which pKa/thermo step is considered "active" by default.
    /// </summary>
    public sealed class BufferMixture
    {
        [JsonPropertyName("attributeKey")]
        public string AttributeKey { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("conc_M")]
        public double Concentration { get; set; }

        [JsonPropertyName("ph")]
        public double PH { get; set; }
    }
}