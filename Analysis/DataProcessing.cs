using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using MathNet.Numerics.LinearAlgebra.Complex.Solvers;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra.Solvers;

namespace AnalysisITC
{
    public class DataProcessor
    {
        public static event EventHandler BaselineInterpolationCompleted;

        internal ExperimentData Data { get; set; }

        CancellationToken cToken { get; set; }
        CancellationTokenSource csource = new CancellationTokenSource();

        public BaselineInterpolator Interpolator { get; set; }
        public BaselineInterpolatorTypes BaselineType
        {
            get
            {
                if (Interpolator == null) return BaselineInterpolatorTypes.None;
                else
                {
                    switch (Interpolator)
                    {
                        case SplineInterpolator: return BaselineInterpolatorTypes.Spline;
                        case AssymetricLeastSquaresInterpolator: return BaselineInterpolatorTypes.ASL;
                        case PolynomialLeastSquaresInterpolator: return BaselineInterpolatorTypes.Polynomial;
                        default: return BaselineInterpolatorTypes.None;
                    }
                }
            }
        }

        public bool BaselineCompleted { get; internal set; } = false;
        public bool IntegrationCompleted => Data.Injections.All(inj => inj.IsIntegrated);


        public DataProcessor(ExperimentData data)
        {
            Data = data;

            //InitializeBaseline(BaselineInterpolatorTypes.Spline);
        }

        public DataProcessor(ExperimentData data, DataProcessor dataProcessor)
        {
            Data = data;

            Interpolator = dataProcessor.Interpolator.Copy(this);
        }

        public void InitializeBaseline(BaselineInterpolatorTypes mode)
        {
            switch (mode)
            {
                case BaselineInterpolatorTypes.None: break;
                case BaselineInterpolatorTypes.Spline: Interpolator = new SplineInterpolator(this); break;
                case BaselineInterpolatorTypes.Polynomial: Interpolator = new PolynomialLeastSquaresInterpolator(this); break;
                case BaselineInterpolatorTypes.ASL:
                default: Interpolator = new SplineInterpolator(this); break;
            }
        }

        public async void InterpolateBaseline()
        {
            try
            {
                BaselineCompleted = false;

                StatusBarManager.StartInderminateProgress();

                csource.Cancel();
                csource = new CancellationTokenSource();
                cToken = csource.Token;

                await System.Threading.Tasks.Task.Run(() => Interpolator.Interpolate(cToken));

                BaselineCompleted = true;

                BaselineInterpolationCompleted?.Invoke(this, null);

                StatusBarManager.StopInderminateProgress();
            }
            catch (Exception ex)
            {

            }
            finally
            {
                
            }
        }

        public void IterationCompleted()
        {
            BaselineInterpolationCompleted?.Invoke(this, null);
        }

        public void SubtractBaseline()
        {
            Data.BaseLineCorrectedDataPoints = new List<DataPoint>();

            foreach (var (dp,bl) in Data.DataPoints.Zip(Data.Processor.Interpolator.Baseline, (x, y) => new Tuple<DataPoint, Energy>(x, y)))
            {
                var bldp = dp.SubtractBaseline(bl);

                Data.BaseLineCorrectedDataPoints.Add(bldp);
            }
        }

        public void IntegratePeaks()
        {
            foreach (var inj in Data.Injections)
            {
                inj.Integrate();
            }
        }
    }

    public class BaselineInterpolator
    {
        internal DataProcessor Parent { get; set; }

        internal ExperimentData Data => Parent.Data;

        internal List<Energy> Baseline = new List<Energy>();

        public bool Finished => Baseline.Count > 0;

        public BaselineInterpolator(DataProcessor processor)
        {
            Parent = processor;

            Baseline = new List<Energy>();
        }

        public virtual BaselineInterpolator Copy(DataProcessor processor)
        {
            return new BaselineInterpolator(processor);
        }

