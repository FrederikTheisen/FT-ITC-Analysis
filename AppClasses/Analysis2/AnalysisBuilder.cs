using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses.Models;

namespace AnalysisITC.AppClasses.AnalysisClasses
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

        public static bool IsModelAvailable(AnalysisModel model, bool isGlobal)
        {
            if (DataManager.Current == null) return false;

            return isGlobal
                ? DataManager.IncludedData.All(d => ModelAvailableForExperiment(model, d))
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
        public static AnalysisContext Build(AnalysisSessionState session)
        {
            return session.IsGlobal
                ? BuildGlobal(session.ModelType, DataManager.Data.Where(d => d.Include).ToList(), session.Global)
                : BuildSingle(session.ModelType, DataManager.Current, session.Single);
        }

        // ── Single ─────────────────────────────────────────────────────────

        static AnalysisContext BuildSingle(AnalysisModel modelType, ExperimentData data, AnalysisState state)
        {
            if (data == null)
                throw new InvalidOperationException("No experiment selected.");
            if (!data.Injections.Any(i => i.Include))
                throw new HandledException(HandledException.Severity.Error, "No valid peaks", "Please check that not all peaks are excluded");

            var model = ConstructModel(modelType, data);
            model.InitializeParameters(data);

            ApplyModelOptionsToModel(model, state);
            ApplyParameterOverridesToModel(model, modelType, state);

            return new AnalysisContext(modelType, model);
        }

        // ── Global ─────────────────────────────────────────────────────────

        static AnalysisContext BuildGlobal(AnalysisModel modelType, List<ExperimentData> dataList, AnalysisState state)
        {
            if (dataList == null || dataList.Count == 0)
                throw new InvalidOperationException("No datasets included in global analysis.");

            var globalModel = new GlobalModel();
            var globalParams = new GlobalModelParameters();

            // Build each individual model with fresh data-derived parameters.
            // Model options are NOT applied to individual models here — they are applied
            // to GlobalModel.ModelOptions and propagated at FinalizeForSolver() time.
            foreach (var data in dataList)
            {
                var model = ConstructModel(modelType, data);
                model.InitializeParameters(data);
                globalModel.AddModel(model);
            }

            // Apply stored model options to the shared GlobalModel options dict
            ApplyModelOptionsToGlobalModel(globalModel, state);

            // Apply stored constraints to the global parameter set
            foreach (var (paramType, constraint) in state.Constraints)
                globalParams.SetConstraintForParameter(paramType, constraint);

            // Build the global parameter table (values come from data guesses or stored overrides)
            InitializeGlobalParameters(modelType, globalModel, globalParams, state);

            // Derive the legal constraint options for the constraint UI
            var constraintOptions = DeriveConstraintOptions(globalModel);

            return new AnalysisContext(modelType, globalModel, globalParams, constraintOptions);
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

                    case ParameterType.Enthalpy1:
                    case ParameterType.Enthalpy2:
                        switch (globalParams.GetConstraintForParameter(par.Key))
                        {
                            case VariableConstraint.SameForAll:
                                {
                                    var (hasOverride, ov) = GetOverride(state, modelType, par.Key);
                                    globalParams.AddorUpdateGlobalParameter(
                                        par.Key,
                                        hasOverride ? ov.Value : globalModel.Models.Average(m => m.GuessEnthalpy()),
                                        hasOverride && ov.IsLocked);
                                    break;
                                }
                            case VariableConstraint.TemperatureDependent:
                                {
                                    var cpKey = par.Key == ParameterType.Enthalpy1 ? ParameterType.HeatCapacity1 : ParameterType.HeatCapacity2;
                                    var (hasHOverride, hOv) = GetOverride(state, modelType, par.Key);
                                    var (hasCpOverride, cpOv) = GetOverride(state, modelType, cpKey);

                                    globalParams.AddorUpdateGlobalParameter(
                                        cpKey,
                                        hasCpOverride ? cpOv.Value : -1000,
                                        hasCpOverride && cpOv.IsLocked);
                                    globalParams.AddorUpdateGlobalParameter(
                                        par.Key,
                                        hasHOverride ? hOv.Value : globalModel.Models.Average(m => m.GuessEnthalpy()),
                                        hasHOverride && hOv.IsLocked);
                                    break;
                                }
                        }
                        break;

                    case ParameterType.Affinity1:
                    case ParameterType.Affinity2:
                        switch (globalParams.GetConstraintForParameter(par.Key))
                        {
                            case VariableConstraint.SameForAll:
                            case VariableConstraint.TemperatureDependent:
                                {
                                    var gibbsKey = par.Key == ParameterType.Affinity1 ? ParameterType.Gibbs1 : ParameterType.Gibbs2;
                                    var (hasOverride, ov) = GetOverride(state, modelType, gibbsKey);
                                    globalParams.AddorUpdateGlobalParameter(
                                        gibbsKey,
                                        hasOverride ? ov.Value : globalModel.Models.Average(m => m.GuessAffinityAsGibbs()),
                                        hasOverride && ov.IsLocked);
                                    break;
                                }
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

                    default:
                        AppEventHandler.Print($"[AnalysisBuilder] Parameter {par.Key} not handled in InitializeGlobalParameters", 1);
                        break;
                }
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
        /// Derives which VariableConstraint choices are legal for each parameter type,
        /// based on the first individual model's parameter table.
        /// </summary>
        static IReadOnlyDictionary<ParameterType, IReadOnlyList<VariableConstraint>> DeriveConstraintOptions(GlobalModel globalModel)
        {
            var dict = new Dictionary<ParameterType, IReadOnlyList<VariableConstraint>>();

            if (globalModel.Models.Count == 0)
                return dict;

            foreach (var par in globalModel.Models.First().Parameters.Table.Values)
            {
                switch (par.Key)
                {
                    case ParameterType.Affinity1:
                    case ParameterType.Affinity2:
                        dict[par.Key] = new[] { VariableConstraint.None, VariableConstraint.TemperatureDependent };
                        break;

                    case ParameterType.Enthalpy1:
                    case ParameterType.Enthalpy2:
                        dict[par.Key] = globalModel.TemperatureDependenceExposed
                            ? new VariableConstraint[] { VariableConstraint.None, VariableConstraint.TemperatureDependent, VariableConstraint.SameForAll }
                            : new VariableConstraint[] { VariableConstraint.None, VariableConstraint.SameForAll };
                        break;

                    case ParameterType.Nvalue1:
                    case ParameterType.Nvalue2:
                    case ParameterType.IsomerizationEquilibriumConstant:
                        dict[par.Key] = new[] { VariableConstraint.None, VariableConstraint.SameForAll };
                        break;

                    default:
                        AppEventHandler.Print($"[AnalysisBuilder] Parameter {par.Key} not handled in DeriveConstraintOptions", 1);
                        break;
                }
            }

            return dict;
        }

        // ── Model construction ─────────────────────────────────────────────

        public static Model ConstructModel(AnalysisModel modelType, ExperimentData data)
        {
            return modelType switch
            {
                AnalysisModel.OneSetOfSites => new OneSetOfSites(data),
                AnalysisModel.CompetitiveBinding => new CompetitiveBinding(data),
                AnalysisModel.TwoSetsOfSites => new TwoSetsOfSites(data),
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
        public static double GetDefaultParameterValue(AnalysisModel modelType, ExperimentData data, ParameterType key)
        {
            var model = ConstructModel(modelType, data);
            model.InitializeParameters(data);
            return model.Parameters.Table.TryGetValue(key, out var par) ? par.Value : 0d;
        }
    }
}
