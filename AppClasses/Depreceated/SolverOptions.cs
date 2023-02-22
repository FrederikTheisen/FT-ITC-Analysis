using System.Linq;

namespace AnalysisITC
{
    public class SolverOptions
    {
        public object Model { get; set; }

        public Analysis.VariableConstraint EnthalpyStyle { get; set; } = Analysis.VariableConstraint.TemperatureDependent;
        public Analysis.VariableConstraint AffinityStyle { get; set; } = Analysis.VariableConstraint.None;
        public Analysis.VariableConstraint NStyle { get; set; } = Analysis.VariableConstraint.None;

        public int ModelCount => this.Model switch
        {
            GlobalModel => (Model as GlobalModel).Models.Count,
            _ => 1,
        };

        public double MeanTemperature => this.Model switch
        {
            GlobalModel => (Model as GlobalModel).MeanTemperature,
            _ => 25,
        };
    }
}
