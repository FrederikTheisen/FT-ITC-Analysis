using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    internal static class ParameterFitPresentation
    {
        internal static string GetStatus(AnalysisResult result, SolutionInterface solution, ParameterType parameter)
        {
            if (IsDerivedParameter(parameter)) return "Derived";
            var key = parameter == ParameterType.ApparentAffinity ? ParameterType.Affinity1 : parameter;
            var global = result?.Model?.Parameters;
            if (global != null)
            {
                var constraint = global.GetConstraintForParameter(key);
                if (constraint != VariableConstraint.None)
                {
                    if (constraint == VariableConstraint.ThermodynamicallyLinked
                        && key.GetProperties().ParentType == ParameterType.Affinity1)
                        return "Derived";
                    var coordinates = GlobalConstraintSemantics.CoordinateKeys(key, constraint);
                    if (coordinates.Count > 0)
                        return coordinates.All(coordinate => global.GlobalTable.TryGetValue(coordinate, out var value) && value.IsLocked)
                            ? "Fixed" : "Fitted";
                    if (global.GlobalTable.TryGetValue(key, out var shared))
                        return shared.IsLocked ? "Fixed" : "Fitted";
                }
            }
            return solution?.Model?.Parameters?.Table != null
                && solution.Model.Parameters.Table.TryGetValue(key, out var local) && local.IsLocked
                ? "Fixed" : "Fitted";
        }

        static bool IsDerivedParameter(ParameterType parameter)
        {
            var parent = parameter.GetProperties().ParentType;
            return parent == ParameterType.Gibbs1
                || parent == ParameterType.Entropy1
                || parent == ParameterType.EntropyContribution1
                || parent == ParameterType.HeatCapacity1;
        }
    }
}
