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

        private double measuredTemperature = double.NaN;

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
        public double StirringSpeed { get; set; } = -1;
        public FeedbackMode FeedBackMode { get; set; }

        public double TargetTemperature { get; set; }
        public double InitialDelay { get; set; }
        public double TargetPowerDiff { get; set; }
        public double MeasuredTemperature
        {
            get
            {
                if (double.IsNaN(measuredTemperature)) return TargetTemperature;
                else return measuredTemperature;
            }
            internal set => measuredTemperature = value;
        }
        public int InjectionCount => Injections.Count;
        public PeakHeatDirection AverageHeatDirection { get; set; } = PeakHeatDirection.Unknown;
        public bool CanBeAnalyzed => Injections.All(inj => inj.IsIntegrated);
        //public InjectionData.IntegrationLengthMode IntegrationLengthMode { get; set; } = InjectionData.IntegrationLengthMode.Time;
        //public float IntegrationLengthFactor { get; set; } = 2;

        bool include = true;
        public bool Include
        {
            get
            {
                if (!Injections.All(inj => inj.IsIntegrated)) return false;
                else return include;
            }
            set => include = value;
        }

        public double MeasuredTemperatureKelvin => 273.15 + MeasuredTemperature;
        public double TimeStep
        {
            get
            {
                if (DataPoints.Count < 2) return 1;

                return (DataPoints.Last().Time - DataPoints.First().Time) / (DataPoints.Count - 1);
            }
        }

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
        public TimeSpan Duration
        {
            get
            {
                if (DataPoints != null && DataPoints.Count > 1)
                {
                    return TimeSpan.FromSeconds(DataPoints.Last().Time - DataPoints.First().Time);
                }

                return TimeSpan.FromSeconds(0);
            }
        }
        public bool HasThermogram => DataPoints != null && DataPoints.Count > 1;
        public AnalysisXAxisType AxisType
        {
            get
            {
                // There is no possible way to get any meaningful X axis from concentrations
                if (CellConcentration < double.Epsilon && SyringeConcentration < double.Epsilon) return AnalysisXAxisType.ID;

                // We can just return the running concentration of titrant in the cell
                if (CellConcentration < double.Epsilon) return AnalysisXAxisType.TitrantConcentration;

                // Using dissociation model, use dissociation axis
                if (Model != null && Model.ModelType == AnalysisModel.Dissociation) return AnalysisXAxisType.TitrantConcentration;

                return AnalysisXAxisType.MolarRatio;
            }
        }

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
                if (i >= lengths.Length) length = lengths[^1];
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
                if (i >= delays.Length) delay = delays[^1];
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
            bool positive = false;
            bool negative = false;

            foreach (var inj in Injections)
            {
                if (!inj.Include) continue;

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
            // Check for self referencing.
            if (reference.UniqueID == this.UniqueID) throw new HandledException(HandledException.Severity.Warning, "Buffer Subtraction Error", "Attempting to set reference experiment to itself");
            if (reference.ReferenceExperiment != null) throw new HandledException(HandledException.Severity.Warning, "Buffer Subtraction Error", "Reference experiment already contains a buffer subtraction");

            // Clear previous setting
            Attributes.RemoveAll(att => att.Key == AttributeKey.BufferSubtraction);

            // Add reference experiment
            Attributes.Add(ExperimentAttribute.ExperimentReference("Reference", reference.UniqueID));

            // Reintegrate peaks
            // Processor?.IntegratePeaks(invalidate: true);
            foreach (var inj in Injections)
                inj.UpdateCorrectedPeakArea();
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
                            // If using leave one out or the data point was excluded from the original fit
                            bool discard = inj.ID == options.DiscardedDataPoint || !inj.Include;
                            var sinj = new InjectionData(clone, inj.ID, inj.Volume, inj.InjectionMass, !discard)
                            {
                                Temperature = inj.Temperature,
                                ActualCellConcentration = inj.ActualCellConcentration,
                                ActualTitrantConcentration = inj.ActualTitrantConcentration,
                                Ratio = inj.Ratio,
                            };
                            sinj.SetPeakArea(new FloatWithError(inj.PeakArea, inj.PeakArea.SD));
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

            CalculatePeakHeatDirection();

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

        /// <summary>
        /// The X axis variable to plot
        /// </summary>
        public double Ratio { get; set; }

        public bool Include { get; private set; } = true;
        public float IntegrationStartDelay { get; private set; } = 0;
        public float IntegrationEndOffset { get; private set; } = 90;
        public float IntegrationStartTime => Time + IntegrationStartDelay;
        public float IntegrationEndTime => Time + IntegrationEndOffset;
        public float IntegrationLength => IntegrationEndOffset - IntegrationStartDelay;
        float MinimumIntegrationTime => 2 * (float)Experiment.TimeStep;

        public PeakHeatDirection HeatDirection { get; set; } = PeakHeatDirection.Unknown;

        public bool IsIntegrated { get; set; } = false;
        public FloatWithError RawPeakArea { get; private set; } = new();
        public FloatWithError PeakArea { get; private set; } = new();
        public Energy Enthalpy2 => new(PeakArea / InjectionMass);
        public double Enthalpy => PeakArea / InjectionMass;
        public double SD => PeakArea.SD / InjectionMass;

        static bool IsValidReferenceInjection(InjectionData inj) => inj != null && inj.Include && inj.IsIntegrated;

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

        public static InjectionData FromFTITCLine(ExperimentData experiment, string line)
        {
            var parameters = line.Split(',');

            var inj = new InjectionData()
            {
                Experiment = experiment,
                ID = int.Parse(parameters[0]),
                Include = parameters[1] == "1",
                Time = float.Parse(parameters[2]),
                Volume = double.Parse(parameters[3]),
                Delay = float.Parse(parameters[4]),
                Duration = float.Parse(parameters[5]),
                Temperature = double.Parse(parameters[6]),
                IntegrationStartDelay = float.Parse(parameters[7]),
                IntegrationEndOffset = float.Parse(parameters[8]),
            };

            // Newer files contain additional information for the injections to handle tandem experiment data
            if (parameters.Count() > 9)
            {
                inj.ActualCellConcentration = double.Parse(parameters[9]);
                inj.ActualTitrantConcentration = double.Parse(parameters[10]);
                inj.Ratio = inj.ActualTitrantConcentration / inj.ActualCellConcentration;
            }

            // Newer files contain additional information
            if (parameters.Count() > 11)
            {
                var peakarea = double.Parse(parameters[11]);
                var peaksd = double.Parse(parameters[12]);

                inj.SetPeakArea(new FloatWithError(peakarea, peaksd));
            }

            return inj;
        }

        public static InjectionData FromPEAQFile(ExperimentData experiment, int id, bool include, double time, double volume, double delay, double duration, double temperature)
        {
            return new InjectionData()
            {
                Experiment = experiment,
                ID = id,
                Include = include,
                Time = (float)time,
                Volume = volume,
                Delay = (float)delay,
                Duration = (float)duration,
                Temperature = temperature
            };
        }

        public InjectionData(ExperimentData experiment, double volume)
        {
            Experiment = experiment;

            ID = experiment.InjectionCount;
            Volume = volume;
            Time = ID; // No meaningful time
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
            Include = ID > 0;
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
            IntegrationEndOffset = float.Parse(parameters[8]);

            // Newer files contain additional information for the injections to handle tandem experiment data
            if (parameters.Count() > 9)
            {
                ActualCellConcentration = double.Parse(parameters[9]);
                ActualTitrantConcentration = double.Parse(parameters[10]);
                Ratio = ActualTitrantConcentration / ActualCellConcentration;
            }
        }

        public InjectionData(ExperimentData data, float volume, float delay, float filter, float duration)
        {
            Experiment = data;
            Volume = volume;
            Delay = delay;
            Filter = filter;
            Duration = duration;
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
            IntegrationEndOffset = 0.8f * Delay;
        }

        public void SetIntegrationStartTime(float delay)
        {
            // start delay cannot be more negative than this number under any circumstances
            // -Delay should be start of previous injection
            // DataPoints first because there might not be datapoinst from the first few seconds
            var absoluteminimum = Math.Max(-Delay, Experiment.DataPoints.First().Time - Time + MinimumIntegrationTime);

            IntegrationStartDelay = Math.Clamp(delay, absoluteminimum, IntegrationEndOffset - MinimumIntegrationTime);
        }

        public void SetIntegrationLengthByPeakFitting()
        {
            SetIntegrationLengthByFactor(2.5f);
        }

        public void SetIntegrationLengthByFactor(float factor)
        {
            const double tau = 8.0;

            const double returnFrac = 0.02;
            const double noiseFactor = 3.0;

            // Time-based parameters (seconds)
            const double smoothWindowSeconds = 5.0; // ~current behavior at 1 s sampling
            const double tailWindowSeconds = 20.0;  // as per comment
            const int minTailSamples = 10;          // keep some robustness

            try
            {
                var dps = Experiment.BaseLineCorrectedDataPoints
                    .Where(dp => dp.Time > Time && dp.Time < Time + Delay)
                    .ToList();
                if (dps.Count < 10) return;

                int n = dps.Count;
                double dt = Experiment.TimeStep;
                if (!(dt > 0)) return;

                // Convert time windows -> sample windows
                int smoothHalfWin = Math.Max(1, (int)Math.Round((smoothWindowSeconds / dt) / 2.0));
                int tailN = (int)Math.Round(tailWindowSeconds / dt);
                tailN = Math.Clamp(tailN, minTailSamples, n);

                // Smooth |power|
                double[] sm = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double s = 0; int c = 0;
                    int a = Math.Max(0, i - smoothHalfWin);
                    int b = Math.Min(n - 1, i + smoothHalfWin);
                    for (int j = a; j <= b; j++) { s += Math.Abs(dps[j].Power); c++; }
                    sm[i] = s / c;
                }

                // Baseline + sigma from tail (last tailWindowSeconds)
                var tail = sm.Skip(n - tailN).OrderBy(v => v).ToArray();
                double baseline = tail[tail.Length / 2];

                double sigma = Math.Sqrt(sm.Skip(n - tailN)
                    .Average(v => (v - baseline) * (v - baseline)));

                // Apex: turning point using the same time-scale neighborhood
                // (keep your logic but with window derived from dt)
                int apex = -1;
                int k = Math.Max(2, smoothHalfWin); // ensure enough points for pattern
                for (int i = k; i < n - k; i++)
                {
                    // require non-decreasing up to i and non-increasing after i over k steps
                    bool up = true, down = true;
                    for (int u = i - k; u < i; u++) if (sm[u] > sm[u + 1]) { up = false; break; }
                    if (up)
                    {
                        for (int d = i; d < i + k; d++) if (sm[d] < sm[d + 1]) { down = false; break; }
                        if (down) { apex = i; break; }
                    }
                }
                if (apex < 0) return;

                double A0 = sm[apex] - baseline;
                if (A0 <= 0) return;

                double thr = Math.Max(returnFrac * A0, noiseFactor * sigma);

                double tReturn = (thr < A0) ? (tau * Math.Log(A0 / thr)) : 0.0;

                double apexOffset = dps[apex].Time - Time;
                double endOffset = apexOffset + factor * tReturn;

                IntegrationEndOffset = Math.Clamp((float)endOffset,
                    IntegrationStartDelay + MinimumIntegrationTime,
                    Delay);
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public void SetIntegrationLengthByTime(float time)
        {
            // Keep between the start delay and the injection scope
            IntegrationEndOffset = Math.Clamp(time, IntegrationStartDelay + MinimumIntegrationTime, Delay);
        }

        public void ToggleDataPointActive()
        {
            Include = !Include;

            // Heat direction depends only on included peaks to avoid artifacts
            Experiment.CalculatePeakHeatDirection();
        }

        public void Integrate()
        {
            var data = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime).ToList();
            var area = 0.0;
            var t = IntegrationStartTime;

            foreach (var dp in data)
            {
                var dt = dp.Time - t;
                area += dp.Power * dt;

                t = dp.Time;
            }

            var sd = EstimateError2();
            var peakarea = new FloatWithError(area, sd);

            // Set the peak area and perform buffer subtraction if relevant
            SetPeakArea(peakarea);
        }

        /// <summary>
        /// Set the integrated area and set the IsIntegrated flag. Also performs buffer referencing.
        /// </summary>
        public void SetPeakArea(FloatWithError area)
        {
            RawPeakArea = area;
            UpdateCorrectedPeakArea();

            // Set heat direction based on the area and error
            HeatDirection =
                PeakArea > 3 * PeakArea.SD ? PeakHeatDirection.Endothermal :
                PeakArea < -3 * PeakArea.SD ? PeakHeatDirection.Exothermal :
                PeakHeatDirection.Unknown;

            IsIntegrated = true;
        }

        public void UpdateCorrectedPeakArea()
        {
            var area = RawPeakArea;

            var reference = Experiment?.ReferenceExperiment;
            if (reference != null && TryGetReferencePeakArea(reference, out var refHeat))
            {
                var newHeat = area.Value - refHeat.Value;
                var newSd = Math.Sqrt(area.SD * area.SD + refHeat.SD * refHeat.SD);
                area = new FloatWithError(newHeat, newSd);
            }

            PeakArea = area;
        }

        bool TryGetReferencePeakArea(ExperimentData reference, out FloatWithError area)
        {
            area = default;

            if (reference.InjectionCount == 0) return false;

            // Prefer same injection if valid
            var idx = Math.Clamp(ID, 0, reference.InjectionCount - 1);
            var same = reference.Injections[idx];
            if (IsValidReferenceInjection(same))
            {
                area = same.RawPeakArea;
                return true;
            }

            // Look for alternatives
            InjectionData prev = null;
            for (int i = idx - 1; i >= 0; i--)
            {
                var inj = reference.Injections[i];
                if (IsValidReferenceInjection(inj))
                {
                    prev = inj;
                    break;
                }
            }

            InjectionData next = null;
            for (int i = idx + 1; i < reference.InjectionCount; i++)
            {
                var inj = reference.Injections[i];
                if (IsValidReferenceInjection(inj))
                {
                    next = inj;
                    break;
                }
            }

            // Prefer average over previous over next
            if (prev != null && next != null) { area = FWEMath.Average(prev.RawPeakArea, next.RawPeakArea); return true; }
            else if (prev != null) { area = prev.RawPeakArea; return true; }
            else if (next != null) { area = next.RawPeakArea; return true; }

            return false;
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

            float dt = (float)Experiment.TimeStep;
            var r1 = Statistics.EstimateAutoCorrelation(bl, 2 * dt);

            var sumVarInt = Statistics.Ar1SumVarFactor(n_samples_integration, r1);
            var sigma_q = sigma_p * dt * Math.Sqrt(sumVarInt);

            var sumVar = Statistics.Ar1SumVarFactor(n1, r1) + Statistics.Ar1SumVarFactor(n2, r1);
            var sigma_q_bl = (sigma_p * Math.Sqrt(sumVar) / blpoints) * IntegrationLength;

            return Math.Sqrt(sigma_q * sigma_q + sigma_q_bl * sigma_q_bl);
        }

        public InjectionData Copy(ExperimentData data)
        {
            var inj = new InjectionData(data, ID, Time, Volume, Delay, Duration, Temperature)
            {
                InjectionMass = Volume * data.SyringeConcentration,
                ActualCellConcentration = ActualCellConcentration,
                ActualTitrantConcentration = ActualTitrantConcentration,
                Ratio = Ratio,
                Include = Include
            };

            inj.SetPeakArea(RawPeakArea);

            return inj;
        }

        public InjectionData CopyWithNewID(ExperimentData data, int id, float timeshift)
        {
            var inj = new InjectionData()
            {
                Experiment = data,
                ID = id,
                Include = this.Include,
                Time = this.Time + timeshift,
                Volume = this.Volume,
                Delay = this.Delay,
                Duration = this.Duration,
                Temperature = this.Temperature,
                IntegrationStartDelay = this.IntegrationStartDelay,
                IntegrationEndOffset = this.IntegrationEndOffset,
            };

            return inj;
        }

        public enum IntegrationLengthMode
        {
            Time,
            Factor,
            Fit
        }

        public override string ToString()
        {
            return $"{ID} : {Ratio:F2} : {Enthalpy2.ToFormattedString(EnergyUnit.KiloJoule)}";
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