        public async virtual Task Interpolate(CancellationToken token, bool replace = true)
        {
            Parent.BaselineCompleted = true;
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
        None = -1,
        Spline = 0,
        ASL = 1,
        Polynomial = 2,
    }

    public class SplineInterpolator : BaselineInterpolator
    {
        public int PointsPerInjection { get; set; } = 1;
        public float FractionBaseline { get; set; } = 0.5f;
        public SplineInterpolatorAlgorithm Algorithm { get; set; } = SplineInterpolatorAlgorithm.Akima;
        public SplineHandleMode HandleMode { get; set; } = SplineHandleMode.Mean;

        public List<SplinePoint> SplinePoints { get; private set; } = new List<SplinePoint>();

        Spline SplineFunction;

        public SplineInterpolator(DataProcessor processor) : base(processor)
        {
            
        }

        public override BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new SplineInterpolator(processor)
            {
                PointsPerInjection = this.PointsPerInjection,
                FractionBaseline = this.FractionBaseline,
                Algorithm = this.Algorithm,
                HandleMode = this.HandleMode
            };

            return interpolator;
        }

        public List<SplinePoint> GetInitialPoints(int pointperinjection = 1, float fraction = 0.8f)
        {
            var points = new List<SplinePoint>();

            int handles = 2 + pointperinjection * Data.InjectionCount;

            float maxInjVol = Data.Injections.Max(inj => inj.Volume);

            //First points
            float segmmentL = (Data.InitialDelay - 5) / 4;
            points.Add(new SplinePoint(segmmentL, GetDataRangeMean(0, 2 * segmmentL), DataPoint.Slope(Data.DataPoints.Where(dp => dp.Time > 0 && dp.Time < 2 * segmmentL).ToList())));

            points.Add(new SplinePoint(3 * segmmentL, GetDataRangeMean(2 * segmmentL, 4 * segmmentL), DataPoint.Slope(Data.DataPoints.Where(dp => dp.Time > 2 * segmmentL && dp.Time < 4 * segmmentL).ToList())));

            foreach (var inj in Data.Injections)
            {
                var _frac = 1 - (1 - fraction) / (float)Math.Sqrt(maxInjVol / inj.Volume);

                var start = inj.Time + inj.Delay * (1 - _frac);
                var length = (inj.Delay * _frac - 5) / pointperinjection;

                for (int j = 0; j < pointperinjection; j++)
                {
                    var s = start + j * length;
                    var e = s + length;

                    points.Add(new SplinePoint(s + length * 0.5f, GetDataRangeMean(s, e), DataPoint.Slope(Data.DataPoints.Where(dp => dp.Time > s && dp.Time < e).ToList())));
                }
            }

            return points;
        }

        Energy GetDataRangeMean(double start, double end)
        {
            List<DataPoint> points = Data.DataPoints.Where(dp => dp.Time > start && dp.Time < end).ToList();

            if (points.Count < 1) points.Add(Data.DataPoints.Last(dp => dp.Time < end));

            switch (HandleMode)
            {
                default:
                case SplineHandleMode.Mean: return DataPoint.Mean(points); 
                case SplineHandleMode.Median: return DataPoint.Median(points); 
                case SplineHandleMode.MinVolatility: return DataPoint.VolatilityWeightedAverage(points); 
                
            }
        }

        public void UpdatePoints(List<Tuple<double, double>> points)
        {

        }

        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            List<SplinePoint> splinePoints;

            if (SplinePoints.Count == 0 || replace) splinePoints = GetInitialPoints(PointsPerInjection, FractionBaseline);
            else splinePoints = SplinePoints;

            var x = splinePoints.Select(sp => sp.Time);
            var y = splinePoints.Select(sp => (double)sp.Power);

            Spline spline;

