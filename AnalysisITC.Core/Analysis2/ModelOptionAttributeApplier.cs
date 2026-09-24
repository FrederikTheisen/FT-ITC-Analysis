using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis
{
    public sealed class MissingModelOptionAttributesException : InvalidOperationException
    {
        public MissingModelOptionAttributesException(IEnumerable<string> missing)
            : base("Cannot prepare the fit because required experiment attributes are missing:\n"
                + string.Join("\n", missing.Select(item => "• " + item)))
        {
        }
    }

    /// <summary>Resolves enabled model option attributes on member models at fit launch.</summary>
    internal static class ModelOptionAttributeApplier
    {
        public static void Prepare(IEnumerable<Model> models, IDictionary<AttributeKey, ExperimentAttribute> sharedOptions)
        {
            var members = (models ?? Enumerable.Empty<Model>()).ToList();
            if (sharedOptions == null) return;

            var concentrationFromAttributes = sharedOptions.TryGetValue(AttributeKey.PreboundLigandConc, out var concentration)
                && concentration.BoolValue;
            var affinityFromAttributes = sharedOptions.TryGetValue(AttributeKey.PreboundLigandAffinity, out var affinity)
                && affinity.BoolValue;
            var enthalpyFromAttributes = sharedOptions.TryGetValue(AttributeKey.PreboundLigandEnthalpy, out var enthalpy)
                && enthalpy.BoolValue;
            var missing = new List<string>();

            foreach (var model in members)
            {
                var required = new List<string>();
                if (concentrationFromAttributes && !model.Data.Attributes.Any(attribute => attribute.Key == AttributeKey.PreboundLigandConc))
                    required.Add(AttributeKey.PreboundLigandConc.GetProperties().Name);
                if ((affinityFromAttributes || enthalpyFromAttributes)
                    && !model.Data.Attributes.Any(attribute => attribute.Key == AttributeKey.CompetitorResult))
                    required.Add(AttributeKey.CompetitorResult.GetProperties().Name);
                if (required.Count > 0)
                    missing.Add($"{model.Data.Name}: {string.Join(", ", required)}");
            }

            if (missing.Count > 0)
                throw new MissingModelOptionAttributesException(missing);

            if (affinityFromAttributes || enthalpyFromAttributes)
                CompetitorResultAttributeResolver.Refresh(members.Select(model => model.Data), affinityFromAttributes, enthalpyFromAttributes);

            // Stage every member's values before changing any model options.
            var staged = members.Select(model =>
            {
                var options = ModelOptionCatalog.CopyOptions(sharedOptions);
                if (concentrationFromAttributes)
                    options[AttributeKey.PreboundLigandConc].ParameterValue = model.Data.Attributes
                        .First(attribute => attribute.Key == AttributeKey.PreboundLigandConc).ParameterValue;
                if (affinityFromAttributes || enthalpyFromAttributes)
                {
                    var source = model.Data.Attributes.First(attribute => attribute.Key == AttributeKey.CompetitorResult);
                    if (affinityFromAttributes)
                    {
                        var kd = source.CapturedAffinity;
                        if (FloatWithError.IsNaN(kd) || !FWEMath.IsFinite(kd.Value) || kd.Value <= 0)
                            throw new InvalidOperationException($"Experiment '{model.Data.Name}' has no usable captured competitor affinity.");
                        var logKd = FWEMath.IsFinite(kd.Lower) && kd.Lower > 0 && FWEMath.IsFinite(kd.Upper)
                            ? FWEMath.Log10(kd)
                            : new FloatWithError(Math.Log10(kd.Value), kd.SD / (kd.Value * Math.Log(10.0)));
                        options[AttributeKey.PreboundLigandAffinity].ParameterValue = new FloatWithError(0) - logKd;
                    }
                    if (enthalpyFromAttributes)
                    {
                        var captured = source.CapturedEnthalpy;
                        if (FloatWithError.IsNaN(captured) || !FWEMath.IsFinite(captured.Value))
                            throw new InvalidOperationException($"Experiment '{model.Data.Name}' has no usable captured competitor enthalpy.");
                        options[AttributeKey.PreboundLigandEnthalpy].ParameterValue = captured;
                    }
                }
                return (Model: model, Options: options);
            }).ToList();

            foreach (var item in staged)
                item.Model.SetModelOptions(item.Options);
        }
    }
}
