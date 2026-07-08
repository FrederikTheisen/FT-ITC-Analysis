using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathNet.Numerics.Interpolation;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra.Solvers;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Processing
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
                        case SegmentedBaselineInterpolator: return BaselineInterpolatorTypes.Segmented;
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
            BaselineCompleted = dataProcessor.BaselineCompleted;
            if (dataProcessor.Interpolator != null) Interpolator = dataProcessor.Interpolator.Copy(this);
            if (dataProcessor.IsLocked) Lock();
        }

        public void InitializeBaseline(BaselineInterpolatorTypes mode)
        {
            switch (mode)
            {
                case BaselineInterpolatorTypes.None: break;
                case BaselineInterpolatorTypes.Spline: Interpolator = new SplineInterpolator(this); break;
                case BaselineInterpolatorTypes.Polynomial: Interpolator = new PolynomialLeastSquaresInterpolator(this); break;
                case BaselineInterpolatorTypes.Segmented: Interpolator = new SegmentedBaselineInterpolator(this); break;
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
        public List<Energy> Baseline { get; set; } = new List<Energy>();
        public bool IsLocked => Processor.IsLocked;
        
        internal ExperimentData Data => Processor.Data;
        public SplineInterpolator SplineInterpolator => this as SplineInterpolator;
        public PolynomialLeastSquaresInterpolator PolynomialLeastSquaresInterpolator => this as PolynomialLeastSquaresInterpolator;
        public SegmentedBaselineInterpolator SegmentedBaselineInterpolator => this as SegmentedBaselineInterpolator;

        public bool Finished => Baseline.Count > 0;

        public BaselineInterpolator(DataProcessor processor)
        {
            Processor = processor;
            Processor.Unlock();

            Baseline = new List<Energy>();
        }

        public virtual BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new BaselineInterpolator(processor);
            interpolator.CopyBaselineFrom(this);

            return interpolator;
        }

        protected void CopyBaselineFrom(BaselineInterpolator interpolator)
        {
            Baseline = interpolator.Baseline.ToList();
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
                PointsPerInjection = pointdensity,
                Algorithm = SplineInterpolator.PolynomialToSplineConversionTargetAlgorithm
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
        Segmented = 3,
    }

    public class SegmentedBaselineInterpolator : BaselineInterpolator
    {
        public const int MinimumDegree = 0;
        public const int MaximumDegree = 2;

        int degree = 1;

        public int Degree
        {
            get => degree;
            set => degree = ClampDegree(value);
        }

        public List<BaselineSegment> Segments { get; private set; } = new List<BaselineSegment>();

        public SegmentedBaselineInterpolator(DataProcessor processor) : base(processor)
        {
        }

        public static int ClampDegree(int value) => Math.Min(MaximumDegree, Math.Max(MinimumDegree, value));

        public override BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new SegmentedBaselineInterpolator(processor)
            {
                Degree = this.Degree,
            };

            interpolator.Segments = Segments.Select(segment => segment.Copy()).ToList();
            interpolator.CopyBaselineFrom(this);

            return interpolator;
        }

        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            await base.Interpolate(token, replace);

            Segments = CreateSegments(token);
            Baseline = Data.DataPoints.Select(dp => new Energy(EvaluateBaseline(dp.Time))).ToList();
        }

        List<BaselineSegment> CreateSegments(CancellationToken token)
        {
            var segments = new List<BaselineSegment>();

            if (Data.DataPoints.Count == 0) return segments;

            var firstTime = Data.DataPoints.First().Time;
            var lastTime = Data.DataPoints.Last().Time;

            if (Data.Injections.Count == 0)
            {
                segments.Add(FitSegment(BaselineSegmentKind.InitialDelay, -1, firstTime, lastTime));
                return segments;
            }

            var firstInjection = Data.Injections.First();
            segments.Add(FitSegment(
                BaselineSegmentKind.InitialDelay,
                -1,
                firstTime,
                Math.Min(lastTime, firstInjection.IntegrationStartTime)));

            token.ThrowIfCancellationRequested();

            for (int i = 0; i < Data.Injections.Count; i++)
            {
                var injection = Data.Injections[i];
                var nextInjection = i < Data.Injections.Count - 1 ? Data.Injections[i + 1] : null;
                var start = Math.Max(firstTime, injection.IntegrationEndTime);
                var end = nextInjection != null ? nextInjection.IntegrationStartTime : lastTime;

                if (end <= start)
                {
                    var scopeEnd = injection.Time + injection.Delay;
                    end = scopeEnd > start ? scopeEnd : start;
                }

                segments.Add(FitSegment(
                    BaselineSegmentKind.InjectionScope,
                    injection.ID,
                    Math.Min(start, lastTime),
                    Math.Min(Math.Max(end, start), lastTime)));

                token.ThrowIfCancellationRequested();
            }

            return segments;
        }

        BaselineSegment FitSegment(BaselineSegmentKind kind, int injectionID, double start, double end)
        {
            if (end < start) (start, end) = (end, start);

            var points = GetSegmentDataPoints(start, end);
            var center = 0.5 * (start + end);

            if (points.Count == 0)
                return new BaselineSegment(kind, injectionID, start, end, center, new[] { 0.0 });

            var fitDegree = Math.Min(Degree, points.Count - 1);
            if (fitDegree < MinimumDegree)
                return new BaselineSegment(kind, injectionID, start, end, center, new[] { points.Average(dp => (double)dp.Power) });

            var x = points.Select(dp => (double)dp.Time - center).ToArray();
            var y = points.Select(dp => (double)dp.Power).ToArray();
            var coefficients = MathNet.Numerics.Fit.Polynomial(x, y, fitDegree);

            return new BaselineSegment(kind, injectionID, start, end, center, coefficients);
        }

        List<DataPoint> GetSegmentDataPoints(double start, double end)
        {
            var points = GetInterpolatedDataPoints(start, end);

            if (points.Count == 0)
                points = Data.DataPoints.Where(dp => dp.Time >= start && dp.Time <= end).ToList();

            if (points.Count == 0)
            {
                var center = 0.5 * (start + end);
                points = Data.DataPoints
                    .OrderBy(dp => Math.Abs(dp.Time - center))
                    .Take(1)
                    .ToList();
            }

            return points.OrderBy(dp => dp.Time).ToList();
        }

        double EvaluateBaseline(double time)
        {
            if (Segments.Count == 0) return 0;

            for (int i = 0; i < Data.Injections.Count; i++)
            {
                var injection = Data.Injections[i];
                if (time < injection.IntegrationStartTime || time > injection.IntegrationEndTime) continue;

                var left = SegmentBeforeInjection(i);
                var right = SegmentForInjection(injection.ID);

                return BlendSegments(left, right, time, injection.IntegrationStartTime, injection.IntegrationEndTime);
            }

            var containingSegment = Segments.FirstOrDefault(segment => segment.Contains(time));
            if (containingSegment != null) return containingSegment.Evaluate(time);

            var nearestPrevious = Segments.LastOrDefault(segment => segment.StartTime <= time);
            if (nearestPrevious != null) return nearestPrevious.Evaluate(time);

            return Segments.First().Evaluate(time);
        }

        BaselineSegment SegmentBeforeInjection(int injectionIndex)
        {
            if (injectionIndex <= 0)
                return Segments.FirstOrDefault(segment => segment.Kind == BaselineSegmentKind.InitialDelay) ?? Segments.First();

            return SegmentForInjection(Data.Injections[injectionIndex - 1].ID);
        }

        BaselineSegment SegmentForInjection(int injectionID)
        {
            return Segments.FirstOrDefault(segment => segment.Kind == BaselineSegmentKind.InjectionScope && segment.InjectionID == injectionID)
                ?? Segments.Last();
        }

        static double BlendSegments(BaselineSegment left, BaselineSegment right, double time, double start, double end)
        {
            if (left == null && right == null) return 0;
            if (left == null) return right.Evaluate(time);
            if (right == null) return left.Evaluate(time);
            if (end <= start) return right.Evaluate(time);

            var weight = Math.Min(1, Math.Max(0, (time - start) / (end - start)));

            return (1 - weight) * left.Evaluate(time) + weight * right.Evaluate(time);
        }

        public enum BaselineSegmentKind
        {
            InitialDelay,
            InjectionScope,
        }

        public class BaselineSegment
        {
            public BaselineSegmentKind Kind { get; }
            public int InjectionID { get; }
            public double StartTime { get; }
            public double EndTime { get; }
            public double CenterTime { get; }
            public double[] Coefficients { get; }
            public int Degree => Math.Max(0, Coefficients.Length - 1);

            public BaselineSegment(BaselineSegmentKind kind, int injectionID, double startTime, double endTime, double centerTime, double[] coefficients)
            {
                Kind = kind;
                InjectionID = injectionID;
                StartTime = startTime;
                EndTime = endTime;
                CenterTime = centerTime;
                Coefficients = coefficients ?? new[] { 0.0 };
            }

            public bool Contains(double time) => time >= StartTime && time <= EndTime;

            public double Evaluate(double time)
            {
                var x = time - CenterTime;
                var value = 0.0;
                var power = 1.0;

                foreach (var coefficient in Coefficients)
                {
                    value += coefficient * power;
                    power *= x;
                }

                return value;
            }

            public BaselineSegment Copy()
            {
                return new BaselineSegment(Kind, InjectionID, StartTime, EndTime, CenterTime, Coefficients.ToArray());
            }
        }
    }

    public class SplineInterpolator : BaselineInterpolator
    {
        public const int MinimumPointsPerInjection = 1;
        public const int MaximumPointsPerInjection = 8;
        const double SmoothSplinePenalty = 1;
        const double DefaultSplinePointWeight = 200.0;
        const double LockedSplinePointWeight = 1000.0;
        static int defaultPointsPerInjection = 2;
        static SplinePointDensity defaultPointDensity = SplinePointDensity.Balanced;

        SplineInterpolatorAlgorithm algorithm = SplineInterpolatorAlgorithm.Smooth;
        int pointsPerInjection = DefaultPointsPerInjection;

        public static SplineInterpolatorAlgorithm PolynomialToSplineConversionTargetAlgorithm { get; set; } = SplineInterpolatorAlgorithm.Rigid;
        public static double ExpectedBaselineFractionForSplinePointSpacing { get; set; } = 0.5;
        public static double AdditionalSplinePointSpacingFraction { get; set; } = 0.75;
        public static int MaximumAdditionalSplinePointsPerInjection { get; set; } = 1;
        public static double LockedSplinePointPlacementMarginFraction { get; set; } = 1.0 / 3.0;
        public static SplineHandleMode DefaultHandleMode { get; set; } = SplineHandleMode.Mean;
        public static bool DefaultAllowPointTimeDragging { get; set; } = false;

        public static SplinePointDensity DefaultPointDensity
        {
            get => defaultPointDensity;
            set
            {
                defaultPointDensity = value;
                DefaultPointsPerInjection = PointsPerInjectionForDensity(value);
            }
        }

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

        public SplineInterpolatorAlgorithm Algorithm
        {
            get => algorithm;
            set
            {
                if (value == SplineInterpolatorAlgorithm.Handles)
                {
                    algorithm = SplineInterpolatorAlgorithm.Smooth;
                    ShowHandles = true;
                }
                else algorithm = value;
            }
        }

        public SplinePointDensity PointDensity { get; set; } = SplinePointDensity.Balanced;
        public bool ShowHandles { get; set; } = false;
        public bool AllowPointTimeDragging { get; set; } = false;
        public SplineHandleMode HandleMode { get; set; } = SplineHandleMode.Mean;

        public List<SplinePoint> SplinePoints { get; private set; } = new List<SplinePoint>();

        Spline SplineFunction;

        public SplineInterpolator(DataProcessor processor) : base(processor)
        {
            PointDensity = DefaultPointDensity;
            PointsPerInjection = DefaultPointsPerInjection;
            HandleMode = DefaultHandleMode;
            AllowPointTimeDragging = DefaultAllowPointTimeDragging;
        }

        static int ClampPointsPerInjection(int value) => Math.Min(MaximumPointsPerInjection, Math.Max(MinimumPointsPerInjection, value));

        public void ApplyPointDensity()
        {
            PointsPerInjection = PointsPerInjectionForDensity(PointDensity);
        }

        public static int PointsPerInjectionForDensity(SplinePointDensity density)
        {
            return ClampPointsPerInjection((int)density + 1);
        }

        public override BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new SplineInterpolator(processor)
            {
                PointsPerInjection = this.PointsPerInjection,
                Algorithm = this.Algorithm,
                PointDensity = this.PointDensity,
                ShowHandles = this.ShowHandles,
                AllowPointTimeDragging = this.AllowPointTimeDragging,
                HandleMode = this.HandleMode,
            };

            interpolator.SetSplinePoints(SplinePoints.Select(sp => sp.Copy()).ToList());
            interpolator.CopyBaselineFrom(this);
            interpolator.RebuildSplineFunctionFromCurrentSplinePoints();

            return interpolator;
        }

        public List<SplinePoint> GetInitialPoints(int pointperinjection = 1)
        {
            var points = new List<SplinePoint>();
            pointperinjection = Math.Max(1, pointperinjection);

            //First points
            var segmmentL = (Data.InitialDelay - 5) / 4;
            points.Add(new SplinePoint(segmmentL, GetDataRangeMean(0, 2 * segmmentL), 0, SplineSlope(segmmentL, 0, 2 * segmmentL)));
            points.Add(new SplinePoint(3 * segmmentL, GetDataRangeMean(2 * segmmentL, 4 * segmmentL), points.Count, SplineSlope(3 * segmmentL, 2 * segmmentL, 4 * segmmentL)));

            foreach (var inj in Data.Injections)
            {
                var start = inj.Time;
                var end = inj.Time + inj.Delay - 5;

                if (Processor.DiscardIntegratedPoints)
                {
                    if (start < inj.IntegrationEndTime) start = inj.IntegrationEndTime;
                }

                if (end <= start) start = (float)Math.Max(inj.Time, end - Data.TimeStep);

                var baselineLength = Math.Max(end - start, Data.TimeStep);
                var pointCount = GetAutomaticSplinePointCount(inj.Delay, baselineLength, pointperinjection);
                var length = baselineLength / pointCount;

                for (int j = 0; j < pointCount; j++)
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

        int GetAutomaticSplinePointCount(double injectionDelay, double baselineLength, int requestedPoints)
        {
            requestedPoints = Math.Max(1, requestedPoints);

            var expectedBaselineFraction = Math.Max(double.Epsilon, ExpectedBaselineFractionForSplinePointSpacing);
            var expectedBaselineLength = Math.Max(Data.TimeStep, injectionDelay * expectedBaselineFraction);
            var expectedPointSpacing = expectedBaselineLength / requestedPoints;

            if (double.IsNaN(expectedPointSpacing) || double.IsInfinity(expectedPointSpacing) || expectedPointSpacing <= double.Epsilon)
                return requestedPoints;

            var scaledPointCount = baselineLength / expectedPointSpacing;
            if (double.IsNaN(scaledPointCount) || double.IsInfinity(scaledPointCount) || scaledPointCount <= double.Epsilon)
                return 1;

            var pointCount = (int)Math.Floor(scaledPointCount);
            var additionalPointThreshold = Math.Min(1, Math.Max(0, AdditionalSplinePointSpacingFraction));

            if (scaledPointCount - pointCount >= additionalPointThreshold) pointCount++;

            var maxPointCount = Math.Min(MaximumPointsPerInjection, requestedPoints + Math.Max(0, MaximumAdditionalSplinePointsPerInjection));

            return Math.Min(maxPointCount, Math.Max(1, pointCount));
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

            if (SplinePoints.Count == 0 || (replace && !IsLocked)) splinePoints = MergeLockedSplinePoints(GetInitialPoints(PointsPerInjection));
            else splinePoints = SplinePoints;

            UpdateAutomaticSplineSlopes(splinePoints);
            var spline = CreateSpline(splinePoints);

            Baseline = Data.DataPoints.Select(dp => spline.Evaluate(dp.Time)).ToList();
            SplinePoints = splinePoints;
            SplineFunction = spline;
        }

        public void RefreshBaselineFromCurrentSplinePoints()
        {
            if (SplinePoints.Count == 0) return;

            UpdateAutomaticSplineSlopes(SplinePoints);
            var spline = CreateSpline(SplinePoints);
            Baseline = Data.DataPoints.Select(dp => spline.Evaluate(dp.Time)).ToList();
            SplineFunction = spline;
        }

        void RebuildSplineFunctionFromCurrentSplinePoints()
        {
            if (SplinePoints.Count == 0) return;

            SplineFunction = CreateSpline(SplinePoints);
        }

        Spline CreateSpline(List<SplinePoint> splinePoints)
        {
            var sortedPoints = splinePoints.OrderBy(sp => sp.Time).ToList();
            var x = sortedPoints.Select(sp => sp.Time);
            var y = sortedPoints.Select(sp => (double)sp.Power);

            switch (Algorithm)
            {
                default:
                case SplineInterpolatorAlgorithm.Linear: return new Spline(LinearSpline.Interpolate(x, y));
                case SplineInterpolatorAlgorithm.Rigid: return new Spline(CubicSpline.InterpolatePchip(x, y));
                case SplineInterpolatorAlgorithm.Smooth: return new Spline(CubicSpline.InterpolateHermite(x, y, sortedPoints.Select(s => s.Slope)), sortedPoints);
            }
        }

        public void RemoveSplinePoint(int id)
        {
            SplinePoints.RemoveAt(id);

            SortAndRenumberSplinePoints();

            _ = Processor.ProcessData(false);
        }

        public void MoveSplinePoint(int id, double time, double power)
        {
            if (id < 0 || id >= SplinePoints.Count) return;

            var point = SplinePoints[id];
            point.Time = time;
            point.Power = power;
            point.Lock();
            SortAndRenumberSplinePoints();
            RefreshBaselineFromCurrentSplinePoints();
        }

        public void SetSplinePointSlope(int id, double slope)
        {
            if (id < 0 || id >= SplinePoints.Count) return;

            var point = SplinePoints[id];
            point.Slope = slope;
            point.Lock();
            point.LockSlope();
            RefreshBaselineFromCurrentSplinePoints();
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
            var placementMargin = GetLockedSplinePointPlacementMargin(generatedPoints);

            foreach (var lockedPoint in SplinePoints.Where(sp => sp.Locked))
            {
                var hasCloseGeneratedPoint = generatedPoints.Any(gp => !gp.Locked && IsWithinPlacementMargin(gp, lockedPoint, placementMargin));

                if (hasCloseGeneratedPoint)
                {
                    generatedPoints.RemoveAll(gp => !gp.Locked && IsWithinPlacementMargin(gp, lockedPoint, placementMargin));
                    generatedPoints.Add(lockedPoint);
                }
                else if (!lockedPoint.UserDefined && lockedPoint.ID >= 0 && lockedPoint.ID < generatedPoints.Count)
                {
                    generatedPoints[lockedPoint.ID] = lockedPoint;
                }
                else
                {
                    generatedPoints.Add(lockedPoint);
                }
            }

            return SortAndRenumberSplinePoints(generatedPoints);
        }

        double GetLockedSplinePointPlacementMargin(List<SplinePoint> generatedPoints)
        {
            var expectedPointDistance = GetExpectedGeneratedSplinePointDistance(generatedPoints);
            var margin = Math.Max(0, LockedSplinePointPlacementMarginFraction) * expectedPointDistance;

            return double.IsNaN(margin) || double.IsInfinity(margin) ? 0 : margin;
        }

        double GetExpectedGeneratedSplinePointDistance(List<SplinePoint> generatedPoints)
        {
            var spacings = generatedPoints
                .Select(sp => sp.Time)
                .OrderBy(time => time)
                .Zip(generatedPoints.Select(sp => sp.Time).OrderBy(time => time).Skip(1), (left, right) => right - left)
                .Where(spacing => spacing > double.Epsilon)
                .OrderBy(spacing => spacing)
                .ToList();

            if (spacings.Count == 0) return Math.Max(Data.TimeStep, double.Epsilon);

            return spacings[spacings.Count / 2];
        }

        static bool IsWithinPlacementMargin(SplinePoint generatedPoint, SplinePoint lockedPoint, double placementMargin)
        {
            return Math.Abs(generatedPoint.Time - lockedPoint.Time) <= placementMargin;
        }

        void UpdateAutomaticSplineSlopes(List<SplinePoint> points)
        {
            if (Algorithm != SplineInterpolatorAlgorithm.Smooth) return;
            if (points.Count < 2) return;

            var guideSpline = CreatePenalizedSmoothingSpline(points);
            foreach (var point in points.Where(point => !point.SlopeLocked))
            {
                point.Slope = guideSpline.Slope(point.Time);
            }

            ApplyLinearSegmentSlopes(points);
        }

        void ApplyLinearSegmentSlopes(List<SplinePoint> points)
        {
            var sortedPoints = points.OrderBy(sp => sp.Time).ToList();

            for (int i = 0; i < sortedPoints.Count - 1; i++)
            {
                if (!IsLinearSegment(sortedPoints, i)) continue;

                var slope = SplinePointSegmentSlope(sortedPoints[i], sortedPoints[i + 1]);
                sortedPoints[i].Slope = slope;
                sortedPoints[i + 1].Slope = slope;
            }
        }

        static bool IsLinearSegment(List<SplinePoint> points, int index) => points[index].Linear && points[index + 1].Linear;

        static double SplinePointSegmentSlope(SplinePoint left, SplinePoint right)
        {
            var dx = right.Time - left.Time;

            return Math.Abs(dx) > double.Epsilon ? (right.Power - left.Power) / dx : 0;
        }

        Spline CreatePenalizedSmoothingSpline(List<SplinePoint> points)
        {
            if (points.Count < 3)
            {
                var x = points.Select(sp => sp.Time);
                var y = points.Select(sp => (double)sp.Power);
                if (points.Count < 2) return new Spline(points.Count == 0 ? 0 : points[0].Power);
                return new Spline(CubicSpline.InterpolatePchip(x, y));
            }

            var sortedPoints = points.OrderBy(sp => sp.Time).ToList();
            var xValues = sortedPoints.Select(sp => sp.Time).ToArray();
            var yValues = sortedPoints.Select(sp => (double)sp.Power).ToArray();
            var fitMatrix = DenseMatrix.Create(sortedPoints.Count, sortedPoints.Count, 0);
            var fitTarget = DenseVector.Create(sortedPoints.Count, i =>
            {
                var weight = SplinePointFitWeight(sortedPoints[i]);
                return weight * yValues[i];
            });

            for (int i = 0; i < sortedPoints.Count; i++)
            {
                fitMatrix[i, i] = SplinePointFitWeight(sortedPoints[i]);
            }

            // Penalize curvature by adding lambda * D'D for the second-difference operator.
            for (int i = 1; i < sortedPoints.Count - 1; i++)
            {
                fitMatrix[i - 1, i - 1] += SmoothSplinePenalty;
                fitMatrix[i - 1, i] -= 2 * SmoothSplinePenalty;
                fitMatrix[i - 1, i + 1] += SmoothSplinePenalty;
                fitMatrix[i, i - 1] -= 2 * SmoothSplinePenalty;
                fitMatrix[i, i] += 4 * SmoothSplinePenalty;
                fitMatrix[i, i + 1] -= 2 * SmoothSplinePenalty;
                fitMatrix[i + 1, i - 1] += SmoothSplinePenalty;
                fitMatrix[i + 1, i] -= 2 * SmoothSplinePenalty;
                fitMatrix[i + 1, i + 1] += SmoothSplinePenalty;
            }

            var smoothedValues = fitMatrix.Solve(fitTarget).ToArray();

            return new Spline(CubicSpline.InterpolateNaturalSorted(xValues, smoothedValues));
        }

        double SplinePointFitWeight(SplinePoint point) => point.Locked || point.UserDefined ? LockedSplinePointWeight : DefaultSplinePointWeight;

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
            Smooth = 0,
            Handles = 1,
            Rigid = 2,
            Linear = 3,
        }

        public enum SplinePointDensity
        {
            Sparse = 0,
            Balanced = 1,
            Dense = 2,
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
            double? ConstantFunction = null;
            double[] SegmentTimes = null;
            double[] SegmentPowers = null;
            bool[] LinearSegments = null;

            public Spline(CubicSpline spline)
            {
                CubicSplineFunction = spline;
            }

            public Spline(CubicSpline spline, List<SplinePoint> points) : this(spline)
            {
                SegmentTimes = points.Select(sp => sp.Time).ToArray();
                SegmentPowers = points.Select(sp => sp.Power).ToArray();
                LinearSegments = points.Zip(points.Skip(1), (left, right) => left.Linear && right.Linear).ToArray();
            }

            public Spline(LinearSpline spline)
            {
                LinearSplineFunction = spline;
            }

            public Spline(double value)
            {
                ConstantFunction = value;
            }

            public Energy Evaluate(float time)
            {
                if (TryGetLinearSegment(time, out int index)) return new(EvaluateLinearSegment(index, time));
                if (CubicSplineFunction != null) return new(CubicSplineFunction.Interpolate(time));
                if (LinearSplineFunction != null) return new(LinearSplineFunction.Interpolate(time));
                if (ConstantFunction.HasValue) return new(ConstantFunction.Value);

                return new(0.0);
            }

            public double Slope(double time)
            {
                if (TryGetLinearSegment(time, out int index)) return LinearSegmentSlope(index);
                if (CubicSplineFunction != null) return CubicSplineFunction.Differentiate(time);
                if (LinearSplineFunction != null) return LinearSplineFunction.Differentiate(time);
                if (ConstantFunction.HasValue) return 0;

                else return 0;
            }

            bool TryGetLinearSegment(double time, out int index)
            {
                index = -1;
                if (LinearSegments == null) return false;

                for (int i = 0; i < LinearSegments.Length; i++)
                {
                    if (!LinearSegments[i]) continue;
                    if (time < SegmentTimes[i] || time > SegmentTimes[i + 1]) continue;

                    index = i;
                    return true;
                }

                return false;
            }

            double EvaluateLinearSegment(int index, double time)
            {
                return SegmentPowers[index] + LinearSegmentSlope(index) * (time - SegmentTimes[index]);
            }

            double LinearSegmentSlope(int index)
            {
                var dx = SegmentTimes[index + 1] - SegmentTimes[index];

                return Math.Abs(dx) > double.Epsilon ? (SegmentPowers[index + 1] - SegmentPowers[index]) / dx : 0;
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
            public bool SlopeLocked;
            public bool Linear;
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
            public void LockSlope() => SlopeLocked = true;
            public void UnlockSlope() => SlopeLocked = false;

            public SplinePoint Copy()
            {
                return new SplinePoint(Time, Power, ID, Slope)
                {
                    Locked = Locked,
                    SlopeLocked = SlopeLocked,
                    Linear = Linear,
                    UserDefined = UserDefined
                };
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

            interpolator.CopyBaselineFrom(this);

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