            switch (Algorithm)
            {
                case SplineInterpolatorAlgorithm.Akima: spline = new Spline(CubicSpline.InterpolateAkima(x, y)); break;
                case SplineInterpolatorAlgorithm.InterpolateBoundaries:
                case SplineInterpolatorAlgorithm.InterpolateHermite: spline = new Spline(CubicSpline.InterpolateHermite(x, y, splinePoints.Select(sp => sp.Slope))); break;
                case SplineInterpolatorAlgorithm.InterpolateNatural: spline = new Spline(CubicSpline.InterpolateNatural(x, y)); break;
                default:
                case SplineInterpolatorAlgorithm.InterpolatePchip: spline = new Spline(CubicSpline.InterpolatePchip(x, y)); break;
                case SplineInterpolatorAlgorithm.LinearSpline: spline = new Spline(LinearSpline.Interpolate(x, y)); break;
            }

            var bsl = new List<Energy>();

            foreach (var dp in Data.DataPoints)
            {
                bsl.Add(spline.Evaluate(dp.Time));
            }

            Baseline = bsl;
            SplinePoints = splinePoints;
            SplineFunction = spline;

            await base.Interpolate(token, replace);
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

            public Energy Evaluate(float time)
            {
                if (CubicSplineFunction != null) return new(CubicSplineFunction.Interpolate(time));
                if (LinearSplineFunction != null) return new(LinearSplineFunction.Interpolate(time));

                return new(0.0);
            }
        }

        public class SplinePoint
        {
            public double Time;
            public Energy Power;
            public double Slope;

