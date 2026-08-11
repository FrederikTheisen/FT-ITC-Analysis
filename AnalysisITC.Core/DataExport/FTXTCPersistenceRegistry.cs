using System;
using System.Collections.Generic;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Core.Export
{
    /// <summary>
    /// Stable identifiers used by FTXTC.  These strings are storage API and must
    /// never be replaced with enum ordinals, type names, or Enum.ToString().
    /// </summary>
    internal static class FtxtcWireIds
    {
        static readonly IReadOnlyDictionary<AnalysisModel, string> Models = new Dictionary<AnalysisModel, string>
        {
            [AnalysisModel.OneSetOfSites] = "one-set-of-sites",
            [AnalysisModel.TwoSetsOfSites] = "two-sets-of-sites",
            [AnalysisModel.SequentialBindingSites] = "sequential-binding-sites",
            [AnalysisModel.Dissociation] = "dissociation",
            [AnalysisModel.CompetitiveBinding] = "competitive-binding",
            [AnalysisModel.PeptideProlineIsomerization] = "peptide-proline-isomerization",
            [AnalysisModel.TwoCompetingSites] = "two-competing-sites",
            [AnalysisModel.OneSetOfSitesSyringeUncertainty] = "one-set-of-sites-syringe-uncertainty",
        };

        static readonly IReadOnlyDictionary<ParameterType, string> Parameters = new Dictionary<ParameterType, string>
        {
            [ParameterType.Nvalue1] = "stoichiometry-1", [ParameterType.Nvalue2] = "stoichiometry-2",
            [ParameterType.Enthalpy1] = "enthalpy-1", [ParameterType.Enthalpy2] = "enthalpy-2",
            [ParameterType.Affinity1] = "affinity-log10-1", [ParameterType.Affinity2] = "affinity-log10-2",
            [ParameterType.Offset] = "offset", [ParameterType.HeatCapacity1] = "heat-capacity-1",
            [ParameterType.HeatCapacity2] = "heat-capacity-2", [ParameterType.Gibbs1] = "gibbs-1",
            [ParameterType.Gibbs2] = "gibbs-2", [ParameterType.Entropy1] = "entropy-1",
            [ParameterType.Entropy2] = "entropy-2", [ParameterType.EntropyContribution1] = "entropy-contribution-1",
            [ParameterType.EntropyContribution2] = "entropy-contribution-2", [ParameterType.IsomerizationRate] = "isomerization-rate",
            [ParameterType.IsomerizationEquilibriumConstant] = "isomerization-equilibrium-constant",
            [ParameterType.CisIsomerPopulationPercentage] = "cis-isomer-population-percent",
            [ParameterType.ApparentAffinity] = "apparent-affinity-log10",
        };

        static readonly IReadOnlyDictionary<AttributeKey, string> Attributes = new Dictionary<AttributeKey, string>
        {
            [AttributeKey.Null] = "none", [AttributeKey.PreboundLigandConc] = "prebound-ligand-concentration",
            [AttributeKey.PreboundLigandAffinity] = "prebound-ligand-affinity", [AttributeKey.PreboundLigandEnthalpy] = "prebound-ligand-enthalpy",
            [AttributeKey.PeptideInCell] = "peptide-in-cell", [AttributeKey.Buffer] = "buffer",
            [AttributeKey.Salt] = "salt", [AttributeKey.IonicStrength] = "ionic-strength",
            [AttributeKey.EquilibriumConstant] = "equilibrium-constant", [AttributeKey.Percentage] = "percentage",
            [AttributeKey.LockDuplicateParameter] = "lock-duplicate-parameter", [AttributeKey.BufferSubtraction] = "buffer-subtraction",
            [AttributeKey.NumberOfSites1] = "number-of-sites-1", [AttributeKey.UseSyringeActiveFraction] = "use-syringe-active-fraction",
            [AttributeKey.NumberOfSites2] = "number-of-sites-2", [AttributeKey.Species] = "species",
        };

        // Buffer ids mirror Resources/Buffers.json and are part of the storage API.
        static readonly IReadOnlyDictionary<Buffer, string> Buffers = new Dictionary<Buffer, string>
        {
            [Buffer.Null] = "null", [Buffer.Hepes] = "hepes",
            [Buffer.SodiumPhosphate] = "sodium_phosphate", [Buffer.PotassiumPhosphate] = "potassium_phosphate",
            [Buffer.Tris] = "tris", [Buffer.Maleate] = "maleate", [Buffer.Chloroacetate] = "chloroacetate",
            [Buffer.Citrate] = "citrate", [Buffer.Formate] = "formate", [Buffer.Succinate] = "succinate",
            [Buffer.Benzoate] = "benzoate", [Buffer.Acetate] = "acetate", [Buffer.Propionate] = "propionate",
            [Buffer.Pyridine] = "pyridine", [Buffer.Piperazine] = "piperazine", [Buffer.MES] = "mes",
            [Buffer.Carbonate] = "carbonate", [Buffer.BisTris] = "bis_tris", [Buffer.ADA] = "ada",
            [Buffer.PIPES] = "pipes", [Buffer.ACES] = "aces", [Buffer.BES] = "bes", [Buffer.MOPS] = "mops",
            [Buffer.TES] = "tes", [Buffer.Tricine] = "tricine", [Buffer.Bicine] = "bicine", [Buffer.TAPS] = "taps",
            [Buffer.Ethanolamine] = "ethanolamine", [Buffer.CHES] = "ches", [Buffer.CAPS] = "caps",
            [Buffer.Methylamine] = "methylamine", [Buffer.Piperidine] = "piperidine", [Buffer.TAPSO] = "tapso",
            [Buffer.PBS] = "pbs", [Buffer.TBS] = "tbs", [Buffer.Histidine] = "histidine",
            [Buffer.Imidazole] = "imidazole",
        };

        static readonly IReadOnlyDictionary<Salt, string> Salts = new Dictionary<Salt, string>
        {
            [Salt.NaCl] = "sodium-chloride", [Salt.NaF] = "sodium-fluoride",
            [Salt.Na2SO4] = "sodium-sulfate", [Salt.K2SO4] = "potassium-sulfate",
            [Salt.MgSO4] = "magnesium-sulfate", [Salt.KCl] = "potassium-chloride",
            [Salt.MgCl2] = "magnesium-chloride", [Salt.KI] = "potassium-iodide",
            [Salt.CaCl2] = "calcium-chloride",
        };

        static readonly IReadOnlyDictionary<BufferSubtractionMethod, string> BufferSubtractionMethods =
            new Dictionary<BufferSubtractionMethod, string>
            {
                [BufferSubtractionMethod.MatchedInjection] = "matched-injection",
                [BufferSubtractionMethod.Linear] = "linear",
                [BufferSubtractionMethod.ExponentialDecay] = "exponential-decay",
            };

        static readonly IReadOnlyDictionary<ExperimentSpeciesLocation, string> SpeciesLocations =
            new Dictionary<ExperimentSpeciesLocation, string>
            {
                [ExperimentSpeciesLocation.Cell] = "cell",
                [ExperimentSpeciesLocation.Syringe] = "syringe",
            };

        static readonly IReadOnlyDictionary<BaselineInterpolatorTypes, string> Processors =
            new Dictionary<BaselineInterpolatorTypes, string>
            {
                [BaselineInterpolatorTypes.None] = "none", [BaselineInterpolatorTypes.Spline] = "spline",
                [BaselineInterpolatorTypes.ASL] = "asl", [BaselineInterpolatorTypes.Polynomial] = "polynomial",
                [BaselineInterpolatorTypes.Segmented] = "segmented",
            };

        internal static string Model(AnalysisModel value) => Get(Models, value, "model");
        internal static AnalysisModel Model(string value) => Get(Models, value, "model");
        internal static string Parameter(ParameterType value) => Get(Parameters, value, "parameter");
        internal static ParameterType Parameter(string value) => Get(Parameters, value, "parameter");
        internal static string Attribute(AttributeKey value) => Get(Attributes, value, "attribute");
        internal static AttributeKey Attribute(string value) => Get(Attributes, value, "attribute");
        internal static string Processor(BaselineInterpolatorTypes value) => Get(Processors, value, "processor");
        internal static BaselineInterpolatorTypes Processor(string value) => Get(Processors, value, "processor");

        internal static bool UsesAttributeValueId(AttributeKey key) => key == AttributeKey.Buffer
            || key == AttributeKey.Salt
            || key == AttributeKey.BufferSubtraction
            || key == AttributeKey.Species;

        internal static bool UsesNumericAttributeIntValue(AttributeKey key) =>
            key == AttributeKey.NumberOfSites1 || key == AttributeKey.NumberOfSites2;

        internal static string AttributeValueId(AttributeKey key, int intValue)
        {
            switch (key)
            {
                case AttributeKey.Buffer: return Get(Buffers, (Buffer)intValue, "buffer");
                case AttributeKey.Salt: return Get(Salts, (Salt)intValue, "salt");
                case AttributeKey.BufferSubtraction: return Get(BufferSubtractionMethods, (BufferSubtractionMethod)intValue, "buffer subtraction method");
                case AttributeKey.Species: return Get(SpeciesLocations, (ExperimentSpeciesLocation)intValue, "species location");
                default: return null;
            }
        }

        internal static int AttributeIntValue(AttributeKey key, string valueId, int? numericValue)
        {
            switch (key)
            {
                case AttributeKey.Buffer: return (int)Get(Buffers, valueId, "buffer");
                case AttributeKey.Salt: return (int)Get(Salts, valueId, "salt");
                case AttributeKey.BufferSubtraction: return (int)Get(BufferSubtractionMethods, valueId, "buffer subtraction method");
                case AttributeKey.Species: return (int)Get(SpeciesLocations, valueId, "species location");
                default: return numericValue ?? 0;
            }
        }

        static string Get<T>(IReadOnlyDictionary<T, string> table, T key, string kind)
        {
            if (table.TryGetValue(key, out var value)) return value;
            throw new NotSupportedException($"No FTXTC wire id is registered for {kind} '{key}'.");
        }

        static T Get<T>(IReadOnlyDictionary<T, string> table, string value, string kind)
        {
            foreach (var item in table)
                if (string.Equals(item.Value, value, StringComparison.Ordinal)) return item.Key;
            throw new NotSupportedException($"Unsupported FTXTC {kind} wire id '{value}'.");
        }
    }

    /// <summary>Persistence-only model construction, intentionally independent of UI factories.</summary>
    internal static class FtxtcModelRegistry
    {
        internal static Model Create(string wireId, ExperimentData data)
        {
            switch (FtxtcWireIds.Model(wireId))
            {
                case AnalysisModel.OneSetOfSites: return new OneSetOfSites(data);
                case AnalysisModel.TwoSetsOfSites: return new TwoSetsOfSites(data);
                case AnalysisModel.SequentialBindingSites:
                case AnalysisModel.Dissociation: return new Dissociation(data);
                case AnalysisModel.CompetitiveBinding: return new CompetitiveBinding(data);
                case AnalysisModel.PeptideProlineIsomerization: return new OneSiteIsomerization(data);
                case AnalysisModel.TwoCompetingSites: return new TwoCompetingSites(data);
                case AnalysisModel.OneSetOfSitesSyringeUncertainty: return new OneSetOfSitesSyringeUncertainty(data);
                default: throw new NotSupportedException($"Unsupported FTXTC model '{wireId}'.");
            }
        }

        internal static IReadOnlyCollection<AnalysisModel> SupportedModels => new[]
        {
            AnalysisModel.OneSetOfSites, AnalysisModel.TwoSetsOfSites, AnalysisModel.SequentialBindingSites,
            AnalysisModel.Dissociation, AnalysisModel.CompetitiveBinding, AnalysisModel.PeptideProlineIsomerization,
            AnalysisModel.TwoCompetingSites, AnalysisModel.OneSetOfSitesSyringeUncertainty,
        };
    }
}
