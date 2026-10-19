using System;
using System.Globalization;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Core.Viewer
{
    /// <summary>Formats recorded attributes without consulting the desktop session.</summary>
    internal static class ViewerAttributePresentation
    {
        const string Unavailable = "Unavailable";
        const UncertaintyDisplayStyle UncertaintyStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;

        public static string Format(ExperimentAttribute attribute,
            Func<string, ExperimentData> resolveExperiment, Func<string, AnalysisResult> resolveResult)
        {
            switch (attribute.Key)
            {
                case AttributeKey.Buffer:
                    var bufferName = ((Buffer)attribute.IntValue).GetProperties()?.AttributedName;
                    if (string.IsNullOrWhiteSpace(bufferName) || !Usable(attribute.ParameterValue)
                        || !FWEMath.IsFinite(attribute.DoubleValue)) return Unavailable;
                    return Concentration(attribute.ParameterValue, ConcentrationUnit.mM, describeUncertainty: false)
                        + " " + PlainName(bufferName) + " pH " + Number(attribute.DoubleValue)
                        + DescribeUncertainty(attribute.ParameterValue);
                case AttributeKey.Salt:
                    if (!Enum.IsDefined(typeof(Salt), attribute.IntValue)) return Unavailable;
                    var saltName = ((Salt)attribute.IntValue).GetProperties()?.AttributedName;
                    if (string.IsNullOrWhiteSpace(saltName) || !Usable(attribute.ParameterValue)) return Unavailable;
                    return Concentration(attribute.ParameterValue, ConcentrationUnit.mM, describeUncertainty: false)
                        + " " + PlainName(saltName) + DescribeUncertainty(attribute.ParameterValue);
                case AttributeKey.IonicStrength:
                    return Concentration(attribute.ParameterValue, ConcentrationUnit.mM);
                case AttributeKey.PreboundLigandConc:
                    return Concentration(attribute.ParameterValue);
                case AttributeKey.Species:
                    return string.IsNullOrWhiteSpace(attribute.StringValue) ? null
                        : ExperimentAttribute.GetSpeciesLocationDisplayName(attribute.IntValue) + ": " + attribute.StringValue;
                case AttributeKey.NumberOfSites1:
                case AttributeKey.NumberOfSites2:
                    // Legacy experiment attributes can carry these Int-declared options as decimals.
                    return Number(attribute.DoubleValue);
                case AttributeKey.BufferSubtraction:
                    var settings = BufferSubtractionSettings.FromAttribute(attribute);
                    var reference = string.IsNullOrWhiteSpace(attribute.StringValue) ? null
                        : resolveExperiment(attribute.StringValue);
                    if (!string.IsNullOrWhiteSpace(reference?.Name))
                        return reference.Name + " (" + settings.MethodDisplayName + ")";
                    if (!string.IsNullOrWhiteSpace(settings.Snapshot?.ReferenceName))
                        return settings.Snapshot.ReferenceName + " (stored values, " + settings.MethodDisplayName + ")";
                    return "Missing reference experiment (" + settings.MethodDisplayName + ")";
                case AttributeKey.CompetitorResult:
                    var source = string.IsNullOrWhiteSpace(attribute.StringValue) ? null
                        : resolveResult(attribute.StringValue);
                    var attribution = string.IsNullOrWhiteSpace(attribute.StringValue) ? "Source result not recorded"
                        : string.IsNullOrWhiteSpace(source?.Name) ? "Source result unavailable"
                        : "From result \"" + source.Name + "\"";
                    var values = CompetitorResultPreviewBuilder.FormatValues(attribute.CapturedAffinity, attribute.CapturedEnthalpy,
                        kd => Usable(kd) ? Concentration(kd, describeUncertainty: false) : "unavailable",
                        enthalpy => Usable(enthalpy)
                            ? ScientificNumber(enthalpy * EnergyUnit.KiloJoule.GetMod()) + " " + EnergyUnit.KiloJoule.GetUnit() + "/mol"
                            : "unavailable");
                    return attribution + "; saved on experiment"
                        + DescribeUncertainty(attribute.CapturedAffinity, attribute.CapturedEnthalpy) + ": " + values;
            }

            // Other legacy attributes use their declared field, not whichever raw field is nonzero.
            switch (attribute.Key.GetProperties()?.Type)
            {
                case ExperimentAttribute.AttributeType.Bool: return attribute.BoolValue ? "Yes" : "No";
                case ExperimentAttribute.AttributeType.Int:
                case ExperimentAttribute.AttributeType.Enum: return attribute.IntValue.ToString(CultureInfo.InvariantCulture);
                case ExperimentAttribute.AttributeType.Double: return Number(attribute.DoubleValue);
                case ExperimentAttribute.AttributeType.Parameter:
                case ExperimentAttribute.AttributeType.ParameterAffinity:
                case ExperimentAttribute.AttributeType.ParameterConcentration:
                    return Usable(attribute.ParameterValue)
                        ? ScientificNumber(attribute.ParameterValue) + DescribeUncertainty(attribute.ParameterValue) : Unavailable;
                case ExperimentAttribute.AttributeType.ReferenceExperiment:
                    return (string.IsNullOrWhiteSpace(attribute.StringValue) ? null : resolveExperiment(attribute.StringValue)?.Name)
                        ?? "Missing reference experiment";
                default: return attribute.StringValue;
            }
        }

        static string PlainName(string name) => name.Replace("{", "").Replace("}", "");
        static bool Usable(FloatWithError value) => !FloatWithError.IsNaN(value) && FWEMath.IsFinite(value.Value);
        static string Number(double value) => FWEMath.IsFinite(value) ? value.ToString("G6", CultureInfo.InvariantCulture) : Unavailable;
        static string ScientificNumber(FloatWithError value) => value.ToString("G6", UncertaintyStyle, CultureInfo.InvariantCulture);

        static string Concentration(FloatWithError value, ConcentrationUnit? unit = null, bool describeUncertainty = true)
        {
            if (!Usable(value)) return Unavailable;
            var displayUnit = unit ?? (value.Value == 0 ? ConcentrationUnit.µM
                : ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(Math.Abs(value.Value)));
            return ScientificNumber(value * displayUnit.GetMod()) + " " + displayUnit.GetName()
                + (describeUncertainty ? DescribeUncertainty(value) : "");
        }

        static string DescribeUncertainty(params FloatWithError[] values)
        {
            var sd = false;
            var ci = false;
            foreach (var value in values)
            {
                if (!Usable(value)) continue;
                sd |= value.HasError;
                ci |= value.HasConfidenceInterval;
            }
            return sd && ci ? " (± SD; brackets: 95% CI)"
                : sd ? " (± SD)" : ci ? " (brackets: 95% CI)" : "";
        }
    }
}
