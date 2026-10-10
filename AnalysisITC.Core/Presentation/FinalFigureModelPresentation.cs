using System.Linq;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Presentation
{
    public enum PublicationFigureModelSelection
    {
        AssessmentPolicy,
        BindingModel,
        NullModel,
        None
    }

    /// <summary>Session-only choice for Final Figure; reports and supporting figures do not use it.</summary>
    public static class FinalFigureModelPresentation
    {
        public const string SelectorTooltip = "Choose Binding, Null, or observations only.";
        public const string AssessmentTooltip = "Saved assessment; model selection does not change it.";
        public const string LineWidthTooltip = "Model curve thickness in points.";
        public const string SmoothnessTooltip = "Interpolation between model predictions.";
        public const string ConfidenceTooltip = "Show bootstrap uncertainty around the binding prediction.";
        public const string OffsetCorrectionTooltip = "Subtract the fitted binding offset from heats.";
        public static int SelectionIndex => Selection == PublicationFigureModelSelection.None ? 2
            : Selection == PublicationFigureModelSelection.NullModel ? 1 : 0;
        public static PublicationFigureModelSelection FromSelectionIndex(int index) => index == 2
            ? PublicationFigureModelSelection.None : index == 1
            ? PublicationFigureModelSelection.NullModel : PublicationFigureModelSelection.BindingModel;
        public const string FitLineTooltip = "Draw the selected model prediction.";
        public const string EnergyUnitTooltip = "Energy unit family; magnitude is chosen automatically.";
        public const string ExcludedAutoYScaleTooltip = "Let excluded points affect automatic Y axis limits.";
        public const string NoAssessmentText = "No assessment";
        public static PublicationFigureModelSelection Selection { get; set; } = PublicationFigureModelSelection.BindingModel;

        static EnergyUnitFamily? energyUnitFamily;

        /// <summary>Session-only Final Figure energy family; starts from the preference.</summary>
        public static EnergyUnitFamily EnergyUnitFamily
        {
            get => energyUnitFamily ??= AppSettings.EnergyUnitFamily == EnergyUnitFamily.Calories
                ? EnergyUnitFamily.Calories : EnergyUnitFamily.Joules;
            set => energyUnitFamily = value;
        }

        public static string UnavailableText(string modelLabel) => modelLabel + " fit unavailable";

        public static string AssessmentText(PublicationFigureSource source)
        {
            if (source?.Solution == null) return NoAssessmentText;
            if (source.Result != null)
            {
                var matches = source.Result.Solution?.Solutions?.Where(member => member?.Guid == source.Solution.Guid
                    && member.Data?.UniqueID == source.Experiment.UniqueID).ToList();
                if (matches?.Count != 1) return NoAssessmentText;
                var assessment = source.Result.GetMemberBindingAssessment(matches[0]);
                return "Assessment: " + NullModelComparisonPresentation.Conclusion(assessment);
            }
            var comparison = source.Solution.NullComparison;
            return comparison == null ? NoAssessmentText
                : "Automatic assessment: " + NullModelComparisonPresentation.Conclusion(comparison);
        }
    }

    public sealed class PublicationFigureModelResolution
    {
        internal PublicationFigureModelResolution(SolutionInterface solution, bool isNull, string label, bool isNone = false)
        {
            Solution = solution;
            IsNull = isNull;
            ModelLabel = label;
            IsNone = isNone;
        }

        public SolutionInterface Solution { get; }
        public bool IsNull { get; }
        public string ModelLabel { get; }
        public bool IsNone { get; }
        public bool IsUnavailable => !IsNone && Solution == null;
    }
}
