using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;

using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Data
{
    public enum AnalysisResultValidity
    {
        Unknown,
        Valid,
        PartialInvalid,
        Invalid
    }

    public sealed class AnalysisResultValidityReport
    {
        public AnalysisResultValidity Status { get; set; }
        public List<string> Reasons { get; set; } = new();

        public bool IsValid => Status == AnalysisResultValidity.Valid;

        public static AnalysisResultValidityReport Valid() => new()
        {
            Status = AnalysisResultValidity.Valid
        };

        public static AnalysisResultValidityReport Invalid(IEnumerable<string> reasons) => new()
        {
            Status = AnalysisResultValidity.Invalid,
            Reasons = reasons?.ToList() ?? new List<string>()
        };

        public static AnalysisResultValidityReport PartialInvalid(IEnumerable<string> reasons) => new()
        {
            Status = AnalysisResultValidity.PartialInvalid,
            Reasons = reasons?.ToList() ?? new List<string>()
        };

        public static AnalysisResultValidityReport Unknown(string reason) => new()
        {
            Status = AnalysisResultValidity.Unknown,
            Reasons = string.IsNullOrWhiteSpace(reason) ? new List<string>() : new List<string> { reason }
        };
    }

    public sealed class AnalysisResultValiditySnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<ExperimentFitInputSnapshot> Experiments { get; set; } = new();

        public static AnalysisResultValiditySnapshot Capture(GlobalSolution solution)
        {
            if (solution?.Model == null) return null;

            var model = solution.Model;

            return new AnalysisResultValiditySnapshot
            {
                Experiments = model.Models?
                    .Select(ExperimentFitInputSnapshot.Capture)
                    .Where(snapshot => snapshot != null)
                    .ToList() ?? new List<ExperimentFitInputSnapshot>()
            };
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static AnalysisResultValiditySnapshot FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            return JsonSerializer.Deserialize<AnalysisResultValiditySnapshot>(json);
        }

        public AnalysisResultValidityReport Compare(GlobalSolution solution)
        {
            if (SchemaVersion != CurrentSchemaVersion)
                return AnalysisResultValidityReport.Unknown($"Unsupported validity snapshot schema: {SchemaVersion}.");

            if (solution?.Model == null)
                return AnalysisResultValidityReport.Unknown("No analysis solution is available.");

            try
            {
                var model = solution.Model;

                // The validity snapshot answers whether the stored analysis still matches
                // the experiment inputs. Later solutions or model settings attached to the
                // same experiments do not invalidate this analysis result.
                var comparison = CompareExperiments(model);

                if (comparison.Reasons.Count == 0)
                    return AnalysisResultValidityReport.Valid();

                return IsPartiallyInvalidResult(solution, comparison)
                    ? AnalysisResultValidityReport.PartialInvalid(comparison.Reasons)
                    : AnalysisResultValidityReport.Invalid(comparison.Reasons);
            }
            catch (Exception ex)
            {
                return AnalysisResultValidityReport.Unknown($"Could not compare validity snapshot: {ex.Message}");
            }
        }

        ExperimentComparisonSummary CompareExperiments(GlobalModel model)
        {
            var summary = new ExperimentComparisonSummary
            {
                MemberCount = Experiments.Count
            };
            var currentModels = model.Models ?? new List<Model>();
            var currentIds = currentModels
                .Where(m => m?.Data != null)
                .Select(m => m.Data.UniqueID)
                .OrderBy(id => id)
                .ToList();
            var storedIds = Experiments
                .Select(e => e.ExperimentID)
                .OrderBy(id => id)
                .ToList();

            if (!storedIds.SequenceEqual(currentIds))
            {
                summary.HasResultLevelInvalidity = true;
                summary.Reasons.Add("Included experiment set changed.");
            }

            foreach (var experiment in Experiments)
            {
                var currentModel = currentModels.FirstOrDefault(m => m?.Data?.UniqueID == experiment.ExperimentID);

                if (currentModel == null)
                {
                    summary.HasResultLevelInvalidity = true;
                    summary.Reasons.Add($"Experiment missing: {experiment.DisplayNameOrID}.");
                    continue;
                }

                if (experiment.Compare(currentModel, summary.Reasons))
                    summary.ModifiedMemberCount++;
            }

            return summary;
        }

        static bool IsPartiallyInvalidResult(GlobalSolution solution, ExperimentComparisonSummary comparison)
        {
            var parameters = solution?.Model?.Parameters;
            var hasGlobalConstraints = parameters?.Constraints?.Any(con => con.Value != VariableConstraint.None) == true;
            var requiresGlobalFitting = parameters?.RequiresGlobalFitting == true || hasGlobalConstraints;

            return !requiresGlobalFitting
                && !comparison.HasResultLevelInvalidity
                && comparison.MemberCount > 1
                && comparison.ModifiedMemberCount > 0
                && comparison.ModifiedMemberCount < comparison.MemberCount;
        }

        internal static bool SameDouble(double stored, double current)
        {
            if (double.IsNaN(stored) && double.IsNaN(current)) return true;
            if (double.IsInfinity(stored) || double.IsInfinity(current)) return stored.Equals(current);

            var scale = Math.Max(1.0, Math.Max(Math.Abs(stored), Math.Abs(current)));
            return Math.Abs(stored - current) <= 1e-12 + 1e-9 * scale;
        }
    }

    sealed class ExperimentComparisonSummary
    {
        public List<string> Reasons { get; } = new();
        public int MemberCount { get; set; }
        public int ModifiedMemberCount { get; set; }
        public bool HasResultLevelInvalidity { get; set; }
    }

    public sealed class ExperimentFitInputSnapshot
    {
        public string ExperimentID { get; set; }
        public string DisplayName { get; set; }
        public double CellConcentration { get; set; }
        public double CellConcentrationSD { get; set; }
        public double SyringeConcentration { get; set; }
        public double SyringeConcentrationSD { get; set; }
        public double CellVolume { get; set; }
        public ExperimentProcessingSnapshot Processing { get; set; }
        public List<ExperimentAttributeSnapshot> Attributes { get; set; } = new();
        public List<InjectionFitInputSnapshot> IncludedInjections { get; set; } = new();
        public List<TandemSegmentSnapshot> Segments { get; set; } = new();

        public string DisplayNameOrID => string.IsNullOrWhiteSpace(DisplayName) ? ExperimentID : DisplayName;

        public static ExperimentFitInputSnapshot Capture(Model model)
        {
            if (model?.Data == null) return null;

            var data = model.Data;

            return new ExperimentFitInputSnapshot
            {
                ExperimentID = data.UniqueID,
                DisplayName = data.Name,
                CellConcentration = data.CellConcentration.Value,
                CellConcentrationSD = data.CellConcentration.SD,
                SyringeConcentration = data.SyringeConcentration.Value,
                SyringeConcentrationSD = data.SyringeConcentration.SD,
                CellVolume = data.CellVolume,
                Processing = ExperimentProcessingSnapshot.Capture(data),
                Attributes = ExperimentAttributeSnapshot.Capture(data.Attributes),
                IncludedInjections = data.Injections?
                    .Where(inj => inj.Include)
                    .Select(InjectionFitInputSnapshot.Capture)
                    .ToList() ?? new List<InjectionFitInputSnapshot>(),
                Segments = data.Segments?
                    .Select(TandemSegmentSnapshot.Capture)
                    .OrderBy(s => s.FirstInjectionID)
                    .ToList() ?? new List<TandemSegmentSnapshot>()
            };
        }

        public bool Compare(Model currentModel, List<string> reasons)
        {
            var data = currentModel.Data;
            var label = DisplayNameOrID;
            var offenses = new List<string>();

            if (!AnalysisResultValiditySnapshot.SameDouble(CellConcentration, data.CellConcentration.Value)
                || !AnalysisResultValiditySnapshot.SameDouble(CellConcentrationSD, data.CellConcentration.SD))
            {
                offenses.Add("cell concentration changed");
            }

            if (!AnalysisResultValiditySnapshot.SameDouble(SyringeConcentration, data.SyringeConcentration.Value)
                || !AnalysisResultValiditySnapshot.SameDouble(SyringeConcentrationSD, data.SyringeConcentration.SD))
            {
                offenses.Add("syringe concentration changed");
            }

            AddOffenseIfDifferent(offenses, CellVolume, data.CellVolume, "cell volume changed");
            var baselineChanged = Processing?.BaselineChangedComparedTo(ExperimentProcessingSnapshot.Capture(data)) ?? false;
            CompareAttributes(Attributes, ExperimentAttributeSnapshot.Capture(data.Attributes), offenses);
            CompareInjections(data, offenses, baselineChanged);
            CompareSegments(data, offenses);

            if (offenses.Count == 0) return false;

            reasons.Add($"{label}: {string.Join("; ", offenses.Distinct())}.");
            return true;
        }

        void CompareInjections(ExperimentData data, List<string> offenses, bool baselineChanged)
        {
            var current = data.Injections?
                .Where(inj => inj.Include)
                .Select(InjectionFitInputSnapshot.Capture)
                .ToList() ?? new List<InjectionFitInputSnapshot>();

            var storedIds = IncludedInjections.Select(i => i.ID).ToList();
            var currentIds = current.Select(i => i.ID).ToList();

            if (!storedIds.SequenceEqual(currentIds))
            {
                offenses.Add("injection inclusion changed");
                return;
            }

            for (int i = 0; i < IncludedInjections.Count; i++)
            {
                IncludedInjections[i].Compare(current[i], offenses, baselineChanged);
            }
        }

        void CompareSegments(ExperimentData data, List<string> offenses)
        {
            var current = data.Segments?
                .Select(TandemSegmentSnapshot.Capture)
                .OrderBy(s => s.FirstInjectionID)
                .ToList() ?? new List<TandemSegmentSnapshot>();

            if (Segments.Count != current.Count)
            {
                offenses.Add("tandem segment layout changed");
                return;
            }

            for (int i = 0; i < Segments.Count; i++)
            {
                Segments[i].Compare(current[i], offenses);
            }
        }

        static void CompareAttributes(
            List<ExperimentAttributeSnapshot> stored,
            List<ExperimentAttributeSnapshot> current,
            List<string> offenses)
        {
            stored ??= new List<ExperimentAttributeSnapshot>();
            current ??= new List<ExperimentAttributeSnapshot>();

            var keys = stored
                .Select(a => a.Key)
                .Concat(current.Select(a => a.Key))
                .Distinct()
                .OrderBy(key => (int)key);

            foreach (var key in keys)
            {
                var storedForKey = stored.Where(a => a.Key == key).ToList();
                var currentForKey = current.Where(a => a.Key == key).ToList();
                var offense = AttributeChangeReason(key);

                if (storedForKey.Count != currentForKey.Count)
                {
                    offenses.Add(offense);
                    continue;
                }

                for (int i = 0; i < storedForKey.Count; i++)
                {
                    if (!storedForKey[i].EquivalentTo(currentForKey[i]))
                    {
                        offenses.Add(offense);
                        break;
                    }
                }
            }
        }

        static string AttributeChangeReason(AttributeKey key)
        {
            return key switch
            {
                AttributeKey.PreboundLigandConc => "ligand concentration attribute changed",
                AttributeKey.BufferSubtraction => "buffer subtraction settings changed",
                _ => "fit-relevant experiment attributes changed"
            };
        }

        static void AddOffenseIfDifferent(List<string> offenses, double stored, double current, string offense)
        {
            if (!AnalysisResultValiditySnapshot.SameDouble(stored, current))
                offenses.Add(offense);
        }
    }

    public sealed class ExperimentProcessingSnapshot
    {
        public BaselineInterpolatorTypes BaselineType { get; set; }
        public int BaselinePointCount { get; set; }
        public double BaselineFirstValue { get; set; }
        public double BaselineLastValue { get; set; }
        public double BaselineValueSum { get; set; }
        public double BaselineValueSumSquares { get; set; }

        public static ExperimentProcessingSnapshot Capture(ExperimentData data)
        {
            var interpolator = data?.Processor?.Interpolator;
            var baseline = interpolator?.Baseline ?? new List<Energy>();

            return new ExperimentProcessingSnapshot
            {
                BaselineType = data?.Processor?.BaselineType ?? BaselineInterpolatorTypes.None,
                BaselinePointCount = baseline.Count,
                BaselineFirstValue = baseline.Count > 0 ? baseline.First().Value : 0,
                BaselineLastValue = baseline.Count > 0 ? baseline.Last().Value : 0,
                BaselineValueSum = baseline.Sum(point => point.Value),
                BaselineValueSumSquares = baseline.Sum(point => point.Value * point.Value)
            };
        }

        public bool BaselineChangedComparedTo(ExperimentProcessingSnapshot current)
        {
            if (current == null) return false;

            return BaselineType != current.BaselineType
                || BaselinePointCount != current.BaselinePointCount
                || !AnalysisResultValiditySnapshot.SameDouble(BaselineFirstValue, current.BaselineFirstValue)
                || !AnalysisResultValiditySnapshot.SameDouble(BaselineLastValue, current.BaselineLastValue)
                || !AnalysisResultValiditySnapshot.SameDouble(BaselineValueSum, current.BaselineValueSum)
                || !AnalysisResultValiditySnapshot.SameDouble(BaselineValueSumSquares, current.BaselineValueSumSquares);
        }
    }

    public sealed class InjectionFitInputSnapshot
    {
        public int ID { get; set; }
        public double Volume { get; set; }
        public double? IntegrationStartDelay { get; set; }
        public double? IntegrationEndOffset { get; set; }
        public double? RawPeakArea { get; set; }
        public double? RawPeakAreaSD { get; set; }
        public double PeakArea { get; set; }
        public double PeakAreaSD { get; set; }
        public double ActualCellConcentration { get; set; }
        public double ActualTitrantConcentration { get; set; }
        public double Ratio { get; set; }

        public static InjectionFitInputSnapshot Capture(InjectionData injection)
        {
            return new InjectionFitInputSnapshot
            {
                ID = injection.ID,
                Volume = injection.Volume,
                IntegrationStartDelay = injection.IntegrationStartDelay,
                IntegrationEndOffset = injection.IntegrationEndOffset,
                RawPeakArea = injection.RawPeakArea.Value,
                RawPeakAreaSD = injection.RawPeakArea.SD,
                PeakArea = injection.PeakArea.Value,
                PeakAreaSD = injection.PeakArea.SD,
                ActualCellConcentration = injection.ActualCellConcentration,
                ActualTitrantConcentration = injection.ActualTitrantConcentration,
                Ratio = injection.Ratio
            };
        }

        public void Compare(InjectionFitInputSnapshot current, List<string> offenses, bool baselineChanged)
        {
            AddOffenseIfDifferent(offenses, Volume, current.Volume, "injection volumes changed");

            var integrationWindowChanged =
                NullableDifferent(IntegrationStartDelay, current.IntegrationStartDelay)
                || NullableDifferent(IntegrationEndOffset, current.IntegrationEndOffset);
            var rawHeatChanged =
                NullableDifferent(RawPeakArea, current.RawPeakArea)
                || NullableDifferent(RawPeakAreaSD, current.RawPeakAreaSD);
            var correctedHeatChanged =
                !AnalysisResultValiditySnapshot.SameDouble(PeakArea, current.PeakArea)
                || !AnalysisResultValiditySnapshot.SameDouble(PeakAreaSD, current.PeakAreaSD);

            if (rawHeatChanged || correctedHeatChanged)
            {
                var bufferSubtractionOnlyChange =
                    correctedHeatChanged
                    && !rawHeatChanged
                    && offenses.Contains("buffer subtraction settings changed");

                if (integrationWindowChanged && rawHeatChanged)
                    offenses.Add("integration windows changed");
                else if (baselineChanged && rawHeatChanged)
                    offenses.Add("baseline correction changed");
                else if (!bufferSubtractionOnlyChange)
                    offenses.Add("processed heat values changed");
            }

            if (!AnalysisResultValiditySnapshot.SameDouble(ActualCellConcentration, current.ActualCellConcentration)
                || !AnalysisResultValiditySnapshot.SameDouble(ActualTitrantConcentration, current.ActualTitrantConcentration)
                || !AnalysisResultValiditySnapshot.SameDouble(Ratio, current.Ratio))
            {
                offenses.Add("injection concentration state changed");
            }
        }

        static void AddOffenseIfDifferent(List<string> offenses, double stored, double current, string offense)
        {
            if (!AnalysisResultValiditySnapshot.SameDouble(stored, current))
                offenses.Add(offense);
        }

        static bool NullableDifferent(double? stored, double? current)
        {
            if (!stored.HasValue || !current.HasValue) return false;

            return !AnalysisResultValiditySnapshot.SameDouble(stored.Value, current.Value);
        }
    }

    public sealed class TandemSegmentSnapshot
    {
        public int FirstInjectionID { get; set; }
        public double SegmentInitialActiveCellConc { get; set; }
        public double SegmentInitialActiveTitrantConc { get; set; }

        public static TandemSegmentSnapshot Capture(TandemExperimentSegment segment)
        {
            return new TandemSegmentSnapshot
            {
                FirstInjectionID = segment.FirstInjectionID,
                SegmentInitialActiveCellConc = segment.SegmentInitialActiveCellConc,
                SegmentInitialActiveTitrantConc = segment.SegmentInitialActiveTitrantConc
            };
        }

        public void Compare(TandemSegmentSnapshot current, List<string> offenses)
        {
            if (FirstInjectionID != current.FirstInjectionID)
                offenses.Add("tandem segment layout changed");

            if (!AnalysisResultValiditySnapshot.SameDouble(SegmentInitialActiveCellConc, current.SegmentInitialActiveCellConc)
                || !AnalysisResultValiditySnapshot.SameDouble(SegmentInitialActiveTitrantConc, current.SegmentInitialActiveTitrantConc))
            {
                offenses.Add("tandem segment concentrations changed");
            }
        }
    }

    public sealed class ExperimentAttributeSnapshot
    {
        public AttributeKey Key { get; set; }
        public bool BoolValue { get; set; }
        public int IntValue { get; set; }
        public double DoubleValue { get; set; }
        public string StringValue { get; set; }
        public double ParameterValue { get; set; }
        public double ParameterSD { get; set; }

        public static List<ExperimentAttributeSnapshot> Capture(IEnumerable<ExperimentAttribute> attributes)
        {
            return (attributes ?? Enumerable.Empty<ExperimentAttribute>())
                .Where(IsFitRelevant)
                .Select(Capture)
                .OrderBy(a => (int)a.Key)
                .ThenBy(a => a.IntValue)
                .ThenBy(a => a.BoolValue)
                .ThenBy(a => a.DoubleValue)
                .ThenBy(a => a.StringValue)
                .ThenBy(a => a.ParameterValue)
                .ThenBy(a => a.ParameterSD)
                .ToList();
        }

        public static ExperimentAttributeSnapshot Capture(ExperimentAttribute attribute)
        {
            return new ExperimentAttributeSnapshot
            {
                Key = attribute.Key,
                BoolValue = attribute.BoolValue,
                IntValue = attribute.IntValue,
                DoubleValue = attribute.DoubleValue,
                StringValue = attribute.StringValue ?? "",
                ParameterValue = attribute.ParameterValue.Value,
                ParameterSD = attribute.ParameterValue.SD
            };
        }

        public bool EquivalentTo(ExperimentAttributeSnapshot current)
        {
            if (current == null) return false;

            return Key == current.Key
                && BoolValue == current.BoolValue
                && IntValue == current.IntValue
                && AnalysisResultValiditySnapshot.SameDouble(DoubleValue, current.DoubleValue)
                && string.Equals(StringValue ?? "", current.StringValue ?? "", StringComparison.Ordinal)
                && AnalysisResultValiditySnapshot.SameDouble(ParameterValue, current.ParameterValue)
                && AnalysisResultValiditySnapshot.SameDouble(ParameterSD, current.ParameterSD);
        }

        static bool IsFitRelevant(ExperimentAttribute attribute)
        {
            if (attribute == null) return false;

            return attribute.Key switch
            {
                AttributeKey.PreboundLigandConc => true,
                AttributeKey.BufferSubtraction => true,
                _ => false
            };
        }
    }
}
