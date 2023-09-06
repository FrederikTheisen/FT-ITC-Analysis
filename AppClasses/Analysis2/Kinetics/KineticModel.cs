using System;
using AnalysisITC.AppClasses.Analysis2.Models;

namespace AnalysisITC.AppClasses.Analysis2.Kinetics
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

