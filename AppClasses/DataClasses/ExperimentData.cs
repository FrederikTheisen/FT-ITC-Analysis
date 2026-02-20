using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.Analysis2.Models;
using DataReaders;
using Utilities;

namespace AnalysisITC
{
    public class ExperimentData : ITCDataContainer
    {
        public static EnergyUnit Unit => DataManager.Unit;
        public static Random Rand = new Random();

        public event EventHandler ProcessingUpdated;
        public event EventHandler SolutionChanged;

        public ITCInstrument Instrument { get; set; } = ITCInstrument.Unknown;
        public ITCDataFormat DataSourceFormat { get; set; }

        public List<DataPoint> DataPoints { get; set; } = new List<DataPoint>();
        public List<DataPoint> BaseLineCorrectedDataPoints { get; set; }
        public List<InjectionData> Injections { get; set; } = new List<InjectionData>();
        public List<TandemExperimentSegment> Segments { get; private set; }
        Dictionary<int, TandemExperimentSegment> _segmentStartLookup;

        public FloatWithError SyringeConcentration { get; set; }
        public FloatWithError CellConcentration { get; set; }
        public double CellVolume { get; set; }
        public double StirringSpeed { get; set; }
        public FeedbackMode FeedBackMode { get; set; }

        public double TargetTemperature { get; set; }
        public double InitialDelay { get; set; }
        public double TargetPowerDiff { get; set; }
        public double MeasuredTemperature { get; internal set; }
        public int InjectionCount => Injections.Count;
        public PeakHeatDirection AverageHeatDirection { get; set; } = PeakHeatDirection.Unknown;
        //public InjectionData.IntegrationLengthMode IntegrationLengthMode { get; set; } = InjectionData.IntegrationLengthMode.Time;
        //public float IntegrationLengthFactor { get; set; } = 2;

        bool include = true;
        public bool Include
        {
            get
            {
                if (Processor == null) return false;
                else if (!Processor.BaselineCompleted) return false;
                else if (!Processor.IntegrationCompleted) return false;
                else return include;
            }
            set => include = value;
        }

        public double MeasuredTemperatureKelvin => 273.15 + MeasuredTemperature;

        public DataProcessor Processor { get; private set; }
        public List<ExperimentAttribute> Attributes { get; } = new List<ExperimentAttribute>();
        public Model Model { get; set; }
        public SolutionInterface Solution => Model?.Solution;
        public ExperimentData ReferenceExperiment
        {
            get
            {
                if (Attributes.Exists(att => att.Key == AttributeKey.BufferSubtraction))
                {
                    var reference = Attributes.Find(att => att.Key == AttributeKey.BufferSubtraction);

                    if (DataManager.Data.Exists(d => d.UniqueID == reference.StringValue))
                    {
                        return DataManager.Data.Find(d => d.UniqueID == reference.StringValue);
                    }
                }

                return null;
            }
        }
        public bool IsTandemExperiment => Segments != null ? Segments.Count > 0 : false;

        public ExperimentData(string file)
        {
            FileName = file;

            Processor = new DataProcessor(this);
        }

        public void IterateFileName()
        {
            var parts = FileName.Split('.');

            int v = 2;

            if (int.TryParse(parts.First().Last().ToString(), out v))
            {
                v++;
            }

            FileName = parts.First() + "_" + v.ToString() + string.Join("", parts.Skip(1));
        }

        public void AddInjection(string dataline)
        {
            var inj = new InjectionData(this, dataline, Injections.Count);

            Injections.Add(inj);
        }

