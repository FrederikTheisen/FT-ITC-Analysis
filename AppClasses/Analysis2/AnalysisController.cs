using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses.Models;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    /// <summary>
    /// The AnalysisCoordinator orchestrates the analysis workflow. It owns the persistent
    /// AnalysisSessionState and the currently built AnalysisContext. Controllers should
    /// delegate user interactions to the coordinator rather than manipulating models
    /// directly. The coordinator in turn delegates model construction to the
    /// AnalysisBuilder.
    /// </summary>
    public class AnalysisController
    {
        public AnalysisSessionState SessionState { get; }

        public AnalysisContext CurrentContext { get; private set; }

        public AnalysisController()
        {
            SessionState = AnalysisSessionState.Current;
        }

        public void SetModelType(AnalysisModel modelType)
        {
            if (SessionState.ModelType == modelType) return;

            var keysToRemove = SessionState.Active.ParameterOverrides
                .Where(kvp => kvp.Key.Model != modelType)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                SessionState.Active.ParameterOverrides.Remove(key);
            }

            SessionState.ModelType = modelType;
        }

        public void SetGlobalMode(bool isGlobal)
        {
            SessionState.IsGlobal = isGlobal;
        }

        public void ApplyParameterOverride(ParameterType key, double value, bool locked)
        {
            var overrideKey = new ParameterOverrideKey(SessionState.ModelType, key);
            SessionState.Active.ParameterOverrides[overrideKey] = new ParameterOverride
            {
                Value = value,
                IsLocked = locked
            };
        }

        public void ResetParameterOverride(ParameterType key)
        {
            var overrideKey = new ParameterOverrideKey(SessionState.ModelType, key);
            SessionState.Active.ParameterOverrides.Remove(overrideKey);
        }

        public void SetModelOption(AttributeKey key, ExperimentAttribute option)
        {
            SessionState.Active.ModelOptions[key] = option;
        }

        public void SetConstraint(ParameterType key, VariableConstraint constraint)
        {
            SessionState.Active.Constraints[key] = constraint;
        }

        public ParameterOverride GetParameterOverride(ParameterType key)
        {
            var overrideKey = new ParameterOverrideKey(SessionState.ModelType, key);
            if (SessionState.Active.ParameterOverrides.TryGetValue(overrideKey, out var ov))
            {
                return ov;
            }

            return null;
        }

        public void BuildContext(IList<ExperimentData> experiments)
        {
            if (experiments == null || experiments.Count == 0)
            {
                CurrentContext = null;
                return;
            }

            if (SessionState.IsGlobal)
            {
                CurrentContext = AnalysisBuilder.Build(SessionState, experiments);
            }
            else
            {
                CurrentContext = AnalysisBuilder.Build(SessionState, new[] { experiments[0] });
            }
        }

        public bool IsModelAvailable(AnalysisModel modelType, bool isGlobal, IEnumerable<ExperimentData> experiments = null)
        {
            if (isGlobal && (experiments == null || experiments.Count() < 2)) return false;

            return true;
        }
    }
}
