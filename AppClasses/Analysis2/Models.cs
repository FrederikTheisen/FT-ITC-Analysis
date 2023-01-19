using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class GlobalModel
    {
        public List<Model> Models { get; set; }

        public double Loss()
        {
            double loss = 0;

            foreach (var model in Models)
                loss += model.Loss();

            return loss;
        }
    }

    public class Model
	{
		public ExperimentData Data { get; set; }

        public AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;

        public ModelParameters Parameters { get; set; }

        public int NumberOfParameters => Parameters.Length;

        public Model(ExperimentData data)
        {
            Data = data;
        }

        public virtual double Evaluate(int injectionindex)
		{
			throw new NotImplementedException();
		}

        public double Loss()
        {
            double loss = 0;

            foreach (var inj in Data.Injections.Where(i => i.Include))
            {
                var diff = Evaluate(inj.ID) - inj.PeakArea;
                loss += diff * diff;
            }

            return Math.Sqrt(loss / Data.Injections.Count(i => i.Include));
        }

        public virtual void InitializeParameters(ModelParameters parameters = null)
        {
            //Overwrite with predefined parameters
            if (parameters != null) foreach (var par in parameters.Table) Parameters.Table[par.Key] = par.Value;

            Console.WriteLine("Parameters for model: " + ModelType.ToString() + " initialized");
            foreach (var par in Parameters.Table)
            {
                Console.WriteLine(par.Key + ": " + par.Value.Value.ToString("G3") + "  " + par.Value.IsLocked.ToString());
            }
        }
    }

    public class OneSetOfSites : Model
    {
        public OneSetOfSites(ExperimentData data) : base(data)
        {
        }

        public override double Evaluate(int injectionindex)
        {
            return GetDeltaHeat(injectionindex, Parameters.Table["N"].Value, Parameters.Table["H"].Value, Parameters.Table["K"].Value) + Parameters.Table["Offset"].Value * Data.Injections[injectionindex].InjectionMass; ;
        }

        public double GetDeltaHeat(int i, double n, double H, double K)
        {
            var inj = Data.Injections[i];
            var Qi = GetHeatContent(inj, n, H, K);
            var Q_i = 0.0;

            if (i != 0) Q_i = GetHeatContent(Data.Injections[i - 1], n, H, K);

            var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2.0) - Q_i;

            return dQi;
        }

        public double GetHeatContent(InjectionData inj, double n, double H, double K)
        {
            var ncell = n * inj.ActualCellConcentration;
            var first = (ncell * H * Data.CellVolume) / 2.0;
            var XnM = inj.ActualTitrantConcentration / ncell;
            var nKM = 1.0 / (K * ncell);
            var square = (1.0 + XnM + nKM);
            var root = (square * square) - 4.0 * XnM;

            return first * (1 + XnM + nKM - Math.Sqrt(root));
        }

        public override void InitializeParameters(ModelParameters parameters = null)
        {
            var GuessOffset = Data.Injections.Where(inj => inj.Include).TakeLast(2).Average(inj => inj.Enthalpy);
            var GuessN = Data.Injections.Last().Ratio / 2;
            var GuessH = Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset;
            var GuessK = 1000000;

            Parameters.AddParameter("N", GuessN);
            Parameters.AddParameter("H", GuessH);
            Parameters.AddParameter("K", GuessK);
            Parameters.AddParameter("Offset", GuessOffset);

            base.InitializeParameters();
        }
    }

}

