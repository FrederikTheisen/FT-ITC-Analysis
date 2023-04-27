using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AnalysisITC.AppClasses.AnalysisClasses.ResultAnalysis
{
    public class ResultAnalysis
    {
        internal static Random Rand { get; } = new Random();

        public AnalysisResult Result { get; private set; }
        public List<Tuple<double, FloatWithError>> DataPoints;
        public LinearFitWithError LinearFitWithError { get; set; }

        public int CompletedIterations { get; internal set; } = 0;

        public ResultAnalysis(AnalysisResult result)
        {
            Result = result;
        }

        public virtual async Task Calculate()
        {
        }
    }
}

