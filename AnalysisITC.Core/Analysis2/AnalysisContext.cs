using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis.Models;


namespace AnalysisITC.Core.Analysis
{
    /// <summary>
    /// An immutable snapshot of a built analysis, produced by AnalysisBuilder.
    /// Consumed by the UI (for parameter display) and by the solver (via FinalizeForSolver + CreateSolver).
    /// Never mutate this object directly — user edits go through AnalysisWorkspace,
    /// which updates session state and triggers a rebuild.
    /// </summary>
    public sealed class AnalysisContext
    {
        public AnalysisModel ModelType { get; }
        public bool IsMultiExperiment { get; }

        // ── Single-analysis ────────────────────────────────────────────────
        public Model SingleModel { get; }

        // ── Global-analysis ────────────────────────────────────────────────
        public GlobalModel GlobalModel { get; }
        public GlobalModelParameters GlobalModelParameters { get; }

        /// <summary>
        /// Populated for global analysis only.
        /// Maps each global ParameterType to the VariableConstraint options that are legal for it.
        /// Derived purely from the first individual model's parameter table.
        /// </summary>
        public IReadOnlyDictionary<ParameterType, IReadOnlyList<VariableConstraint>> ExposedConstraintOptions { get; }

        /// <summary>
        /// Constraint controls grouped at the model-family level. Existing models
        /// expose one descriptor per parameter; sequential models expose one affinity
        /// and one enthalpy descriptor across all active steps.
        /// </summary>
        public IReadOnlyList<GlobalConstraintFamilyDescriptor> ExposedConstraintFamilies { get; }

        // ── Common UI surface ──────────────────────────────────────────────

        /// <summary>
        /// The parameter list the UI should display and let the user edit.
        /// For single analysis: the individual model's parameters.
        /// For global analysis: the global table parameters (Gibbs, HeatCapacity, etc.)
        /// </summary>
        public IReadOnlyList<Parameter> ExposedParameters { get; }

        /// <summary>
        /// The model options the UI should display and let the user edit.
        /// For single analysis: the individual model's options.
        /// For global analysis: the GlobalModel's shared options dict.
        /// </summary>
        public IDictionary<AttributeKey, ExperimentAttribute> ExposedModelOptions { get; }

        public bool WillFitGlobally =>
            IsMultiExperiment &&
            GlobalModelParameters?.RequiresGlobalFitting == true;

        /// <summary>
        /// Number of independent variables in the current fit configuration.
        /// </summary>
        public int FittingVariableCount
        {
            get
            {
                if (!IsMultiExperiment)
                    return SingleModel?.NumberOfParameters ?? 0;

                var sharedVariables = GlobalModelParameters?.GlobalTable.Values
                    .Count(parameter => parameter.IsFitted) ?? 0;
                var individualVariables = GlobalModel?.Models.Sum(model =>
                    model.Parameters.Table.Values.Count(parameter =>
                        parameter.IsFitted
                        && GlobalModelParameters.GetConstraintForParameter(parameter.Key)
                            == VariableConstraint.None)) ?? 0;

                return sharedVariables + individualVariables;
            }
        }

        /// <summary>
        /// Number of included experiments used as observations by the fit.
        /// </summary>
        public int FittingExperimentCount => IsMultiExperiment
            ? GlobalModel?.Models.Count ?? 0
            : 1;

        /// <summary>
        /// Number of included injection heats used as observations by the fit.
        /// </summary>
        public int FittingPointCount => IsMultiExperiment
            ? GlobalModel?.GetNumberOfPoints() ?? 0
            : SingleModel?.NumberOfPoints ?? 0;

        // ── Constructors ───────────────────────────────────────────────────

        internal AnalysisContext(AnalysisModel modelType, Model model)
        {
            ModelType = modelType;
            IsMultiExperiment = false;
            SingleModel = model;
            ExposedParameters = model.Parameters.Table.Values.ToList();
            ExposedModelOptions = model.ModelOptions;
            ExposedConstraintOptions = new Dictionary<ParameterType, IReadOnlyList<VariableConstraint>>();
            ExposedConstraintFamilies = new List<GlobalConstraintFamilyDescriptor>();
        }

