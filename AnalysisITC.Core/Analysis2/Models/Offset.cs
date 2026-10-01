using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Analysis.Models
{
    /// <summary>Constant dilution heat proportional to injected amount.</summary>
    public sealed class Offset : Model
    {
        public override AnalysisModel ModelType => AnalysisModel.Offset;

        public Offset(ExperimentData data) : base(data) { }

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);
            Parameters.AddOrUpdateParameter(ParameterType.Offset, PreviousOrDefault(ParameterType.Offset, 0));
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
            => withoffset
                ? Parameters.Table[ParameterType.Offset].Value
                    * Data.Injections.First(injection => injection.ID == injectionindex).InjectionMass
                : 0;

        internal override Model GenerateSyntheticModel(Random random)
            => GenerateSyntheticModel(random, ModelCloneOptions);

        internal override Model GenerateSyntheticModel(Random random, ModelCloneOptions options)
        {
            var model = new Offset(Data.GetSynthClone(options, random));
            SetSynthModelParameters(model, random, options);
            return model;
        }

        public sealed class ModelSolution : SolutionInterface
        {
            public ModelSolution(Model model)
            {
                Model = model;
                BootstrapSolutions = new List<SolutionInterface>();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);
                if (info.HasFlag(FinalFigureDisplayParameters.Offset))
                    output.Add(new("Offset", Offset.ToFormattedString(ReportEnergyUnit, permole: true)));
                return output;
            }

            public override Dictionary<ParameterType, FloatWithError> ReportParameters => new()
            {
                { ParameterType.Offset, Parameters[ParameterType.Offset] },
            };
        }
    }
}
