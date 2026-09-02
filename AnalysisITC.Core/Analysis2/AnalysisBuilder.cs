using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis.Models;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>
    /// Pure static factory. Given session state + DataManager data, produces a fresh AnalysisContext.
    /// Holds no mutable state of its own. Every method is a pure transformation.
    /// </summary>
    public static class AnalysisBuilder
    {
        // ── Data readiness ─────────────────────────────────────────────────

        public static bool IsAnalysisReady(ExperimentData data)
        {
            if (data == null) return false;
            if (data.SyringeConcentration <= double.Epsilon) return false;
            if (data.Injections == null) return false;

            var included = data.Injections.Where(i => i.Include).ToList();
            if (included.Count < 3) return false;
            if (included.Any(i => double.IsNaN(i.Enthalpy))) return false;
            if (included.All(i => Math.Abs(i.Enthalpy) < double.Epsilon)) return false;
            if (included.Last().Ratio <= 0) return false;

            return true;
        }

        /// <summary>
        /// Returns whether every included injection has a finite, positive peak-area
        /// uncertainty and can therefore participate in an error-weighted fit.
        /// </summary>
        public static bool CanUseErrorWeightedFitting(ExperimentData experiment)
        {
            if (experiment?.Injections == null) return false;

            var included = experiment.Injections.Where(injection => injection.Include).ToList();
            return included.Count > 0 && included.All(injection =>
                !double.IsNaN(injection.PeakArea.SD)
                && !double.IsInfinity(injection.PeakArea.SD)
                && injection.PeakArea.SD > 0);
        }

        /// <summary>
        /// Returns whether every included injection in every supplied experiment can
        /// participate in an error-weighted fit.
        /// </summary>
        public static bool CanUseErrorWeightedFitting(IEnumerable<ExperimentData> experiments)
        {
            if (experiments == null) return false;

            var data = experiments.ToList();
            return data.Count > 0 && data.All(CanUseErrorWeightedFitting);
        }

        public static void ValidateErrorWeightedFitting(IEnumerable<ExperimentData> experiments)
        {
            if (!CanUseErrorWeightedFitting(experiments))
                throw new HandledException(
                    HandledException.Severity.Error,
                    "Error-weighted fitting unavailable",
                    "Every included data point must have a finite peak-area SD larger than zero.");
        }

        public static bool IsModelAvailable(AnalysisModel model, bool isGlobal)
        {
            var includedData = DataManager.IncludedData.ToList();

            return isGlobal
                ? includedData.Count > 1 && includedData.All(d => ModelAvailableForExperiment(model, d))
                : ModelAvailableForExperiment(model, DataManager.Current);
        }

        public static bool DataSupportsAnalysis(ExperimentData experiment)
        {
            return AnalysisModelAttribute.GetAll().Any(mdl => ModelAvailableForExperiment(mdl, experiment));
        }

        static bool ModelAvailableForExperiment(AnalysisModel model, ExperimentData data)
        {
            if (data?.Injections == null) return false;
            if (data.Injections.Count(inj => inj.Include) < 3) return false;

            return model == AnalysisModel.Dissociation
                ? data.SyringeConcentration > double.Epsilon
                : data.CellConcentration > double.Epsilon;
        }

        // ── Build entry point ──────────────────────────────────────────────

        /// <summary>
        /// Build a fresh AnalysisContext from session state and the current DataManager.
        /// Throws if data is not in a ready state — callers should gate on IsAnalysisReady first.
        /// </summary>
        public static AnalysisContext Build(AnalysisSessionState session, bool reuseAttachedSolutionInitialValues = true)
        {
            return session.IsGlobal
                ? BuildGlobal(session.ModelType, DataManager.Data.Where(d => d.Include).ToList(), session.Global, reuseAttachedSolutionInitialValues)
                : BuildSingle(session.ModelType, DataManager.Current, session.Single, reuseAttachedSolutionInitialValues);
        }

        public static AnalysisContext Build(AnalysisSessionState session, IEnumerable<ExperimentData> experiments, bool reuseAttachedSolutionInitialValues = true)
        {
            var dataList = experiments?.Where(d => d != null).ToList() ?? new List<ExperimentData>();

            return session.IsGlobal
                ? BuildGlobal(session.ModelType, dataList, session.Global, reuseAttachedSolutionInitialValues)
                : BuildSingle(session.ModelType, dataList.FirstOrDefault(), session.Single, reuseAttachedSolutionInitialValues);
        }

        // ── Single ─────────────────────────────────────────────────────────

        static AnalysisContext BuildSingle(AnalysisModel modelType, ExperimentData data, AnalysisState state, bool reuseAttachedSolutionInitialValues)
        {
            if (data == null)
                throw new InvalidOperationException("No experiment selected.");
            if (!data.Injections.Any(i => i.Include))
                throw new HandledException(HandledException.Severity.Error, "No valid peaks", "Please check that not all peaks are excluded");

            var model = ConstructModel(modelType, data);
            model.ReuseAttachedSolutionInitialValues = reuseAttachedSolutionInitialValues;
            model.InitializeParameters(data);

            ApplyModelOptionsToModel(model, state);
            model.ApplyModelOptions();
            ApplyParameterOverridesToModel(model, modelType, state);

            return new AnalysisContext(modelType, model);
        }

        // ── Global ─────────────────────────────────────────────────────────

        static AnalysisContext BuildGlobal(AnalysisModel modelType, List<ExperimentData> dataList, AnalysisState state, bool reuseAttachedSolutionInitialValues)
        {
            if (dataList == null || dataList.Count < 2)
                throw new InvalidOperationException("Global analysis requires at least two included datasets.");

            var globalModel = new GlobalModel();
            var globalParams = new GlobalModelParameters();

            // Build each individual model with fresh data-derived parameters. Shared
            // structural options are applied below before constraints are constructed.
            foreach (var data in dataList)
            {
                var model = ConstructModel(modelType, data);
                model.ReuseAttachedSolutionInitialValues = reuseAttachedSolutionInitialValues;
                model.InitializeParameters(data);
                globalModel.AddModel(model);
            }

            // Apply stored model options to the shared GlobalModel options dict
            ApplyModelOptionsToGlobalModel(globalModel, state);
            foreach (var model in globalModel.Models)
                model.SetModelOptions(globalModel.ModelOptions);

            if (modelType == AnalysisModel.SequentialBindingSites)
                ValidateSequentialConstraintState(state, globalModel.Models.First());

            // Apply the persisted settings first so the constraint controls and the
            // generated parameter table can describe the same effective model state.
            foreach (var (paramType, constraint) in state.Constraints)
                globalParams.SetConstraintForParameter(paramType, constraint);

            // Build the global parameter table (values come from data guesses or stored overrides)
            InitializeGlobalParameters(modelType, globalModel, globalParams, state);

            // The active constraint is always included in the UI choices, even if it
            // would not normally be offered for the current datasets. This keeps the
            // displayed setting aligned with the dependent parameters and lets the
            // user explicitly change or remove it.
            var constraintOptions = DeriveConstraintOptions(globalModel, globalParams);
            var constraintFamilies = DeriveConstraintFamilies(modelType, globalModel, constraintOptions);

            return new AnalysisContext(modelType, globalModel, globalParams, constraintOptions, constraintFamilies);
        }

        static void ValidateSequentialConstraintState(AnalysisState state, Model model)
        {
            var activeSlots = ThermodynamicParameterSlots.Active(model).ToList();
            foreach (var family in new[]
            {
                ThermodynamicParameterFamily.Affinity,
                ThermodynamicParameterFamily.Enthalpy,
            })
            {
                var styles = activeSlots
                    .Select(slot => state.Constraints.TryGetValue(slot.Get(family), out var style)
                        ? style
                        : VariableConstraint.None)
                    .Distinct()
                    .ToList();
                if (styles.Count != 1)
                    throw new InvalidOperationException(
                        $"Sequential {family.ToString().ToLowerInvariant()} constraints must use one family-wide style across every active step.");
            }

            var activeCount = activeSlots.Count;
            if (state.Constraints.Keys.Any(key =>
                ThermodynamicParameterSlots.TryResolve(key, out var slot, out var family)
                && (slot.Index > activeCount
                    || (family != ThermodynamicParameterFamily.Affinity
                        && family != ThermodynamicParameterFamily.Enthalpy))))
                throw new InvalidOperationException(
                    "Sequential constraints contain an inactive or unsupported thermodynamic coordinate.");
        }

        // ── Global parameter initialization ───────────────────────────────

        /// <summary>
        /// Populates the GlobalModelParameters table based on which parameters are constrained.
        /// For each constrained parameter, the value comes from either a stored user override
        /// or a data-derived guess.
        /// </summary>
        static void InitializeGlobalParameters(
            AnalysisModel modelType,
            GlobalModel globalModel,
            GlobalModelParameters globalParams,
            AnalysisState state)
        {
            if (globalModel.Models.Count == 0) return;

            globalParams.ClearGlobalTable();

            var firstParams = globalModel.Models.First().Parameters;

            foreach (var par in firstParams.Table.Values)
            {
                if (GlobalConstraintSemantics.IsSupportedThermodynamicMember(par.Key))
                {
                    InitializeThermodynamicGlobalParameters(
                        modelType,
                        globalModel,
                        globalParams,
                        state,
                        par.Key);
                    continue;
                }

                switch (par.Key)
                {
                    case ParameterType.Nvalue1:
                    case ParameterType.Nvalue2:
                        if (globalParams.GetConstraintForParameter(par.Key) == VariableConstraint.SameForAll)
                        {
                            var (hasOverride, ov) = GetOverride(state, modelType, par.Key);
                            globalParams.AddorUpdateGlobalParameter(
                                par.Key,
                                hasOverride ? ov.Value : globalModel.Models.Average(m => m.GuessN()),
                                hasOverride && ov.IsLocked);
                        }
                        break;

                    case ParameterType.IsomerizationEquilibriumConstant:
                        if (globalParams.GetConstraintForParameter(par.Key) == VariableConstraint.SameForAll)
                        {
                            var (hasOverride, ov) = GetOverride(state, modelType, par.Key);
                            globalParams.AddorUpdateGlobalParameter(
                                par.Key,
                                hasOverride ? ov.Value : 0.42,
                                hasOverride && ov.IsLocked);
                        }
                        break;

                    case ParameterType.Offset:
                        if (globalParams.GetConstraintForParameter(par.Key) == VariableConstraint.SameForAll)
                        {
                            var (hasOverride, ov) = GetOverride(state, modelType, par.Key);
                            globalParams.AddorUpdateGlobalParameter(
                                par.Key,
                                hasOverride ? ov.Value : globalModel.Models.Average(m => m.GuessOffset()),
                                hasOverride && ov.IsLocked);
                        }
                        break;

                    default:
                        AppEventHandler.Print($"[AnalysisBuilder] Parameter {par.Key} not handled in InitializeGlobalParameters", 1);
                        break;
                }
            }
        }

        static void InitializeThermodynamicGlobalParameters(
            AnalysisModel modelType,
            GlobalModel globalModel,
            GlobalModelParameters globalParams,
            AnalysisState state,
            ParameterType memberKey)
        {
            var constraint = globalParams.GetConstraintForParameter(memberKey);
            foreach (var coordinateKey in GlobalConstraintSemantics.CoordinateKeys(memberKey, constraint))
            {
                var (hasOverride, ov) = GetOverride(state, modelType, coordinateKey);
                globalParams.AddorUpdateGlobalParameter(
                    coordinateKey,
                    hasOverride
                        ? ov.Value
                        : GlobalConstraintSemantics.InitialCoordinateValue(
                            globalModel.Models,
                            memberKey,
                            coordinateKey),
                    hasOverride && ov.IsLocked);
            }
        }

        static (bool hasOverride, ParameterOverride ov) GetOverride(AnalysisState state, AnalysisModel modelType, ParameterType key)
        {
            var overrideKey = new ParameterOverrideKey(modelType, key);
            bool has = state.ParameterOverrides.TryGetValue(overrideKey, out var ov);
            return (has, ov);
        }

        // ── Constraint option derivation ───────────────────────────────────

        /// <summary>
        /// Derives the normally available VariableConstraint choices for each parameter
        /// type and retains any currently active choice so the UI never misrepresents
        /// the parameter table that was built from it.
        /// </summary>
        static IReadOnlyDictionary<ParameterType, IReadOnlyList<VariableConstraint>> DeriveConstraintOptions(
            GlobalModel globalModel,
            GlobalModelParameters globalParams)
        {
            var dict = new Dictionary<ParameterType, IReadOnlyList<VariableConstraint>>();

            if (globalModel.Models.Count == 0)
                return dict;

            foreach (var par in globalModel.Models.First().Parameters.Table.Values)
            {
                if (ThermodynamicParameterSlots.TryResolve(par.Key, out _, out var family)
                    && family == ThermodynamicParameterFamily.Affinity)
                {
                    dict[par.Key] = new[]
                    {
                        VariableConstraint.None,
                        VariableConstraint.TemperatureDependent,
                        VariableConstraint.SameForAll,
                    };
                }
                else if (ThermodynamicParameterSlots.TryResolve(par.Key, out _, out family)
                    && family == ThermodynamicParameterFamily.Enthalpy)
                {
                    dict[par.Key] = globalModel.TemperatureDependenceExposed
                        ? new VariableConstraint[] { VariableConstraint.None, VariableConstraint.TemperatureDependent, VariableConstraint.SameForAll }
                        : new VariableConstraint[] { VariableConstraint.None, VariableConstraint.SameForAll };
                }
                else
                switch (par.Key)
                {
                    case ParameterType.Nvalue1:
                    case ParameterType.Nvalue2:
                    case ParameterType.Offset:
                    case ParameterType.IsomerizationEquilibriumConstant:
                        dict[par.Key] = new[] { VariableConstraint.None, VariableConstraint.SameForAll };
                        break;

                    default:
                        AppEventHandler.Print($"[AnalysisBuilder] Parameter {par.Key} not handled in DeriveConstraintOptions", 1);
                        break;
                }

                if (!dict.TryGetValue(par.Key, out var choices)) continue;

                var activeConstraint = globalParams.GetConstraintForParameter(par.Key);
                if (choices.Contains(activeConstraint)) continue;

                dict[par.Key] = new[]
                {
                    VariableConstraint.None,
                    VariableConstraint.TemperatureDependent,
                    VariableConstraint.SameForAll,
                }.Where(choice => choice == activeConstraint || choices.Contains(choice)).ToArray();
            }

            return dict;
        }

        static IReadOnlyList<GlobalConstraintFamilyDescriptor> DeriveConstraintFamilies(
            AnalysisModel modelType,
            GlobalModel globalModel,
            IReadOnlyDictionary<ParameterType, IReadOnlyList<VariableConstraint>> options)
        {
            if (modelType != AnalysisModel.SequentialBindingSites)
            {
                return options.Select(item => new GlobalConstraintFamilyDescriptor(
                    item.Key,
                    new[] { item.Key },
                    item.Value)).ToList();
            }

            var activeSlots = ThermodynamicParameterSlots.Active(globalModel.Models.First()).ToList();
            var descriptors = new List<GlobalConstraintFamilyDescriptor>();
            AddFamily(activeSlots.Select(slot => slot.Affinity));
            AddFamily(activeSlots.Select(slot => slot.Enthalpy));

            if (options.TryGetValue(ParameterType.Offset, out var offsetOptions))
            {
                descriptors.Add(new GlobalConstraintFamilyDescriptor(
                    ParameterType.Offset,
                    new[] { ParameterType.Offset },
                    offsetOptions));
            }

            return descriptors;

            void AddFamily(IEnumerable<ParameterType> keys)
            {
                var members = keys.Where(options.ContainsKey).ToList();
                if (members.Count == 0) return;
                descriptors.Add(new GlobalConstraintFamilyDescriptor(
                    members[0],
                    members,
                    options[members[0]]));
            }
        }

        // ── Model construction ─────────────────────────────────────────────

        public static Model ConstructModel(AnalysisModel modelType, ExperimentData data)
        {
            return modelType switch
            {
                AnalysisModel.OneSetOfSites => new OneSetOfSites(data),
                AnalysisModel.CompetitiveBinding => new CompetitiveBinding(data),
                AnalysisModel.TwoSetsOfSites => new TwoSetsOfSites(data),
                AnalysisModel.SequentialBindingSites => new SequentialBindingSites(data),
                AnalysisModel.Dissociation => new Dissociation(data),
                _ => throw new NotImplementedException($"Model '{modelType}' is not implemented.")
            };
        }

        // ── Option and override application ───────────────────────────────

        static void ApplyModelOptionsToModel(Model model, AnalysisState state)
        {
            foreach (var (key, storedOpt) in state.ModelOptions)
            {
                if (!model.ModelOptions.ContainsKey(key)) continue;

                var opt = storedOpt.Copy();
                // Preserve the current model's display name — the stored name may be stale
                opt.OptionName = model.ModelOptions[key].OptionName;
                model.ModelOptions[key] = opt;
            }
        }

        static void ApplyModelOptionsToGlobalModel(GlobalModel globalModel, AnalysisState state)
        {
            foreach (var (key, storedOpt) in state.ModelOptions)
            {
                if (!globalModel.ModelOptions.ContainsKey(key)) continue;

                var opt = storedOpt.Copy();
                opt.OptionName = globalModel.ModelOptions[key].OptionName;
                globalModel.ModelOptions[key] = opt;
            }
        }

        static void ApplyParameterOverridesToModel(Model model, AnalysisModel modelType, AnalysisState state)
        {
            foreach (var (overrideKey, ov) in state.ParameterOverrides)
            {
                if (overrideKey.Model != modelType) continue;
                if (!model.Parameters.Table.ContainsKey(overrideKey.Key)) continue;

                model.Parameters.Table[overrideKey.Key].SetValue(ov.Value, ov.IsLocked);
            }
        }

        // ── Default value retrieval ────────────────────────────────────────

        /// <summary>
        /// Returns the data-derived default value for a given parameter, without applying any user overrides.
        /// Used by Parameter.ReinitializeParameter to restore the model-default without going through ModelFactory.
        /// </summary>
        public static double GetDefaultParameterValue(
            AnalysisModel modelType,
            ExperimentData data,
            ParameterType key,
            IDictionary<AttributeKey, ExperimentAttribute> modelOptions = null)
        {
            var model = ConstructModel(modelType, data);
            model.ReuseAttachedSolutionInitialValues = false;
            model.InitializeParameters(data);
            if (modelOptions != null)
            {
                foreach (var (optionKey, option) in modelOptions)
                {
                    if (model.ModelOptions.ContainsKey(optionKey))
                        model.ModelOptions[optionKey] = option.Copy();
                }
            }
            model.ApplyModelOptions();
            return model.Parameters.Table.TryGetValue(key, out var par) ? par.Value : 0d;
        }
    }
}
