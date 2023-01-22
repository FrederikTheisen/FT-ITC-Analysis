using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SolverFoundation.Services;

namespace AnalysisITC.AppClasses.Analysis2
{
	public class GlobalModel
	{
		public List<Model> Models { get; set; }

		public GlobalModelParameters Parameters { get; set; }

		public double LossFunction(double[] parameters)
		{
			Parameters.UpdateFromArray(parameters);

			return Loss();
		}

		public double Loss()
		{
			double loss = 0;

			foreach (var model in Models)
				loss += model.LossFunction(Parameters.GetParametersForModel(this, model).ToArray());

			return loss;
		}
	}

	public class Model
    {
		public ExperimentData Data { get; set; }
		public AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;
		public ModelParameters Parameters { get; set; }

		public int NumberOfParameters => Parameters.FittingParameterCount;

		public virtual double GuessEnthalpy()
		{
			return Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset();
		}
		public virtual double GuessOffset()
		{
			return Data.Injections.Where(inj => inj.Include).TakeLast(2).Average(inj => inj.Enthalpy);
		}
		public virtual double GuessN()
		{
			return Data.Injections.Last().Ratio / 2;
		}
		public virtual double GuessAffinity() => 1000000;

		public Model(ExperimentData data)
		{
			Data = data;
		}

		public virtual double Evaluate(int injectionindex)
		{
			throw new NotImplementedException();
		}

		public double LossFunction(double[] parameters)
		{
			Parameters.UpdateFromArray(parameters);

			return Loss();
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
	}

	public class OneSetOfSites : Model
	{
		public OneSetOfSites(ExperimentData data) : base(data)
		{
		}

		public override double Evaluate(int injectionindex)
		{
			return GetDeltaHeat(injectionindex, Parameters.Table[ParameterTypes.Nvalue1].Value, Parameters.Table[ParameterTypes.Enthalpy1].Value, Parameters.Table[ParameterTypes.Affinity1].Value) + Parameters.Table[ParameterTypes.Offset].Value * Data.Injections[injectionindex].InjectionMass; ;
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
	}

	public class TwoSetsOfSites : Model
	{
		public TwoSetsOfSites(ExperimentData data) : base(data)
		{
			throw new NotImplementedException("TwoSetsOfSites not implemented yet");
		}
	}

}

