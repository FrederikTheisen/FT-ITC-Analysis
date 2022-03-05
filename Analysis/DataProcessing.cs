using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.Interpolation;


namespace AnalysisITC
{
    public class DataProcessor
    {
        public event EventHandler InterpolationCompleted;

        internal ExperimentData Data { get; set; }

        public BaselineInterpolator Interpolator { get; set; }
        public BaselineInterpolatorTypes BaselineType { get; set; }

        public DataProcessor(ExperimentData data)
        {
            Data = data;

            InitializeBaseline(BaselineInterpolatorTypes.Spline);
        }

        public void InitializeBaseline(BaselineInterpolatorTypes mode)
        {
            switch (mode)
            {
                case BaselineInterpolatorTypes.Spline: Interpolator = new SplineInterpolator(this); break;
                case BaselineInterpolatorTypes.ASL:
                default: Interpolator = new SplineInterpolator(this); break;
            }
        }

        public void InterpolateBaseline()
        {
            Interpolator.Interpolate();

            InterpolationCompleted?.Invoke(this, null);
        }

        void SubtractBaseline()
        {

        }
    }

    public class BaselineInterpolator
    {
        DataProcessor Parent { get; set; }

        internal ExperimentData Data => Parent.Data;

        internal List<float> Baseline;

        public bool Finished => Baseline.Count > 0;

        public BaselineInterpolator(DataProcessor processor)
        {
            Parent = processor;

            Baseline = new List<float>();
        }

        public virtual void Interpolate()
        {

        }

        public void WriteToConsole()
        {
            for (int i = 0; i < Data.DataPoints.Count; i++)
            {
                Console.WriteLine(Data.DataPoints[i].Time + " " + Data.DataPoints[i].Power + " " + Baseline[i]);
            }
        }
    }

    public enum BaselineInterpolatorTypes
    {
        Spline = 0,
        ASL = 1,
    }

    public class SplineInterpolator : BaselineInterpolator
    {
        public int PointsPerInjection { get; set; } = 1;
        public float FractionBaseline { get; set; } = 0.5f;
        public SplineInterpolatorAlgorithm Algorithm { get; set; } = SplineInterpolatorAlgorithm.Akima;
        public SplineHandleMode HandleMode { get; set; } = SplineHandleMode.Mean;

        public List<Tuple<double, double, double>> SplinePoints { get; private set; }

        Spline SplineFunction;

        public SplineInterpolator(DataProcessor processor) : base(processor)
        {
            SplinePoints = new List<Tuple<double, double, double>>();
        }

        public void GetInitialPoints(int pointperinjection = 1, float fraction = 0.8f)
        {
            int handles = 2 + pointperinjection * Data.InjectionCount;

            float maxInjVol = Data.Injections.Max(inj => inj.Volume);

            //First points
            float segmmentL = (Data.InitialDelay - 5) / 4;
            SplinePoints.Add(new Tuple<double, double, double>(segmmentL,
                                                               GetDataRangeMean(0, 2 * segmmentL),
                                                               DataPoint.Slope(Data.DataPoints.Where(dp => dp.Time > 0 && dp.Time < 2 * segmmentL).ToList())));

            SplinePoints.Add(new Tuple<double, double, double>(3 * segmmentL,
                                                   GetDataRangeMean(2 * segmmentL, 4 * segmmentL),
                                                   DataPoint.Slope(Data.DataPoints.Where(dp => dp.Time > 2 * segmmentL && dp.Time < 4 * segmmentL).ToList())));

            foreach (var inj in Data.Injections)
            {
                var _frac = 1 - (1 - fraction) / (float)Math.Sqrt(maxInjVol / inj.Volume);

                var start = inj.Time + inj.Delay * (1 - _frac);
                var length = (inj.Delay * _frac - 5) / pointperinjection;

                for (int j = 0; j < pointperinjection; j++)
                {
                    var s = start + j * length;
                    var e = s + length;

                    SplinePoints.Add(new Tuple<double, double, double>(s + length * 0.5, GetDataRangeMean(s, e), DataPoint.Slope(Data.DataPoints.Where(dp => dp.Time > s && dp.Time < e).ToList())));
                }
            }
        }

        float GetDataRangeMean(float start, float end)
        {
            switch (HandleMode)
            {
                default:
                case SplineHandleMode.Mean: return DataPoint.Mean(Data.DataPoints.Where(dp => dp.Time > start && dp.Time < end).ToList());
                case SplineHandleMode.Median: return DataPoint.Median(Data.DataPoints.Where(dp => dp.Time > start && dp.Time < end).ToList());
                case SplineHandleMode.MinVolatility: return DataPoint.VolatilityWeightedAverage(Data.DataPoints.Where(dp => dp.Time > start && dp.Time < end).ToList());
                
            }


        }

        public void UpdatePoints(List<Tuple<float, float>> points)
        {

        }

        public override void Interpolate()
        {
            if (SplinePoints.Count == 0) GetInitialPoints(PointsPerInjection, FractionBaseline);

            
            var x = SplinePoints.Select(sp => sp.Item1);
            var y = SplinePoints.Select(sp => sp.Item2);

            for (int i = 0; i < x.Count(); i++) Console.WriteLine(x.ToList()[i] + " " + y.ToList()[i]);

            switch (Algorithm)
            {
                case SplineInterpolatorAlgorithm.Akima: SplineFunction = new Spline(CubicSpline.InterpolateAkima(x, y)); break;
                case SplineInterpolatorAlgorithm.InterpolateBoundaries:
                case SplineInterpolatorAlgorithm.InterpolateHermite: SplineFunction = new Spline(CubicSpline.InterpolateHermite(x, y, SplinePoints.Select(sp => sp.Item3))); break;
                case SplineInterpolatorAlgorithm.InterpolateNatural: SplineFunction = new Spline(CubicSpline.InterpolateNatural(x, y)); break;
                default:
                case SplineInterpolatorAlgorithm.InterpolatePchip: SplineFunction = new Spline(CubicSpline.InterpolatePchip(x, y)); break;
                case SplineInterpolatorAlgorithm.LinearSpline: SplineFunction = new Spline(LinearSpline.Interpolate(x, y)); break;
            }

            

            foreach (var dp in Data.DataPoints)
            {
                Baseline.Add((float)SplineFunction.Evaluate(dp.Time));
            }

            base.Interpolate();
        }

        public enum SplineInterpolatorAlgorithm
        {
            Akima,
            InterpolateBoundaries,
            InterpolateHermite,
            InterpolateNatural,
            InterpolatePchip,
            LinearSpline
        }

        public enum SplineHandleMode
        {
            Mean,
            Median,
            MinVolatility
        }

        private class Spline
        {
            CubicSpline CubicSplineFunction = null;
            LinearSpline LinearSplineFunction = null;

            public Spline(CubicSpline spline)
            {
                CubicSplineFunction = spline;
            }

            public Spline(LinearSpline spline)
            {
                LinearSplineFunction = spline;
            }

            public float Evaluate(float time)
            {
                if (CubicSplineFunction != null) return (float)CubicSplineFunction.Interpolate(time);
                if (LinearSplineFunction != null) return (float)LinearSplineFunction.Interpolate(time);

                return 0;
            }
        }
    }
}
