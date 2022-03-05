using System;
using DataReaders;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public static class GlobalAnalyzer
    {

    }

    public class Analyzer
    {
        public static List<ExperimentData> Data => DataManager.Data;


        public static void ProcessData()
        {
            float f = 0.6f;


            //var interpolator = new SplineInterpolator(Data[0]) { FractionBaseline = f, HandleMode = SplineInterpolator.SplineHandleMode.Mean };
            //interpolator.Interpolate();

            //var interpolator2 = new SplineInterpolator(Data[0]) { FractionBaseline = f, HandleMode = SplineInterpolator.SplineHandleMode.Median };
            //interpolator2.Interpolate();

            //var interpolator3 = new SplineInterpolator(Data[0]) { FractionBaseline = f, HandleMode = SplineInterpolator.SplineHandleMode.Median, Algorithm = SplineInterpolator.SplineInterpolatorAlgorithm.LinearSpline };
            //interpolator3.Interpolate();

            //for (int i = 0; i < Data[0].DataPoints.Count; i++)
            //{
            //    Console.WriteLine(Data[0].DataPoints[i].Time + " "
            //        + Data[0].DataPoints[i].Power + " "
            //        + interpolator.Baseline[i] + " "
            //        + interpolator2.Baseline[i] + " "
            //        + interpolator3.Baseline[i]);
            //}
        }
    }
}
