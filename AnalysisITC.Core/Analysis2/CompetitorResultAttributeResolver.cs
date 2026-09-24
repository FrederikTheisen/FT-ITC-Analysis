using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>Refreshes cached competitor summary values immediately before a fit.</summary>
    public static class CompetitorResultAttributeResolver
    {
        public static void Refresh(IEnumerable<ExperimentData> experiments, bool requireAffinity, bool requireEnthalpy)
        {
            var updates = new List<(ExperimentData Experiment, ExperimentAttribute Attribute, string SolutionId,
                FloatWithError Kd, FloatWithError Enthalpy)>();
            foreach (var experiment in experiments ?? Enumerable.Empty<ExperimentData>())
            {
                var attribute = experiment.Attributes.FirstOrDefault(item => item.Key == AttributeKey.CompetitorResult);
                if (attribute == null)
                {
                    if (requireAffinity || requireEnthalpy)
                        throw new InvalidOperationException($"Experiment '{experiment.Name}' needs a Competitor properties attribute because a competitor model option uses From attributes.");
                    continue;
                }
                var result = DataManager.Results.FirstOrDefault(item => item.UniqueID == attribute.StringValue);
                if (result == null)
                {
                    EnsureCaptured(experiment, attribute, requireAffinity, requireEnthalpy);
                    continue; // Cached values remain usable after source deletion.
                }
                if (result.Model?.ModelType != Models.AnalysisModel.OneSetOfSites)
                    throw new InvalidOperationException($"Analysis Result '{result.Name}' must use the one-set-of-sites model to supply competitor properties.");

                var (kd, enthalpy) = EvaluateSummary(result, experiment);
                var sourceSolutionId = result.Solution.UniqueID;
                if (string.Equals(attribute.SourceSolutionId, sourceSolutionId, StringComparison.Ordinal)
                    && Same(attribute.CapturedAffinity, kd)
                    && Same(attribute.CapturedEnthalpy, enthalpy)) continue;
                updates.Add((experiment, attribute, sourceSolutionId, kd, enthalpy));
            }

            // Validate every member before changing any experiment attribute.
            foreach (var update in updates)
            {
                var refreshed = update.Attribute.Copy();
                refreshed.SourceSolutionId = update.SolutionId;
                refreshed.CapturedAffinity = update.Kd;
                refreshed.CapturedEnthalpy = update.Enthalpy;
                update.Experiment.AddOrUpdateAttribute(refreshed, notify: false);
            }
        }

        internal static (FloatWithError Kd, FloatWithError Enthalpy) EvaluateSummary(AnalysisResult result, ExperimentData experiment)
        {
            var temperature = result.Model.TemperatureDependenceExposed
                ? experiment.MeasuredTemperature
                : AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);
            var summaries = new AnalysisResultAggregateSummaryCalculator(result);
            var gibbs = summaries.EvaluateSummaryParameter(ParameterType.Gibbs1, temperature)?.Value ?? FloatWithError.NaN;
            var enthalpy = summaries.EvaluateSummaryParameter(ParameterType.Enthalpy1, temperature)?.Value ?? FloatWithError.NaN;
            var kelvin = temperature + 273.15;
            if (!(kelvin > 0) || FloatWithError.IsNaN(gibbs) || !FWEMath.IsFinite(gibbs.Value))
                throw new InvalidOperationException($"Analysis Result '{result.Name}' does not have a usable global affinity summary.");

            // Match the result summary display: Kd = exp(∆G / RT).
            var kd = FWEMath.Exp(gibbs / (kelvin * Energy.R));
            if (!FWEMath.IsFinite(kd.Value) || kd.Value <= 0)
                throw new InvalidOperationException($"Analysis Result '{result.Name}' does not have a usable global affinity summary.");
            if (FloatWithError.IsNaN(enthalpy) || !FWEMath.IsFinite(enthalpy.Value))
                throw new InvalidOperationException($"Analysis Result '{result.Name}' does not have a usable global enthalpy summary.");

            return (kd, enthalpy);
        }

        internal static void EnsureCaptured(ExperimentData experiment, ExperimentAttribute attribute, bool requireAffinity, bool requireEnthalpy)
        {
            if ((requireAffinity || requireEnthalpy) && string.IsNullOrWhiteSpace(attribute.StringValue))
                throw new InvalidOperationException($"Experiment '{experiment.Name}' needs a selected one-set-of-sites Analysis Result in its Competitor properties attribute.");
            if (requireAffinity && (FloatWithError.IsNaN(attribute.CapturedAffinity) || !FWEMath.IsFinite(attribute.CapturedAffinity.Value)))
                throw new InvalidOperationException($"Experiment '{experiment.Name}' references a missing Analysis Result and has no captured competitor affinity. Restore the source result or enter the affinity manually.");
            if (requireEnthalpy && (FloatWithError.IsNaN(attribute.CapturedEnthalpy) || !FWEMath.IsFinite(attribute.CapturedEnthalpy.Value)))
                throw new InvalidOperationException($"Experiment '{experiment.Name}' references a missing Analysis Result and has no captured competitor enthalpy. Restore the source result or enter the enthalpy manually.");
        }

        static bool Same(FloatWithError first, FloatWithError second) =>
            !FloatWithError.IsNaN(first) && !FloatWithError.IsNaN(second)
                && FWEMath.IsFinite(first.Value) && FWEMath.IsFinite(second.Value)
                && first.Value == second.Value && first.SD == second.SD
                && first.Lower == second.Lower && first.Upper == second.Upper;

    }
}
