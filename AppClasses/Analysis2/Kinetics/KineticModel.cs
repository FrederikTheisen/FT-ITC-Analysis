using System;
using AnalysisITC.AppClasses.AnalysisClasses.Models;

namespace AnalysisITC.AppClasses.AnalysisClasses.Kinetics
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

