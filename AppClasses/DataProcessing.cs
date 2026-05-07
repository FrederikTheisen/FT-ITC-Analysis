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
        public static event EventHandler ProcessingCompleted;

        internal ExperimentData Data { get; set; }
        public bool IsLocked { get; private set; } = false;

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
        public bool DiscardIntegratedPoints { get; set; } = true;
        public InjectionData.IntegrationLengthMode IntegrationLengthMode { get; set; } = InjectionData.IntegrationLengthMode.Time;
        public float IntegrationLengthFactor { get; set; } = 2;

        public bool BaselineCompleted { get; internal set; } = false;
        public bool IntegrationCompleted => Data.Injections.All(inj => inj.IsIntegrated);


        public DataProcessor(ExperimentData data)
        {
            Data = data;

            DiscardIntegratedPoints = AppSettings.DiscardIntegrationRegionForBaseline;
        }

        public DataProcessor(ExperimentData data, DataProcessor dataProcessor)
        {
            Data = data;
            DiscardIntegratedPoints = dataProcessor.DiscardIntegratedPoints;
            IntegrationLengthFactor = dataProcessor.IntegrationLengthFactor;
            IntegrationLengthMode = dataProcessor.IntegrationLengthMode;
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

        public async Task ProcessData(bool replace = true, bool invalidate = true, bool showProgress = true)
        {
            if (BaselineType == BaselineInterpolatorTypes.None) return;

            if (showProgress) StatusBarManager.StartInderminateProgress();

            this.WillProcessData(invalidate);
            await this.InterpolateBaseline(replace);
            this.IntegratePeaks(invalidate);
            this.DidProcessData(invalidate);

            if (showProgress) StatusBarManager.StopIndeterminateProgress();
        }

        public void Lock() => IsLocked = true;
        public void Unlock() => IsLocked = false;
        public void ToggleLock() => IsLocked = !IsLocked;

        public void WillProcessData(bool invalidate = true)
        {
            BaselineCompleted = false;

            Data.Injections.ForEach(inj => inj.IsIntegrated = false); //FIXME Crashes if not on UI thread
            Data.UpdateProcessing(invalidate);
        }

        public async Task InterpolateBaseline(bool replace = true)
        {
            try
            {
                csource.Cancel();
                csource = new CancellationTokenSource();
                cToken = csource.Token;

                await Task.Run(() => Interpolator.Interpolate(cToken, replace));

                SubtractBaseline();

                BaselineCompleted = true;
                BaselineInterpolationCompleted?.Invoke(this, null);
            }
            catch (Exception ex)
            {
                AppEventHandler.PrintAndLog("Baseline Interpolation Error");
                AppEventHandler.PrintAndLog(ex.Message);
                AppEventHandler.PrintAndLog(ex.StackTrace);
            }
        }

        public void DidProcessData(bool invalidate = true)
        {
            Data.UpdateProcessing(invalidate);

            ProcessingCompleted?.Invoke(Data, null);
        }

        public void SubtractBaseline()
        {
            Data.BaseLineCorrectedDataPoints = new List<DataPoint>();

            foreach (var (dp,bl) in Data.DataPoints.Zip(Interpolator.Baseline, (x, y) => new Tuple<DataPoint, Energy>(x, y)))
            {
                var bldp = dp.SubtractBaseline((float)bl);

                Data.BaseLineCorrectedDataPoints.Add(bldp);
            }

            Data.CalculateExperimentHeatDirection();
        }

        public void IntegratePeaks(bool invalidate = true)
        {
            if (Data.BaseLineCorrectedDataPoints == null || Data.BaseLineCorrectedDataPoints.Count == 0) return;

            try
            {
                foreach (var inj in Data.Injections)
                {
                    inj.Integrate();
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }

            Data.UpdateProcessing(invalidate);

            ProcessingCompleted?.Invoke(Data, null);
        }
    }

    public class BaselineInterpolator
    {
        public DataProcessor Processor { get; set; }
        internal List<Energy> Baseline { get; set; } = new List<Energy>();
        public bool IsLocked => Processor.IsLocked;
        
        internal ExperimentData Data => Processor.Data;
        public SplineInterpolator SplineInterpolator => this as SplineInterpolator;
        public PolynomialLeastSquaresInterpolator PolynomialLeastSquaresInterpolator => this as PolynomialLeastSquaresInterpolator;

        public bool Finished => Baseline.Count > 0;

        public BaselineInterpolator(DataProcessor processor)
        {
            Processor = processor;
            Processor.Unlock();

            Baseline = new List<Energy>();
        }

        public virtual BaselineInterpolator Copy(DataProcessor processor)
        {
            return new BaselineInterpolator(processor);
        }

        public List<DataPoint> GetInterpolatedDataPoints(double start, double end)
        {
            var datapoints = Data.DataPoints.Where(dp => dp.Time >= start && dp.Time <= end);

            if (Processor.DiscardIntegratedPoints)
            {
                foreach (var inj in Data.Injections)
                {
                    datapoints = datapoints.Where(dp => dp.Time < inj.IntegrationStartTime || dp.Time > inj.IntegrationEndTime);
                }
            }

            return datapoints.ToList();
        }

        public async virtual Task Interpolate(CancellationToken token, bool replace = true)
        {

        }

        public void WriteToConsole()
        {
            for (int i = 0; i < Data.DataPoints.Count; i++)
            {
                Console.WriteLine(Data.DataPoints[i].Time + " " + Data.DataPoints[i].Power + " " + Baseline[i]);
            }
        }

        public void ConvertToSpline(int pointdensity = 2)
        {
            if (this is SplineInterpolator) return;
            pointdensity = Math.Max(1, pointdensity);

            int num_of_points = (Data.InjectionCount + 1) * pointdensity;

            int skip = Math.Max(1, Baseline.Count / (num_of_points + 1));

            var interpolator = new SplineInterpolator(Processor)
            {
                PointsPerInjection = pointdensity
            };
            
            //interpolator.IsLocked = true;

            int k = 0;
            for (int i = skip; i < Baseline.Count - 1; i += skip)
            {
                var time = Data.DataPoints[i].Time;
                var val = Baseline[i].Value;
                var slope = (Baseline[i + 1].Value - Baseline[i - 1].Value) / 2;

                interpolator.SplinePoints.Add(new SplineInterpolator.SplinePoint(time, val, k, slope));
                k++;
            }

            Processor.Interpolator = interpolator;
            _ = Processor.ProcessData();
            Processor.Lock();
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
        public const int MinimumPointsPerInjection = 1;
        public const int MaximumPointsPerInjection = 8;
        static int defaultPointsPerInjection = 2;

        int pointsPerInjection = DefaultPointsPerInjection;

        public static int DefaultPointsPerInjection
        {
            get => defaultPointsPerInjection;
            set => defaultPointsPerInjection = ClampPointsPerInjection(value);
        }

        public int PointsPerInjection
        {
            get => pointsPerInjection;
            set => pointsPerInjection = ClampPointsPerInjection(value);
        }

        public float FractionBaseline { get; set; } = 0.9f;
        public SplineInterpolatorAlgorithm Algorithm { get; set; } = SplineInterpolatorAlgorithm.Smooth;
        public SplineHandleMode HandleMode { get; set; } = SplineHandleMode.Mean;

        public List<SplinePoint> SplinePoints { get; private set; } = new List<SplinePoint>();

        Spline SplineFunction;

        public SplineInterpolator(DataProcessor processor) : base(processor)
        {
            
        }

        static int ClampPointsPerInjection(int value) => Math.Min(MaximumPointsPerInjection, Math.Max(MinimumPointsPerInjection, value));

        public override BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new SplineInterpolator(processor)
            {
                PointsPerInjection = this.PointsPerInjection,
                FractionBaseline = this.FractionBaseline,
                Algorithm = this.Algorithm,
                HandleMode = this.HandleMode,
            };

            return interpolator;
        }

        public List<SplinePoint> GetInitialPoints(int pointperinjection = 1, double fraction = 0.8f)
        {
            var points = new List<SplinePoint>();
            pointperinjection = Math.Max(1, pointperinjection);
            var maxInjVol = Data.Injections.Max(inj => inj.Volume);

            //First points
            var segmmentL = (Data.InitialDelay - 5) / 4;
            points.Add(new SplinePoint(segmmentL, GetDataRangeMean(0, 2 * segmmentL), 0, SplineSlope(segmmentL, 0, 2 * segmmentL)));
            points.Add(new SplinePoint(3 * segmmentL, GetDataRangeMean(2 * segmmentL, 4 * segmmentL), points.Count, SplineSlope(3 * segmmentL, 2 * segmmentL, 4 * segmmentL)));

            foreach (var inj in Data.Injections)
            {
                var _frac = inj.Volume > 0 && maxInjVol > 0 ? 1 - (1 - fraction) / Math.Sqrt(maxInjVol / inj.Volume) : fraction;

                var start = inj.Time + inj.Delay * (1 - _frac);
                var end = inj.Time + inj.Delay - 5;

                if (Processor.DiscardIntegratedPoints)
                {
                    if (start < inj.IntegrationEndTime) start = inj.IntegrationEndTime;
                }

                if (end <= start) start = Math.Max(inj.Time, end - Data.TimeStep);
                var length = (end - start) / pointperinjection;

                for (int j = 0; j < pointperinjection; j++)
                {
                    var s = start + j * length;
                    var e = s + length;
                    var time = (s + e) / 2;

                    double slope = SplineSlope(time, s, e);

                    points.Add(new SplinePoint(time, GetDataRangeMean(s, e), points.Count, slope));
                }
            }

            return points;
        }

        double SplineSlope(double time, double s = 0, double e = 1) => DataPoint.Slope(GetInterpolatedDataPoints(s, e));

        double GetDataRangeMean(double start, double end)
        {
            List<DataPoint> points = GetInterpolatedDataPoints(start, end);

            if (points.Count < 1) points.Add(Data.DataPoints.Last(dp => dp.Time < end));

            switch (HandleMode)
            {
                default:
                case SplineHandleMode.Mean: return DataPoint.Mean(points); 
                case SplineHandleMode.Median: return DataPoint.Median(points); 
                case SplineHandleMode.MinVolatility: return DataPoint.VolatilityWeightedAverage(points); 
                
            }
        }

        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            await base.Interpolate(token, replace);

            List<SplinePoint> splinePoints;

            if (SplinePoints.Count == 0 || (replace && !IsLocked)) splinePoints = MergeLockedSplinePoints(GetInitialPoints(PointsPerInjection, FractionBaseline));
            else splinePoints = SplinePoints;

            var x = splinePoints.Select(sp => sp.Time);
            var y = splinePoints.Select(sp => (double)sp.Power);

            Spline spline;

            switch (Algorithm)
            {
                default:
                case SplineInterpolatorAlgorithm.Linear: spline = new Spline(LinearSpline.Interpolate(x, y)); break;
                case SplineInterpolatorAlgorithm.Rigid: spline = new Spline(CubicSpline.InterpolatePchip(x, y)); break;
                case SplineInterpolatorAlgorithm.Smooth: spline = new Spline(CubicSpline.InterpolateHermite(x, y, SmoothSplineSlopes(splinePoints))); break;
                case SplineInterpolatorAlgorithm.Handles: spline = new Spline(CubicSpline.InterpolateHermite(x, y, splinePoints.Select(s => s.Slope))); break;
            }

            var bsl = new List<Energy>();

            foreach (var dp in Data.DataPoints)
            {
                bsl.Add(spline.Evaluate(dp.Time));
            }

            Baseline = bsl;
            SplinePoints = splinePoints;
            SplineFunction = spline;
        }

        public void RemoveSplinePoint(int id)
        {
            SplinePoints.RemoveAt(id);

            SortAndRenumberSplinePoints();

            _ = Processor.ProcessData(false);
        }

        public void InsertSplinePoint(double cursorpos, bool usedatavalue = false)
        {
            if (Baseline.Count == 0) return;

            double pointValue;
            if (usedatavalue) pointValue = cursorpos > Data.DataPoints.Last().Time ? Data.DataPoints.Last().Power : Data.DataPoints.First(dp => dp.Time > cursorpos).Power;
            else  pointValue = SplineFunction.Evaluate((float)cursorpos);

            var newsp = new SplinePoint(cursorpos, pointValue, 0) { Locked = true, UserDefined = true };

            // Insert and order by time
            SplinePoints.Add(newsp);
            SortAndRenumberSplinePoints();

            _ = Processor.ProcessData(false);
        }

        List<SplinePoint> MergeLockedSplinePoints(List<SplinePoint> generatedPoints)
        {
            foreach (var lockedPoint in SplinePoints.Where(sp => sp.Locked))
            {
                if (!lockedPoint.UserDefined && lockedPoint.ID >= 0 && lockedPoint.ID < generatedPoints.Count)
                    generatedPoints[lockedPoint.ID] = lockedPoint;
                else
                    generatedPoints.Add(lockedPoint);
            }

            return SortAndRenumberSplinePoints(generatedPoints);
        }

        double[] SmoothSplineSlopes(List<SplinePoint> points)
        {
            if (points.Count < 2) return points.Select(_ => 0.0).ToArray();

            var slopes = new double[points.Count - 1];
            for (int i = 0; i < slopes.Length; i++)
            {
                var dx = points[i + 1].Time - points[i].Time;
                slopes[i] = Math.Abs(dx) > double.Epsilon ? (points[i + 1].Power - points[i].Power) / dx : 0;
            }

            var tangents = new double[points.Count];
            tangents[0] = SmoothEndpointSlope(slopes, 0);
            tangents[points.Count - 1] = SmoothEndpointSlope(slopes, slopes.Length - 1);

            for (int i = 1; i < points.Count - 1; i++)
            {
                var prevSlope = slopes[i - 1];
                var nextSlope = slopes[i];

                if (prevSlope * nextSlope <= 0)
                {
                    tangents[i] = 0;
                    continue;
                }

                var prevDx = points[i].Time - points[i - 1].Time;
                var nextDx = points[i + 1].Time - points[i].Time;
                var totalDx = prevDx + nextDx;
                var weightedAverage = Math.Abs(totalDx) > double.Epsilon ? (nextDx * prevSlope + prevDx * nextSlope) / totalDx : 0;
                var limit = 3 * Math.Min(Math.Abs(prevSlope), Math.Abs(nextSlope));

                tangents[i] = Math.Sign(weightedAverage) * Math.Min(Math.Abs(weightedAverage), limit);
            }

            return tangents;
        }

        double SmoothEndpointSlope(double[] slopes, int index)
        {
            if (slopes.Length == 1) return slopes[index];
            var slope = slopes[index];
            var neighbor = index == 0 ? slopes[1] : slopes[index - 1];

            if (slope * neighbor <= 0) return 0;

            return Math.Sign(slope) * Math.Min(Math.Abs(slope), 3 * Math.Abs(neighbor));
        }

        public void SetSplinePoints(List<SplinePoint> points)
        {
            SplinePoints = SortAndRenumberSplinePoints(points);
        }

        List<SplinePoint> SortAndRenumberSplinePoints(List<SplinePoint> points)
        {
            var sorted = points.OrderBy(sp => sp.Time).ToList();
            sorted.ForEach(sp => sp.ID = sorted.IndexOf(sp));

            return sorted;
        }

        void SortAndRenumberSplinePoints()
        {
            SplinePoints = SortAndRenumberSplinePoints(SplinePoints);
        }

        public enum SplineInterpolatorAlgorithm
        {
            Smooth,
            Handles,
            Rigid,
            Linear
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

            public double Slope(double time)
            {
                if (CubicSplineFunction != null) return CubicSplineFunction.Differentiate(time);
                if (LinearSplineFunction != null) return LinearSplineFunction.Differentiate(time);

                else return 0;
            }
        }

        public class SplinePoint
        {
            /// <summary>
            /// Mouse over feature relevant ID
            /// </summary>
            public int ID;
            public double Time;
            public double Power;
            public double Slope;
            public bool Locked;
            public bool UserDefined;

            public SplinePoint(double time, double power, int id, double slope = 0)
            {
                Time = time;
                Power = power;
                ID = id;
                Slope = slope;
            }

            public void Lock() => Locked = true;
            public void Unlock() => Locked = false;
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

        SparseMatrix Diff(DiagonalMatrix m)
        {
            var dense = new SparseMatrix(m.RowCount, m.RowCount - 2);

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

        public int Degree { get; set; } = 12;
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
            await base.Interpolate(token, replace);

            //Arrays of time and power datapoints
            var x = Data.DataPoints.Select(dp => (double)dp.Time).ToArray();
            var y = Data.DataPoints.Select(dp => (double)dp.Power).ToArray();

            if (Processor.DiscardIntegratedPoints)
            {
                foreach (var inj in Data.Injections)
                {
                    y = y.Where((v, idx) => x[idx] < inj.IntegrationStartTime || x[idx] > inj.IntegrationEndTime).ToArray();
                    x = x.Where((v, idx) => v < inj.IntegrationStartTime || v > inj.IntegrationEndTime).ToArray();
                }
            }

            var fit = MathNet.Numerics.Fit.Polynomial(x, y, Degree);
            var line = LineFromFit(fit, x);

            var previousRSoS = 1.0;

            var r = ResidualSumOfSquares(line, y);

            while (r > double.Epsilon && Math.Abs(previousRSoS - r) > double.Epsilon)
            {
                var residuals = Residuals(line, y);

                var s = Math.Sqrt(r / (line.Length - 1));
                var avg = residuals.Average();

                var Zscores = residuals.Select(v => Math.Abs(v - avg) / s).ToArray();
                previousRSoS = r;

                //x = x.Where((v, idx) => IdxToTime(idx) < Data.InitialDelay || IdxToTime(idx) > Data.Injections.Last().IntegrationEndTime || Zscores[idx] < ZLimit).ToArray();
                //y = y.Where((v, idx) => IdxToTime(idx) < Data.InitialDelay || IdxToTime(idx) > Data.Injections.Last().IntegrationEndTime || Zscores[idx] < ZLimit).ToArray();

                y = y.Where((v, idx) => Zscores[idx] < ZLimit).ToArray();
                x = x.Where((v, idx) => Zscores[idx] < ZLimit).ToArray();

                fit = MathNet.Numerics.Fit.Polynomial(x, y, Degree);
                line = LineFromFit(fit, x);

                r = ResidualSumOfSquares(line, y);

                token.ThrowIfCancellationRequested();
            }

            this.fit = fit;

            Baseline = Evaluate().Select(e => new Energy(e)).ToList();
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
