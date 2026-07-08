using System;
using AnalysisITC.Core.Analysis.Models;


namespace AnalysisITC.Core.Analysis.Kinetics
{
	public class KineticModel
	{
		public Model Model { get; private set; }

		public KineticModel(Model model)
		{
			Model = model;
		}
	}
}

