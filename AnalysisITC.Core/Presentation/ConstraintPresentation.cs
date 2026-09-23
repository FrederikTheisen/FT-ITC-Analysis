using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Human-readable constraint text that follows the parameter's meaning.</summary>
    public static class ConstraintPresentation
    {
        public static string Description(ParameterType parameter, VariableConstraint constraint)
        {
            if (ThermodynamicParameterSlots.TryResolve(parameter, out _, out var family)
                && family == ThermodynamicParameterFamily.Affinity)
            {
                return constraint switch
                {
                    VariableConstraint.None => "Independent",
                    VariableConstraint.TemperatureDependent => "Shared ΔG",
                    VariableConstraint.SameForAll => "Shared Kd",
                    VariableConstraint.ThermodynamicallyLinked => "Thermodynamically linked",
                    _ => constraint.GetEnumDescription(),
                };
            }
            return constraint.GetEnumDescription();
        }

        public static string Tooltip(ParameterType parameter, VariableConstraint constraint)
        {
            if (ThermodynamicParameterSlots.TryResolve(parameter, out _, out var family)
                && family == ThermodynamicParameterFamily.Affinity)
            {
                return constraint switch
                {
                    VariableConstraint.None => "Each experiment has its own fitted affinity.",
                    VariableConstraint.TemperatureDependent => "One Gibbs-energy coordinate is shared; Kd varies with temperature.",
                    VariableConstraint.SameForAll => "One Kd value is shared across all experiments.",
                    VariableConstraint.ThermodynamicallyLinked => "Gibbs energy is shared at the fit reference and follows the selected enthalpy relationship.",
                    _ => Description(parameter, constraint),
                };
            }
            return Description(parameter, constraint);
        }
    }
}