        public void FitIntegrationPeaks()
        {
            try
            {
                foreach (var inj in Injections)
                {
                    inj.SetIntegrationLengthByPeakFitting();
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public void SetIntegrationLengthByFactor(float factor)
        {
            try
            {
                foreach (var inj in Injections)
                {
                    inj.SetIntegrationLengthByFactor(factor);
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public void SetIntegrationLengthByTimes(float[] lengths)
        {
            int i = 0;
            foreach (var inj in Injections)
            {
                float length;
                if (i == lengths.Length) length = lengths[^1];
                else length = lengths[i];

                inj.SetIntegrationLengthByTime(length);

                i++;
            }
        }

        public void SetIntegrationLengthByTime(float length)
        {
            try
            {
                foreach (var inj in Injections) { inj.SetIntegrationLengthByTime(length); }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public void SetIntegrationStartTimes(float[] delays)
        {
            int i = 0;
            foreach (var inj in Injections)
            {
                float delay;
                if (i == delays.Length) delay = delays[delays.Length - 1];
                else delay = delays[i];

                inj.SetIntegrationStartTime(delay);

                i++;
            }
        }

        public void SetIntegrationStartTime(float delay)
        {
            foreach (var inj in Injections) { inj.SetIntegrationStartTime(delay); }
        }

        public void CalculatePeakHeatDirection()
        {
            if (BaseLineCorrectedDataPoints.Count < 5) return;

            bool positive = false;
            bool negative = false;

            foreach (var inj in Injections)
            {
                if (inj.HeatDirection == PeakHeatDirection.Endothermal) positive = true;
                else if (inj.HeatDirection == PeakHeatDirection.Exothermal) negative = true;
            }

            if (positive && negative) AverageHeatDirection = PeakHeatDirection.Both;
            else if (positive) AverageHeatDirection = PeakHeatDirection.Endothermal;
            else if (negative) AverageHeatDirection = PeakHeatDirection.Exothermal;
            else AverageHeatDirection = PeakHeatDirection.Unknown;
        }

        public void SetReferenceExperiment(ExperimentData reference)
        {
            // Clear previous setting
            Attributes.RemoveAll(att => att.Key == AttributeKey.BufferSubtraction);

            // Add reference experiment
            Attributes.Add(ExperimentAttribute.ExperimentReference("Reference", reference.UniqueID));

            // Reintegrate peaks
            Processor?.IntegratePeaks(invalidate: true);
        }

        public void SetProcessor(DataProcessor processor)
        {
            Processor = processor;
        }

        List<InjectionData> GetBootstrappedResiduals()
        {
            if (Solution == null) return Injections;

            var syntheticdata = new List<InjectionData>();

            var residuals = new List<(double,double)>();

            foreach (var inj in Injections.Where(inj => inj.Include))
            {
                var fit = Model.EvaluateEnthalpy(inj.ID, withoffset: true);
                residuals.Add((inj.Enthalpy - fit, inj.SD));
            }

            residuals.Shuffle();
            int resindex = 0;

            foreach (var inj in Injections)
            {
                var res = (0.0,0.0);

                if (inj.Include) { res = residuals[resindex]; resindex++; }

                var fit = Model.EvaluateEnthalpy(inj.ID, withoffset: true);
                var resarea = res.Item1 * inj.InjectionMass;
                var ressdarea = res.Item2 * inj.InjectionMass;
                var fitarea = fit * inj.InjectionMass;

                var syn_inj = new InjectionData(null, inj.ID, inj.Volume, inj.InjectionMass, inj.Include)
                {
                    Temperature = inj.Temperature,
                    ActualCellConcentration = inj.ActualCellConcentration,
                    ActualTitrantConcentration = inj.ActualTitrantConcentration,
                    Ratio = inj.Ratio
                };
                syn_inj.SetPeakArea(new FloatWithError(fitarea + resarea, ressdarea));

                syntheticdata.Add(syn_inj);
            }

            return syntheticdata;
        }

        void AddConcentrationVariance(List<InjectionData> injections, ModelCloneOptions options = null)
        {
            var sd_cell = CellConcentration.FractionSD;
            var sd_syringe = SyringeConcentration.FractionSD;

            if (options.EnableAutoConcentrationVariance)
            {
                if (sd_cell < 0.001) sd_cell = options.AutoConcentrationVariance;
                if (sd_syringe < 0.001) sd_syringe = options.AutoConcentrationVariance;
            }

            var cell = 1 + (2 * Rand.NextDouble() - 1) * sd_cell;
            var syringe = 1 + (2 * Rand.NextDouble() - 1) * sd_syringe;

            foreach (var inj in injections)
            {
                inj.ActualCellConcentration *= cell;
                inj.ActualTitrantConcentration *= syringe;
            }
        }

        public virtual ExperimentData GetSynthClone(ModelCloneOptions options)
        {
            var clone = new ExperimentData(FileName)
            {
                CellVolume = CellVolume,
                MeasuredTemperature = MeasuredTemperature,
                CellConcentration = CellConcentration,
                SyringeConcentration = SyringeConcentration,
            };
            List<InjectionData> syninj;

            switch (options.ErrorEstimationMethod)
            {
                default:
                case ErrorEstimationMethod.BootstrapResiduals: syninj = GetBootstrappedResiduals(); break;
                case ErrorEstimationMethod.LeaveOneOut when options.IsGlobalClone: syninj = Injections; break;
                case ErrorEstimationMethod.LeaveOneOut:
                    {
                        syninj = new List<InjectionData>();
                        foreach (var inj in Injections)
                        {
                            var sinj = new InjectionData(clone, inj.ID, inj.Volume, inj.InjectionMass, inj.Include)
                            {
                                Temperature = inj.Temperature,
                                ActualCellConcentration = inj.ActualCellConcentration,
                                ActualTitrantConcentration = inj.ActualTitrantConcentration,
                                Ratio = inj.Ratio,
                            };
                            sinj.SetPeakArea(new FloatWithError(inj.PeakArea, inj.PeakArea.SD));
                            if (sinj.ID == options.DiscardedDataPoint) sinj.Include = false;
                            syninj.Add(sinj);
                        }
                        break;
                    }
            }

            if (options.IncludeConcentrationErrorsInBootstrap) AddConcentrationVariance(syninj, options);

            clone.Injections = syninj;

            clone.Segments = Segments?
                .Select(s => new TandemExperimentSegment(s.FirstInjectionID, s.SegmentInitialActiveCellConc, s.SegmentInitialActiveTitrantConc))
                .ToList();

            clone.InvalidateSegmentLookup();

            foreach (var opt in Attributes) clone.Attributes.Add(opt);

            clone.SetID(UniqueID);

            return clone;
        }

        public void UpdateProcessing(bool invalidate = true)
        {
            if (Solution != null && invalidate) Solution.Invalidate();

            ProcessingUpdated?.Invoke(Processor, null);
        }

        public void UpdateSolution(Model mdl = null)
        {
            if (mdl != null) Model = mdl;

            SolutionChanged?.Invoke(this, null);
        }

        public void AddSegment(TandemExperimentSegment segment)
        {
            if (Segments == null) Segments = new List<TandemExperimentSegment>();

            Console.WriteLine("Adding Segment:\n  ID: "
                + segment.FirstInjectionID.ToString() + "\n  Cell Conc:    "
                + segment.SegmentInitialActiveCellConc.ToString() + "\n  Titrant Conc: "
                + segment.SegmentInitialActiveTitrantConc.ToString());

            Segments.Add(segment);
        }

        void EnsureSegmentStartLookup()
        {
            if (_segmentStartLookup != null) return;
            _segmentStartLookup = Segments?
                .GroupBy(s => s.FirstInjectionID)
                .ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<int, TandemExperimentSegment>();
        }

        // Call this after loading / after you assign SegmentStarts
        public void InvalidateSegmentLookup()
        {
            _segmentStartLookup = null;
        }
    }

    public class InjectionData
    {
        public ExperimentData Experiment { get; private set; }
        public int ID { get; private set; }

        float time = -1;

        public float Time { get => time; set { time = value; InitializeIntegrationTimes(); } }
        public double Volume { get; private set; }
        public float Duration { get; private set; }
        public float Delay { get; private set; }
        public float Filter { get; private set; } = 5;
        public double Temperature { get; set; }
        public double InjectionMass { get; set; }

        public double ActualCellConcentration { get; set; }
        public double ActualTitrantConcentration { get; set; }
        public double Ratio { get; set; }

        public bool Include { get; internal set; } = true;
        public float IntegrationStartDelay { get; private set; } = 0;
        public float IntegrationLength { get; private set; } = 90;
        public float IntegrationStartTime => Time + IntegrationStartDelay;
        public float IntegrationEndTime => Time + IntegrationLength;

        public PeakHeatDirection HeatDirection { get; set; } = PeakHeatDirection.Unknown;

        public FloatWithError PeakArea { get; private set; } = new();
        public Energy Enthalpy2 => new(PeakArea / InjectionMass);
        public double Enthalpy => PeakArea / InjectionMass;
        public double SD => PeakArea.SD / InjectionMass;

        public double OffsetEnthalpy
        {
            get
            {
                if (Experiment.Solution == null) return Enthalpy;
                //else return Enthalpy - Experiment.Solution.Offset; //A1 pattern
                else return Enthalpy - Experiment.Solution.Parameters[ParameterType.Offset].Value;
            }
        }

        public double ResidualEnthalpy
        {
            get
            {
                if (Experiment.Solution == null) return 0;
                if (InjectionMass == 0) return 0;

                return Experiment.Model.Residual(this) / InjectionMass;
            }
        }

        public bool IsIntegrated { get; set; } = false;

        private InjectionData() { }

        public static InjectionData FromTAFileLine(ExperimentData experiment, int id, double v, DataPoint dp, InjectionData prev)
        {
            float delay = 0.0f;
            if (prev != null)
            {
                delay = dp.Time - prev.Time;
                prev.Delay = delay;
            }


            return new InjectionData()
            {
                Experiment = experiment,
                ID = id,
                Include = id != 0,
                Volume = v,
                Time = dp.Time,
                Temperature = dp.Temperature,
                Delay = delay, // Delay guess until next injection is processed
                Duration = 2 * (float)v, // Not known
                Filter = 0.0f, // Not known, not used
            };
        }

        public InjectionData(ExperimentData experiment, int id, double volume, double mass, bool include)
        {
            Experiment = experiment;
            ID = id;
            Volume = volume;
            InjectionMass = mass;
            Include = include;
        }

        public InjectionData(ExperimentData experiment, string line, int id)
        {
            Experiment = experiment;

            ID = id;

            var data = line.Substring(1).Split(',');

            Volume = float.Parse(data[0]) / 1000000f;
            Duration = float.Parse(data[1]);
            Delay = float.Parse(data[2]);
            Filter = float.Parse(data[3]);
        }

        /// <summary>
        /// FT-ITC file format reader for injection data
        /// </summary>
        /// <param name="experiment"></param>
        /// <param name="line"></param>
        public InjectionData(ExperimentData experiment, string line)
        {
            Experiment = experiment;

            var parameters = line.Split(',');

            ID = int.Parse(parameters[0]);
            Include = parameters[1] == "1";
            Time = float.Parse(parameters[2]);
            Volume = double.Parse(parameters[3]);
            Delay = float.Parse(parameters[4]);
            Duration = float.Parse(parameters[5]);
            Temperature = double.Parse(parameters[6]);
            IntegrationStartDelay = float.Parse(parameters[7]);
            IntegrationLength = float.Parse(parameters[8]);

            // Newer files contain additional information for the injections to handle tandem experiment data
            if (parameters.Count() > 9)
            {
                ActualCellConcentration = double.Parse(parameters[9]);
                ActualTitrantConcentration = double.Parse(parameters[10]);
                Ratio = ActualTitrantConcentration / ActualCellConcentration;
            }
        }

        private InjectionData(ExperimentData data, int id, float time, double volume, float delay, float duration, double temp)
        {
            Experiment = data;
            ID = id;
            Time = time;
            Volume = volume;
            Duration = duration;
            Temperature = temp;
            Delay = delay;

            InitializeIntegrationTimes();
        }

        public void InitializeIntegrationTimes()
        {
            IntegrationStartDelay = 0;
            IntegrationLength = 0.9f * Delay;
        }

        public void SetIntegrationStartTime(float delay)
        {
            IntegrationStartDelay = Math.Clamp(delay, -Delay, IntegrationLength - 1);
        }

        public void SetIntegrationLengthByPeakFitting()
        {
            SetIntegrationLengthByFactor(2.5f);
        }

        public void SetIntegrationLengthByFactor(float factor)
        {
            // Assumed instrument/response time constant (seconds).
            // If you meant "k = 1/8 s^-1" (rate constant), then tau = 1/k = 8 s (same value here).
            const double tau = 8.0;

            // "Back to baseline" definition
            const double returnFrac = 0.02;   // 2% of apex amplitude above baseline
            const double noiseFactor = 3.0;   // 3*sigma floor
            const int smoothHalfWin = 2;      // 5-pt moving average

            try
            {
                var dps = Experiment.BaseLineCorrectedDataPoints
                    .Where(dp => dp.Time > Time && dp.Time < Time + Delay)
                    .ToList();
                if (dps.Count < 10) return;

                int n = dps.Count;

                // Smooth |power| to suppress isolated spikes (cheap, short)
                double[] sm = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double s = 0; int c = 0;
                    for (int j = Math.Max(0, i - smoothHalfWin); j <= Math.Min(n - 1, i + smoothHalfWin); j++)
                    { s += Math.Abs(dps[j].Power); c++; }
                    sm[i] = s / c;
                }

                // Baseline + sigma from tail (last ~20 s or last 10%)
                int tailN = Math.Min(20, Math.Max(10, n / 10));
                var tail = sm.Skip(n - tailN).OrderBy(v => v).ToArray();
                double baseline = tail[tail.Length / 2];

                double sigma = Math.Sqrt(sm.Skip(n - tailN).Average(v => (v - baseline) * (v - baseline)));

                // Apex = last point before |power| turns back (use smoothed series)
                // Pick the first "real" turning point: sm rises then begins to fall.
                int apex = -1;
                for (int i = 2; i < n - 2; i++)
                {
                    if (sm[i - 2] <= sm[i - 1] && sm[i - 1] <= sm[i] && sm[i] >= sm[i + 1] && sm[i + 1] >= sm[i + 2])
                    { apex = i; break; }
                }
                if (apex < 0) return;

                double A0 = sm[apex] - baseline;
                if (A0 <= 0) return;

                double thr = Math.Max(returnFrac * A0, noiseFactor * sigma);

                // Exponential return time (seconds). Guard against thr >= A0.
                double tReturn = (thr < A0) ? (tau * Math.Log(A0 / thr)) : 0.0;

                // IntegrationLength assumed to be "end offset from injection start Time"
                double apexOffset = dps[apex].Time - Time;
                double endOffset = apexOffset + factor * tReturn;

                // Keep between the start delay and the injection scope
                IntegrationLength = Math.Clamp((float)endOffset, IntegrationStartDelay + 2, Delay);
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public void SetIntegrationLengthByTime(float time)
        {
            // Keep between the start delay and the injection scope
            IntegrationLength = IntegrationLength = Math.Clamp(time, IntegrationStartDelay + 2, Delay); ;
        }

        public void ToggleDataPointActive()
        {
            Include = !Include;
        }

        public void Integrate()
        {
            var data = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime);
            var reference = Experiment.ReferenceExperiment;

            if (data.Count() <= 0) throw new Exception($"Cannot integrate peak with no data point(s)");

            double area = 0;
            float t = data.First().Time;

            foreach (var dp in data)
            {
                var dt = dp.Time - t;
                area += dp.Power * dt;

                t = dp.Time;
            }

            var sd = EstimateError2();
            var peakarea = new FloatWithError(area, sd);

            HeatDirection = area > 0 ? PeakHeatDirection.Endothermal : PeakHeatDirection.Exothermal;

            SetPeakArea(peakarea);

            // Update peak area if reference experiment is set
            if (reference != null)
            {
                // Clamp reference to corresponding or last (we just have to assume all dilution heats are the same following the last)
                var idx = Math.Clamp(ID, 0, reference.InjectionCount - 1);
                var inj = reference.Injections[idx];

                var ref_heat = inj.PeakArea;
                var new_heat = peakarea.Value - ref_heat.Value;
                var new_sd = Math.Sqrt(peakarea.SD * peakarea.SD + ref_heat.SD * ref_heat.SD);

                peakarea = new FloatWithError(new_heat, new_sd);

                SetPeakArea(peakarea);
            }
        }

        public void SetPeakArea(FloatWithError area)
        {
            PeakArea = area;

            IsIntegrated = true;
        }

        private double EstimateError2()
        {
            // Set baseline fitting time points to non-integrated data points surrounding current peak.
            float baseline_start_time = ID == 0 ? 0 : Experiment.Injections[ID - 1].IntegrationEndTime;
            float baseline_end_time = Time + Delay;
            float baseline_exclude_start_time = IntegrationStartTime;

            // Start at previous inj integration end time, but take at least 10s
            baseline_start_time = Math.Min(baseline_start_time, baseline_exclude_start_time - 10);
            // Exclude integration but keep at least the last 10s of the injection scope
            float baseline_exclude_end_time = Math.Min(IntegrationEndTime, baseline_end_time - 10);

            // In case of strange values or very short injection scopes
            if (baseline_exclude_end_time <= baseline_exclude_start_time)
                baseline_exclude_end_time = baseline_exclude_start_time;

            // Collect the baseline points (excluding integrated points)
            var bl = Experiment.BaseLineCorrectedDataPoints.Where(dp =>
                dp.Time >= baseline_start_time
                && dp.Time <= baseline_end_time
                && !(dp.Time > baseline_exclude_start_time && dp.Time < baseline_exclude_end_time)).ToList();

            var med = Statistics.Median(bl.Select(dp => dp.Power).ToList());
            var mad = Statistics.Median(bl.Select(dp => Math.Abs(dp.Power - med)).ToList());
            var sigma0 = 1.4826f * mad;
            if (sigma0 <= 0) sigma0 = 1e-12f;
            var k = 6.0f;
            var cap = k * sigma0;

            var blpoints = bl.Count;

            if (blpoints < 2) return 0;

            // Calculate the RMSD
            double ss = 0;
            int n1 = 0;
            int n2 = 0;
            foreach (var dp in bl)
            {
                var p = dp.Power;

                if (Math.Abs(p) > cap) p = cap;

                ss += p * p;

                if (dp.Time <= baseline_exclude_start_time) n1++;
                else n2++;
            }

            var sigma_p = Math.Sqrt(ss / (blpoints - 1));

            var intpoints = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime).ToList();
            int n_samples_integration = intpoints.Count;

            if (n_samples_integration < 2) return 0;

            float dt = (intpoints.Last().Time - intpoints.First().Time) / (n_samples_integration - 1);
            var r1 = Statistics.EstimateAutoCorrelation(bl, 2 * dt);

            var sumVarInt = Statistics.Ar1SumVarFactor(n_samples_integration, r1);
            var sigma_q = sigma_p * dt * Math.Sqrt(sumVarInt);

            var sumVar = Statistics.Ar1SumVarFactor(n1, r1) + Statistics.Ar1SumVarFactor(n2, r1);
            var sigma_q_bl = (sigma_p * Math.Sqrt(sumVar) / blpoints) * IntegrationLength;

            return Math.Sqrt(sigma_q * sigma_q + sigma_q_bl * sigma_q_bl);
        }

        public InjectionData Copy(ExperimentData data)
        {
            var inj = new InjectionData(data, ID, Time, Volume, Delay, Duration, Temperature);
            inj.InjectionMass = Volume * data.SyringeConcentration;
            inj.ActualCellConcentration = ActualCellConcentration;
            inj.ActualTitrantConcentration = ActualTitrantConcentration;
            inj.Ratio = Ratio;
            inj.Include = Include;

            return inj;
        }

        public enum IntegrationLengthMode
        {
            Time,
            Factor,
            Fit
        }
    }

    public readonly struct DataPoint
    {
        /// <summary>
        /// Power in Joules
        /// </summary>
        public readonly float Power { get; }

        public float Time { get; }
        /// <summary>
        /// Cell temperature
        /// </summary>
        public float Temperature { get; }
        /// <summary>
        /// Temperature difference between sample and reference cell
        /// </summary>
        public float DT { get; } //Delta T between sample and reference cells
        /// <summary>
        /// Thermal shield temperature
        /// </summary>
        public float ShieldT { get; } //Probably jacket temperature
        public float ATP { get; } //Unknown variable
        /// <summary>
        /// Jacket FeedBack current
        /// </summary>
        public float JFBI { get; }

        public DataPoint(float time, float power, float temp)
        {
            this.Time = time;
            this.Power = power;
            this.Temperature = temp;
            this.ATP = 0;
            this.JFBI = 0;
            this.DT = 0;
            this.ShieldT = 0;
        }

        public DataPoint(float time, float power, float temp = 0, float dt = 0, float shieldt = 0, float atp = 0, float jfbi = 0)
        {
            this.Time = time;
            this.Power = power;
            this.Temperature = temp;
            this.ATP = atp;
            this.JFBI = jfbi;
            this.DT = dt;
            this.ShieldT = shieldt;
        }

        public static double Mean(List<DataPoint> list)
        {
            double sum = 0;

            foreach (var dp in list) sum += dp.Power;

            return sum / list.Count;
        }

        public static double Median(List<DataPoint> list)
        {
            int count = list.Count();

            if (count % 2 == 0)
                return list.Select(x => x.Power).OrderBy(o => o).Skip((count / 2) - 1).Take(2).Average();
            else
                return list.Select(x => x.Power).OrderBy(x => x).ElementAt(count / 2);
        }

        public static double VolatilityWeightedAverage(List<DataPoint> list)
        {
            double w = 0;
            double sum = 0;

            for (int i = 1; i < list.Count; i++)
            {
                double _w = .01 / (0.005 + Math.Abs(list[i].Power - list[i - 1].Power));

                w += _w;
                sum += _w * list[i].Power;
            }

            return sum / w;
        }

        public static double Slope(List<DataPoint> list)
        {
            if (list.Count == 0) return 0;

            var deltaX = (list.Last().Time - list.First().Time) / 2;

            var first = DataPoint.Mean(list.Take(list.Count / 2).ToList());
            var last = DataPoint.Mean(list.Skip(list.Count / 2).ToList());

            return (last - first) / deltaX;
        }

        public DataPoint SubtractBaseline(float baseline)
        {
            return new DataPoint(Time, Power - baseline, Temperature, DT, ShieldT, ATP, JFBI);
        }

        public DataPoint Copy()
        {
            return new DataPoint(Time, Power, Temperature, DT, ShieldT, ATP, JFBI);
        }

        override public string ToString()
        {
            return Time.ToString("F1") + ", " + Power.ToString("G3");
        }
    }

    public class TandemExperimentSegment
    {
        public int FirstInjectionID { get; private set; }          // ID of first injection in this segment

        /// <summary>
        /// Segment starting concentrations in cell
        /// </summary>
        public double SegmentInitialActiveCellConc { get; private set; }         // M_pre in cell (mol/L)
        public double SegmentInitialActiveTitrantConc { get; private set; }      // L_pre in cell (mol/L)

        public TandemExperimentSegment() { }

        public TandemExperimentSegment(int ID, double activecellconc, double activetitrantconc)
        {
            FirstInjectionID = ID;
            SegmentInitialActiveCellConc = activecellconc;
            SegmentInitialActiveTitrantConc = activetitrantconc;
        }

        public static TandemExperimentSegment FromFile(string line)
        {
            var items = line.Split(',');

            return new TandemExperimentSegment()
            {
                FirstInjectionID = int.Parse(items[0]),
                SegmentInitialActiveCellConc = double.Parse(items[1]),
                SegmentInitialActiveTitrantConc = double.Parse(items[2])
            };
        }

        public void UpdateConcentrations(double activecellconc, double activetitrantconc)
        {
            Console.WriteLine("Updating TandemSegment Concentrations: \n" + activecellconc.ToString() + "\n" + activetitrantconc.ToString());

            SegmentInitialActiveCellConc = activecellconc;
            SegmentInitialActiveTitrantConc = activetitrantconc;
        }
    }

    public enum PeakHeatDirection
    {
        Unknown,
        Exothermal,
        Endothermal,
        Both,
    }

    public enum FeedbackMode
    {
        [FeedbackMode("None")]
        None = 0,
        [FeedbackMode("Low")]
        Low = 1,
        [FeedbackMode("High")]
        High = 2
    }
}