            public SplinePoint(double time, Energy power, double slope = 0)
            {
                Time = time;
                Power = power;
                Slope = slope;
            }
        }
    }

    public class AssymetricLeastSquaresInterpolator : BaselineInterpolator
    {
        static int alg_niter = 10;
        double Lambda = 1000;
        double p = 0.96;
        double[] datapoints => Data.DataPoints.Select(dp => (double)dp.Power).ToArray();

        public AssymetricLeastSquaresInterpolator(DataProcessor processor) : base(processor)
        {

        }

        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            var y = SparseVector.OfEnumerable(datapoints);
            var L = y.Count; //len(y)
            var D = Diff(new DiagonalMatrix(L, L, 1)); //sparse.csc_matrix(np.diff(np.eye(L), 2))
            var w = new SparseVector(L).Add(1);

            var z = new SparseVector(L);

            for (int i = 0; i < alg_niter; i++)
            {
                Console.WriteLine("ASL iter: " + i);
                var W = SparseMatrix.CreateDiagonal(L, L, (o => w[o]));
                var a = SparseMatrix.OfMatrix(D * D.Transpose());
                var Z = W + Lambda * a;
                var mul = w.PointwiseMultiply(y);

                var monitor = new Iterator<double>(
                    new IterationCountStopCriterion<double>(2000),
                    new ResidualStopCriterion<double>(0.001));

                var solver = new MathNet.Numerics.LinearAlgebra.Double.Solvers.TFQMR();

                var nZ = Z.SolveIterative(mul, solver, monitor);//Solve(mul);
                z = SparseVector.OfEnumerable(nZ.Storage.ToArray());
                w = Select(z.ToList(), y.ToList());

                token.ThrowIfCancellationRequested();
            }

            //Baseline = z.Select(o => o).ToList();

            await base.Interpolate(token, replace);
        }

        MathNet.Numerics.LinearAlgebra.Double.SparseMatrix Diff(DiagonalMatrix m)
        {
            var dense = new MathNet.Numerics.LinearAlgebra.Double.SparseMatrix(m.RowCount, m.RowCount - 2);

            var rows = m.EnumerateRows().ToList();

            for (int i = 0; i < rows.Count(); i++)
            {
                var row = rows[i];
                double[] newrow = new double[row.Count() - 2];

                for (int j = 0; j < row.Count() - 2; j++)
                {
                    if (i == j) newrow[j] = 1;
                    else if (i == j + 1) newrow[j] = -2;
                    else if (i == j + 2) newrow[j] = 1;

                    //newrow[j] = (row[j + 2] - row[j + 1]) - (row[j + 1] - row[j]);
                }

                dense.SetRow(i, newrow);
            }
            return dense;
        }

        DenseVector Select(List<double> z, List<double> y)
        {
            var w = new DenseVector(z.Count);

            for (int i = 0; i < z.Count(); i++)
            {
                if (z[i] < y[i]) w[i] = p;
                else w[i] = (1 - p);
            }

            return w;
        }
    }

    public class PolynomialLeastSquaresInterpolator : BaselineInterpolator
    {
        double[] fit;

        public int Degree { get; set; } = 8;
        public double ZLimit { get; set; } = 2;

        public PolynomialLeastSquaresInterpolator(DataProcessor processor) : base(processor)
        {
        }

        public override BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new PolynomialLeastSquaresInterpolator(processor)
            {
                Degree = this.Degree,
                ZLimit = this.ZLimit,
            };

            return interpolator;
        }

        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            var x = Data.DataPoints.Select(dp => (double)dp.Time).ToArray();
            var y = Data.DataPoints.Select(dp => (double)dp.Power).ToArray();

            var fit = MathNet.Numerics.Fit.Polynomial(x, y, Degree);
            var line = LineFromFit(fit, x);

            var previousRSoS = 1.0;

            var r = ResidualSumOfSquares(line, y);

            while (r > double.Epsilon && Math.Abs(previousRSoS - r) > double.Epsilon)
            {
                var residues = Residuals(line, y);

                var s = Math.Sqrt(r / (line.Length - 1));
                var avg = residues.Average();

                var Zscores = residues.Select(v => Math.Abs(v - avg) / s).ToArray();
                previousRSoS = r;

                x = x.Where((v, idx) => IdxToTime(idx) < Data.InitialDelay || IdxToTime(idx) > Data.Injections.Last().IntegrationEndTime || Zscores[idx] < ZLimit).ToArray();
                y = y.Where((v, idx) => IdxToTime(idx) < Data.InitialDelay || IdxToTime(idx) > Data.Injections.Last().IntegrationEndTime || Zscores[idx] < ZLimit).ToArray();

                fit = MathNet.Numerics.Fit.Polynomial(x, y, Degree);
                line = LineFromFit(fit, x);

                r = ResidualSumOfSquares(line, y);

                token.ThrowIfCancellationRequested();
            }

            this.fit = fit;

            Baseline = Evaluate().Select(e => new Energy(e)).ToList();

            await base.Interpolate(token, replace);
        }

        double[] Residuals(double[] fit, double[] dat)
        {
            double[] res = new double[fit.Length];

            for (int i = 0; i < fit.Length; i++)
            {
                var v1 = fit[i];
                var v2 = dat[i];

                res[i] = v1 - v2;
            }

            return res;
        }

        double ResidualSumOfSquares(double[] fit, double[] dat)
        {
            var sum = 0.0;
            var res = Residuals(fit, dat);

            foreach (var r in res) sum += r * r;

            return sum;
        }

        double[] LineFromFit(double[] fit, double[] x)
        {
            int order = fit.Length - 1;

            double[] line = new double[x.Length];

            for (int i = 0; i < x.Length; i++)
            {
                var xval = x[i];
                var yval = 0.0;

                for (int e = 0; e <= order; e++)
                {
                    yval += fit[e] * Math.Pow(xval, e);
                }

                line[i] = yval;
            }

            return line;
        }

        double IdxToTime(int idx)
        {
            return Data.DataPoints[idx].Time;
        }

        double[] Evaluate()
        {
            double[] eval = new double[Data.DataPoints.Count];
            var data = Data.DataPoints.Select(dp => dp.Time).ToArray();

            for (int i = 0; i < eval.Length; i++)
            {
                eval[i] = MathNet.Numerics.Polynomial.Evaluate(data[i], this.fit);
            }

            return eval;
        }
    }
}
