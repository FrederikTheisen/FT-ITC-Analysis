using System;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Read-only presentation of saved model options in Results.</summary>
    public static class AnalysisResultModelOptionPresentation
    {
        public static bool UsesExperimentAttribute(AttributeKey key, ExperimentAttribute option) =>
            option?.BoolValue == true && key is AttributeKey.PreboundLigandConc
                or AttributeKey.PreboundLigandAffinity or AttributeKey.PreboundLigandEnthalpy;

        public static string FormatValue(AttributeKey key, ExperimentAttribute option, bool showAttributeSource = true)
        {
            if (option == null) return "";
            if (showAttributeSource && UsesExperimentAttribute(key, option))
                return "From experiment attribute";

            var style = AppSettings.UncertaintyDisplayStyle;
            var value = option.ParameterValue;
            switch (key)
            {
                case AttributeKey.PreboundLigandAffinity:
                    var kd = 1.0 / FWEMath.Pow(10.0, value);
                    return kd.AsFormattedConcentration(AppSettings.DefaultConcentrationUnit, style: style);
                case AttributeKey.PreboundLigandConc:
                    return value.AsFormattedConcentration(AppSettings.DefaultConcentrationUnit, style: style);
                case AttributeKey.PreboundLigandEnthalpy:
                    return new Energy(value).ToFormattedString(
                        EnergyUnitResolver.Resolve(AppSettings.EnergyUnitFamily, value.Value),
                        permole: true, style: style);
                case AttributeKey.Percentage:
                    return (100.0 * value).AsNumber(style) + " %";
                case AttributeKey.NumberOfSites1:
                case AttributeKey.NumberOfSites2:
                    return FormatStoichiometry(option.DoubleValue);
            }

            return key.GetProperties().Type switch
            {
                ExperimentAttribute.AttributeType.Parameter
                    or ExperimentAttribute.AttributeType.ParameterAffinity
                    or ExperimentAttribute.AttributeType.ParameterConcentration => value.AsNumber(style),
                _ => option.GetDisplayValue(),
            };
        }

        static string FormatStoichiometry(double value)
        {
            if (!FWEMath.IsFinite(value)) return "Unavailable";
            var preset = StoichiometryOptions.Presets.FirstOrDefault(item => Math.Abs(item.Factor - value) < 1e-9);
            return preset?.Title ?? value.ToString("G6", CultureInfo.CurrentCulture);
        }
    }
}
