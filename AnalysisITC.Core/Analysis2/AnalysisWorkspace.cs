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
    /// Coordinator that owns session state and the current built AnalysisContext.
    /// Replaces the old ModelFactory.Factory global singleton.
    /// The view controller holds one of these and delegates all orchestration to it.
    ///
    /// Contract:
    ///   - All user edits (parameter overrides, model options, constraints, mode) go through
    ///     this class, which updates persistent session state and triggers a rebuild.
    ///   - The controller never touches AnalysisSessionState directly.
    ///   - A rebuild always produces a completely fresh context — no state is ever salvaged
    ///     from a previous context object.
    /// </summary>
    public sealed class AnalysisWorkspace
    {
        /// <summary>
        /// Persistent user choices. Survives rebuilds. Never cleared except on model-type switch.
        /// Single and Global state are kept separate so switching modes does not leak parameters.
        /// </summary>
        public AnalysisSessionState Session { get; } = AnalysisSessionState.CreateDefault();

        /// <summary>
        /// The most recently built analysis. Null until a successful build.
        /// Replaced entirely on every rebuild — never partially mutated.
        /// </summary>
        public AnalysisContext Context { get; private set; }
        bool reuseAttachedSolutionInitialValues = true;

        public bool IsReady => Context != null;

        /// <summary>Raised on the calling thread after every successful rebuild.</summary>
        public event EventHandler ContextRebuilt;

        /// <summary>
        /// Raised when current inputs can no longer produce a context. Consumers must
        /// clear parameter and constraint controls rather than retain an older snapshot.
        /// </summary>
        public event EventHandler ContextInvalidated;

        /// <summary>Raised when a rebuild attempt fails (e.g. data not ready).</summary>
        public event EventHandler<Exception> RebuildFailed;

        // ── Session mutations ──────────────────────────────────────────────
        // Each method updates persistent session state, then calls TryRebuild.
        // The rebuild always reads from session state, never from the previous context.

        public void SetModelType(AnalysisModel model)
        {
            if (Session.ModelType == model) return;

            // Overrides from the old model are incompatible — clear them.
            // Single and Global keep separate AnalysisState objects, so only the
            // currently-active one needs clearing.
            Session.Active.ParameterOverrides.Clear();
            Session.ModelType = model;
            TryRebuild();
        }

        public void SetGlobalMode(bool isGlobal)
        {
            if (Session.IsGlobal == isGlobal) return;
            Session.IsGlobal = isGlobal;
            // Single/Global have independent AnalysisState — no cross-contamination.
            TryRebuild();
        }

        public void SetModelOption(AttributeKey key, ExperimentAttribute option)
        {
            if (option == null) return;
            if (key == AttributeKey.SequentialSiteCount)
                PrepareSequentialStepCount(option.IntValue);
            Session.Active.ModelOptions[key] = option.Copy();
            TryRebuild();
        }

        public void ReplaceModelOptions(IDictionary<AttributeKey, ExperimentAttribute> options, bool rebuild = true)
        {
            if (Session.ModelType == AnalysisModel.SequentialBindingSites
                && options.TryGetValue(AttributeKey.SequentialSiteCount, out var incomingCountOption))
                PrepareSequentialStepCount(incomingCountOption.IntValue);

            Session.Active.ModelOptions.Clear();

            foreach (var (key, opt) in options)
                Session.Active.ModelOptions[key] = opt.Copy();

            if (rebuild)
                TryRebuild();
        }

        /// <summary>
        /// Records a user-driven parameter change. The value and lock state are stored
        /// and reapplied on every subsequent rebuild. Callers batching inspector edits
        /// can suppress the immediate rebuild and rebuild once after the batch.
        /// </summary>
        public void SetParameterOverride(ParameterType key, double value, bool isLocked, bool rebuild = true)
        {
            var overrideKey = new ParameterOverrideKey(Session.ModelType, key);

            if (!Session.Active.ParameterOverrides.TryGetValue(overrideKey, out var ov))
            {
                ov = new ParameterOverride();
                Session.Active.ParameterOverrides[overrideKey] = ov;
            }

            ov.Value = value;
            ov.IsLocked = isLocked;
            if (rebuild) TryRebuild();
        }

        /// <summary>
        /// Removes the stored override for the given parameter.
        /// The next rebuild will restore the model/data-derived default.
        /// </summary>
        public void ResetParameterOverride(ParameterType key, bool rebuild = true)
        {
            var overrideKey = new ParameterOverrideKey(Session.ModelType, key);
            Session.Active.ParameterOverrides.Remove(overrideKey);
            if (rebuild) TryRebuild();
        }

        /// <summary>
        /// Records a global constraint change and, by default, triggers a rebuild.
        /// The new constraint is reflected in the rebuilt context's GlobalModelParameters.
        /// </summary>
        public void SetConstraint(ParameterType key, VariableConstraint constraint, bool rebuild = true)
        {
            Session.Active.Constraints[key] = constraint;
            if (rebuild) TryRebuild();
        }

        /// <summary>
        /// Atomically applies one affinity- or enthalpy-family style to every active
        /// sequential step without making the individual step values equal.
        /// </summary>
        public void SetSequentialConstraintFamily(
            ParameterType familyKey,
            VariableConstraint constraint,
            bool rebuild = true)
        {
            if (Session.ModelType != AnalysisModel.SequentialBindingSites)
                throw new InvalidOperationException("Sequential constraint families require the sequential model.");

            Session.Active.SetSequentialConstraintFamily(
                familyKey,
                constraint,
                GetSequentialStepCount());
            if (rebuild) TryRebuild();
        }

        /// <summary>
        /// Stores and validates the sequential step count, removing all inactive
        /// step constraints and overrides before rebuilding.
        /// </summary>
        public void SetSequentialStepCount(int activeStepCount, bool rebuild = true)
        {
            if (Session.ModelType != AnalysisModel.SequentialBindingSites)
                throw new InvalidOperationException("Sequential step count requires the sequential model.");

            PrepareSequentialStepCount(activeStepCount);
            Session.Active.ModelOptions[AttributeKey.SequentialSiteCount] =
                ExperimentAttribute.Int(
                    AttributeKey.SequentialSiteCount,
                    AttributeKey.SequentialSiteCount.GetProperties().Name,
                    activeStepCount);
            if (rebuild) TryRebuild();
        }

        int GetSequentialStepCount()
        {
            return Session.Active.ModelOptions.TryGetValue(AttributeKey.SequentialSiteCount, out var option)
                ? option.IntValue
                : ThermodynamicParameterSlots.MinimumSequentialCount;
        }

        void PrepareSequentialStepCount(int activeStepCount)
        {
            ThermodynamicParameterSlots.ValidateSequentialCount(activeStepCount);
            if (Session.ModelType == AnalysisModel.SequentialBindingSites)
                Session.Active.ResizeSequentialSteps(
                    GetSequentialStepCount(),
                    activeStepCount);
        }

        /// <summary>
        /// Bulk-imports constraints from an external source (e.g. read back from a
        /// live GlobalModelParameters after the constraint UI mutated it directly).
        /// Does not trigger a rebuild — caller is responsible for that.
        /// </summary>
        public void ImportConstraints(IDictionary<ParameterType, VariableConstraint> constraints)
        {
            foreach (var (key, value) in constraints)
                Session.Active.Constraints[key] = value;
        }

        /// <summary>
        /// Clears saved analysis inputs for both single and global modes without changing
        /// the user's selected analysis mode or model type.
        /// </summary>
        public void ResetStoredAnalysisState()
        {
            ClearStoredState(Session.Single);
            ClearStoredState(Session.Global);

            reuseAttachedSolutionInitialValues = false;
            Context = null;
            TryRebuild();
        }

        static void ClearStoredState(AnalysisState state)
        {
            state.ModelOptions.Clear();
            state.ParameterOverrides.Clear();
            state.Constraints.Clear();
        }

        /// <summary>
        /// Copies the current context's ExposedModelOptions back into session state.
        /// Use this after a third-party view (e.g. OptionAdjustmentView) has mutated the
        /// live options dict directly, so those changes survive the next rebuild.
        /// Does not trigger a rebuild — call TryRebuild() afterward when all changes are done.
        /// </summary>
        public void SyncModelOptionsToSession()
        {
            if (!IsReady) return;

            foreach (var (key, opt) in Context.ExposedModelOptions)
                Session.Active.ModelOptions[key] = opt.Copy();
        }

        // ── Rebuild ────────────────────────────────────────────────────────

        /// <summary>
        /// Unconditionally builds a fresh context from current session state + DataManager.
        /// Raises ContextRebuilt on success, or propagates the exception on failure.
        /// </summary>
        public void Rebuild()
        {
            AnalysisContext rebuiltContext;
            try
            {
                rebuiltContext = AnalysisBuilder.Build(Session, reuseAttachedSolutionInitialValues);
            }
            catch
            {
                InvalidateContext();
                throw;
            }

            Context = rebuiltContext;

            AppEventHandler.Print(
                $"[AnalysisWorkspace] Rebuilt {Session.ModelType} ({(Session.IsGlobal ? "global" : "single")})", 0);

            ContextRebuilt?.Invoke(this, EventArgs.Empty);
        }

        void InvalidateContext()
        {
            if (Context == null) return;

            Context = null;
            ContextInvalidated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Rebuilds only if data is loaded and all relevant experiments are analysis-ready.
        /// Returns true when a rebuild actually occurred.
        /// Does not throw — failures are swallowed and optionally reported via RebuildFailed.
        /// </summary>
        public bool TryRebuild()
        {
            if (!DataManager.DataIsLoaded)
            {
                InvalidateContext();
                return false;
            }

            var requiredData = Session.IsGlobal
                ? DataManager.Data.Where(d => d.Include).ToList()
                : DataManager.Current != null
                    ? new List<ExperimentData> { DataManager.Current }
                    : new List<ExperimentData>();

            if (requiredData.Count == 0 || (Session.IsGlobal && requiredData.Count < 2) || !requiredData.All(AnalysisBuilder.IsAnalysisReady))
            {
                InvalidateContext();
                return false;
            }

            try
            {
                Rebuild();
                return true;
            }
            catch (Exception ex)
            {
                InvalidateContext();
                AppEventHandler.Print($"[AnalysisWorkspace] TryRebuild failed: {ex.Message}", 0);
                RebuildFailed?.Invoke(this, ex);
                return false;
            }
        }

        // ── Solver integration ─────────────────────────────────────────────

        /// <summary>
        /// Finalizes the context and returns a ready-to-run solver.
        /// Call this immediately before solver.Analyze() — not earlier.
        /// </summary>
        public SolverInterface PrepareForSolve()
        {
            if (Context == null)
                throw new InvalidOperationException("No analysis context available. Ensure TryRebuild() succeeded before calling PrepareForSolve().");

            if (FittingOptionsController.EnableSolverDiagnostics || AppSettings.Verbose)
            {
                AppEventHandler.PrintAndLog($"[FitDiag] Workspace prepare: mode={(Context.IsMultiExperiment ? "global" : "single")}, model={Context.ModelType}");
                foreach (var par in Context.ExposedParameters)
                {
                    AppEventHandler.PrintAndLog(
                        $"[FitDiag] Workspace parameter {par.Key}: value={par.Value:G17}, locked={par.IsLocked}, globallyDetermined={par.IsGloballyDetermined}, changedByUser={par.ChangedByUser}, fitted={par.IsFitted}",
                        1);
                }
            }

            reuseAttachedSolutionInitialValues = true;
            Context.FinalizeForSolver();
            Context.RefreshParameterLimits();
            var violations = Context.IsMultiExperiment
                ? InitialParameterLimitViolationDetector.Detect(Context.GlobalModel)
                : InitialParameterLimitViolationDetector.Detect(Context.SingleModel);
            InitialParameterLimitViolationDetector.ThrowIfAny(violations);
            return Context.CreateSolver();
        }

        /// <summary>
        /// After a successful fit, persists the solver's converged parameter values back
        /// into session state so they survive the next rebuild.
        /// Does NOT trigger a rebuild — the current context IS the fitted model.
        /// Locked user-set parameters are preserved as-is.
        /// </summary>
        public void PersistFittedParameters(AnalysisState state = null)
        {
            if (!IsReady) return;

            state ??= Session.Active;

            foreach (var par in Context.ExposedParameters)
            {
                var overrideKey = new ParameterOverrideKey(Session.ModelType, par.Key);

                if (!state.ParameterOverrides.TryGetValue(overrideKey, out var ov))
                {
                    ov = new ParameterOverride();
                    state.ParameterOverrides[overrideKey] = ov;
                }

                ov.Value = par.Value;
                ov.IsLocked = par.IsLocked;
            }
        }
    }
}
