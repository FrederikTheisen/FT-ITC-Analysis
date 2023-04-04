using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class TwoSetsOfSites : Model
	{
		public TwoSetsOfSites(ExperimentData data) : base(data)
		{
			throw new NotImplementedException("TwoSetsOfSites not implemented yet");
		}

		public override void InitializeParameters(ExperimentData data)
		{
            base.InitializeParameters(data);

            Parameters.AddParameter(ParameterTypes.Nvalue1, this.GuessN());
            Parameters.AddParameter(ParameterTypes.Enthalpy1, this.GuessEnthalpy() / 2);
            Parameters.AddParameter(ParameterTypes.Affinity1, this.GuessAffinity());
            Parameters.AddParameter(ParameterTypes.Nvalue2, this.GuessN());
            Parameters.AddParameter(ParameterTypes.Enthalpy2, this.GuessEnthalpy() / 2);
            Parameters.AddParameter(ParameterTypes.Affinity2, this.GuessAffinity());
            Parameters.AddParameter(ParameterTypes.Offset, this.GuessOffset());
        }

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new TwoSetsOfSites(Data.GetSynthClone(ModelCloneOptions));

            foreach (var par in Parameters.Table)
            {
                mdl.Parameters.AddParameter(par.Key, par.Value.Value, par.Value.IsLocked);
            }

            return mdl;
        }

		public class ModelSolution : SolutionInterface
		{
            public new List<ModelSolution> BootstrapSolutions { get; set; }

            public Energy Enthalpy1 => new(Parameters[ParameterTypes.Enthalpy1]);
            public Energy Enthalpy2 => new(Parameters[ParameterTypes.Enthalpy2]);
            public FloatWithError K1 => Parameters[ParameterTypes.Affinity1];
            public FloatWithError K2 => Parameters[ParameterTypes.Affinity2];
            public FloatWithError N1 => Parameters[ParameterTypes.Nvalue1];
            public FloatWithError N2 => Parameters[ParameterTypes.Nvalue2];
            public Energy Offset => new(Parameters[ParameterTypes.Offset]);

            public FloatWithError Kd1 => new FloatWithError(1) / K1;
            public Energy GibbsFreeEnergy1 => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K1));
            public Energy TdS1 => GibbsFreeEnergy1 - Enthalpy1;
            public Energy Entropy1 => TdS1 / TempKelvin;

            public FloatWithError Kd2 => new FloatWithError(1) / K2;
            public Energy GibbsFreeEnergy2 => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K2));
            public Energy TdS2 => GibbsFreeEnergy2 - Enthalpy2;
            public Energy Entropy2 => TdS2 / TempKelvin;

            public ModelSolution(Model model, double[] parameters)
            {
                Model = model;
            }

            public override void ComputeErrorsFromBootstrapSolutions()
            {
                var enthalpies1 = BootstrapSolutions.Select(s => s.Enthalpy1.FloatWithError.Value);
                var enthalpies2 = BootstrapSolutions.Select(s => s.Enthalpy2.FloatWithError.Value);
                var k1 = BootstrapSolutions.Select(s => s.K1.Value);
                var k2 = BootstrapSolutions.Select(s => s.K2.Value);
                var n1 = BootstrapSolutions.Select(s => s.N1.Value);
                var n2 = BootstrapSolutions.Select(s => s.N2.Value);
                var offsets = BootstrapSolutions.Select(s => (double)s.Offset);

                Parameters[ParameterTypes.Enthalpy1] = new FloatWithError(enthalpies1, Enthalpy1);
                Parameters[ParameterTypes.Affinity1] = new FloatWithError(k1, K1);
                Parameters[ParameterTypes.Nvalue1] = new FloatWithError(n1, N1);
                Parameters[ParameterTypes.Enthalpy1] = new FloatWithError(enthalpies2, Enthalpy2);
                Parameters[ParameterTypes.Affinity1] = new FloatWithError(k2, K2);
                Parameters[ParameterTypes.Nvalue1] = new FloatWithError(n2, N2);
                Parameters[ParameterTypes.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                output.Add(new("Kd1", Kd1.ToString()));
                output.Add(new("∆H1", Enthalpy1.ToString()));

                return output;
            }
        }
    }
}