        internal AnalysisContext(
            AnalysisModel modelType,
            GlobalModel globalModel,
            GlobalModelParameters globalParams,
            IReadOnlyDictionary<ParameterType, IReadOnlyList<VariableConstraint>> constraintOptions,
            IReadOnlyList<GlobalConstraintFamilyDescriptor> constraintFamilies = null)
        {
            ModelType = modelType;
            IsMultiExperiment = true;
            GlobalModel = globalModel;
            GlobalModelParameters = globalParams;
            ExposedConstraintOptions = constraintOptions;
            ExposedConstraintFamilies = constraintFamilies
                ?? new List<GlobalConstraintFamilyDescriptor>();
            ExposedParameters = globalParams.GlobalTable.Values.ToList();
            ExposedModelOptions = globalModel.ModelOptions;
        }

        // ── Solver integration ─────────────────────────────────────────────

        /// <summary>
        /// Wires the model object graph immediately before handing it to the solver.
        /// Must be called once per solve attempt, just before CreateSolver().
        /// It is safe to call multiple times on the same context.
        /// </summary>
        public void FinalizeForSolver()
        {
            PrepareSolverGraph(attachToExperiments: true);
        }

        /// <summary>
        /// Prepares the context-owned model graph for validation or solving. Validation
        /// must not replace the fitted models currently attached to experiments.
        /// </summary>
        void PrepareSolverGraph(bool attachToExperiments)
        {
            if (!IsMultiExperiment)
            {
                if (attachToExperiments)
                    SingleModel.Data.Model = SingleModel;
                SingleModel.SetModelOptions();
                SingleModel.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            }
            else
            {
                GlobalModelParameters.IndividualModelParameterList.Clear();
                GlobalModel.Parameters = GlobalModelParameters;

                foreach (var mdl in GlobalModel.Models)
                {
                    if (attachToExperiments)
                        mdl.Data.Model = mdl;
                    mdl.ModelCloneOptions = GlobalModelParameters.RequiresGlobalFitting
                        ? ModelCloneOptions.DefaultGlobalOptions
                        : ModelCloneOptions.DefaultOptions;
                    mdl.SetModelOptions(GlobalModel.ModelOptions);
                    GlobalModelParameters.AddIndivdualParameter(mdl.Parameters);
                }

                GlobalModelParameters.SetIndividualFromGlobal();

                GlobalModel.ModelCloneOptions = GlobalModelParameters.RequiresGlobalFitting
                    ? ModelCloneOptions.DefaultGlobalOptions
                    : ModelCloneOptions.DefaultOptions;
            }
        }

        public SolverInterface CreateSolver()
        {
            return IsMultiExperiment
                ? SolverInterface.Initialize(GlobalModel)
                : SolverInterface.Initialize(SingleModel);
        }

        public void RefreshParameterLimits()
        {
            if (!IsMultiExperiment)
            {
                foreach (var par in SingleModel.Parameters.Table.Values)
                    par.RefreshLimits();
            }
            else
            {
                foreach (var par in GlobalModelParameters.GlobalTable.Values)
                    par.RefreshLimits();
                foreach (var parSet in GlobalModelParameters.IndividualModelParameterList)
                    foreach (var par in parSet.Table.Values)
                        par.RefreshLimits();
            }
        }

        /// <summary>
        /// Returns current starting-value limit violations after applying global
        /// constraints and refreshing limits. This is used by inspectors for
        /// transient inline feedback; PrepareForSolve remains the launch guard.
        /// </summary>
        public IReadOnlyList<InitialParameterLimitViolation> DetectInitialParameterLimitViolations()
        {
            PrepareSolverGraph(attachToExperiments: false);
            RefreshParameterLimits();
            return IsMultiExperiment
                ? InitialParameterLimitViolationDetector.Detect(GlobalModel)
                : InitialParameterLimitViolationDetector.Detect(SingleModel);
        }
    }
}
