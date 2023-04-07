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
        public List<DataPoint> BaseLineCorrectedDataPoints;
        public List<InjectionData> Injections { get; set; } = new List<InjectionData>();

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
        public InjectionData.IntegrationLengthMode IntegrationLengthMode { get; set; } = InjectionData.IntegrationLengthMode.Time;
        public float IntegrationLengthFactor { get; set; } = 8;

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
        public Dictionary<ModelOptionKey, ModelOptions> ExperimentOptions { get; } = new Dictionary<ModelOptionKey, ModelOptions>();
        public Model Model { get; set; }
        public SolutionInterface Solution => Model?.Solution;

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

        public void FitIntegrationPeaks(float threshold = .01f)
        {
            foreach (var inj in Injections)
            {
                var dps = BaseLineCorrectedDataPoints.Where(dp => dp.Time > inj.Time && dp.Time < inj.Time + inj.Delay);
                var max = dps.First(dp => Math.Abs(dp.Power) > (0.999 * dps.Max(dp => Math.Abs(dp.Power))));
                dps = dps.Where(dp => dp.Time > max.Time);
                var x = new double[dps.Count()];

                for (int i = 0; i < x.Length; i++) x[i] = i;

                var y = dps.Select(dp => (double)(Math.Abs(max.Power) - Math.Abs(dp.Power))).ToArray();
                var fit = MathNet.Numerics.Fit.Curve(x, y, (v, k, x) => x * v / (k + x), Math.Abs(max.Power), 10);
                var peaklen = (max.Time - inj.Time) + threshold * fit.P1;

                inj.SetCustomIntegrationTimes(0, (float)peaklen, true);
            }
        }

        public void SetCustomIntegrationTimes(float[] delays, float[] lengths)
        {
            for (int i = 0; i < Injections.Count; i++)
            {
                var inj = Injections[i];

                int j = i;
                if (i >= delays.Length) j = delays.Length - 1;

                inj.SetCustomIntegrationTimes(delays[j], lengths[j]);
            }
        }

        public void SetCustomIntegrationTimes(float? delay, float? variable)
        {
            if (IntegrationLengthMode != InjectionData.IntegrationLengthMode.Time && variable != null) IntegrationLengthFactor = (float)variable;

            foreach (var inj in Injections) inj.SetCustomIntegrationTimes(delay, variable);
        }

        public void CalculatePeakHeatDirection()
        {
            var tot_diff = 0.0;

            foreach (var inj in Injections)
            {
                var dat = DataPoints.Where(dp => dp.Time > inj.Time && dp.Time < inj.Time + inj.Delay);

                var mean = dat.Average(dp => dp.Power);
                var min = dat.Min(dp => dp.Power);
                var max = dat.Max(dp => dp.Power);

                if ((mean - min) > (max - mean)) inj.HeatDirection = PeakHeatDirection.Exothermal;
                else inj.HeatDirection = PeakHeatDirection.Endothermal;

                tot_diff += (mean - min) - (max - mean);
            }

            if (tot_diff < 0) AverageHeatDirection = PeakHeatDirection.Endothermal;
            else AverageHeatDirection = PeakHeatDirection.Exothermal;
        }

        public void SetProcessor(DataProcessor processor)
        {
            Processor = processor;
        }

        public double GetNoise(float start = -1, float end = -1)
        {
            if (DataPoints.Count == 0) return 0;
            if (start == -1) start = DataPoints.First().Time;
            if (end == -1) end = DataPoints.Last().Time;

            var dps = (Processor.BaselineCompleted ? BaseLineCorrectedDataPoints : DataPoints).Where(dp => dp.Time >= start && dp.Time <= end);

            double sum_of_squares = 0;

            foreach (var dp in dps)
            {
                var p = dp.Power;

                sum_of_squares += p * p;
            }

            return Math.Sqrt(sum_of_squares / (dps.Count() - 1)); //TODO check formula is correct
        }

        List<InjectionData> GetBootstrappedResiduals()
        {
            var syntheticdata = new List<InjectionData>();

            var residuals = new List<double>();

            foreach (var inj in Injections.Where(inj => inj.Include))
            {
                var fit = Model.EvaluateEnthalpy(inj.ID, withoffset: true);
                residuals.Add(inj.Enthalpy - fit);
            }

            residuals.Shuffle();
            int resindex = 0;

            foreach (var inj in Injections)
            {
                var res = 0.0;

                if (inj.Include) { res = residuals[resindex]; resindex++; }

                var fit = Model.EvaluateEnthalpy(inj.ID, withoffset: true);
                var resarea = res * inj.InjectionMass;
                var fitarea = fit * inj.InjectionMass;

                var syn_inj = new InjectionData(inj.ID, inj.Volume, inj.InjectionMass, inj.Include)
                {
                    Temperature = inj.Temperature,
                    ActualCellConcentration = inj.ActualCellConcentration,
                    ActualTitrantConcentration = inj.ActualTitrantConcentration,
                    Ratio = inj.Ratio
                };
                syn_inj.SetPeakArea(new FloatWithError(fitarea + resarea, inj.SD));

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

        public ExperimentData GetSynthClone(ModelCloneOptions options)
        {
            List<InjectionData> syninj;

            switch (options.ErrorEstimationMethod)
            {
                default:
                case ErrorEstimationMethod.BootstrapResiduals: syninj = GetBootstrappedResiduals(); break;
            }

            if (options.IncludeConcentrationErrorsInBootstrap) AddConcentrationVariance(syninj, options);

            var syndat = new ExperimentData(FileName)
            {
                Injections = syninj,
                CellVolume = CellVolume,
                MeasuredTemperature = MeasuredTemperature,
            };

            syndat.SetID(UniqueID);

            return syndat;
        }

        public void UpdateProcessing()
        {
            if (Solution != null) Solution.Invalidate();

            ProcessingUpdated?.Invoke(Processor, null);
        }

        public void UpdateSolution(Model mdl = null)
        {
            if (mdl != null) Model = mdl;

            SolutionChanged?.Invoke(this, null);
        }
    }

    public class InjectionData
    {
        public ExperimentData Experiment { get; private set; }
        public int ID { get; private set; }

        float time = -1; 

        public float Time { get => time; set { time = value; SetIntegrationTimes(); } }
        public double Volume { get; private set; }
        public float Duration { get; private set; }
        public float Delay { get; private set; }
        public float Filter { get; private set; }
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
        public double Enthalpy => PeakArea / InjectionMass;
        public double SD => PeakArea.SD;

        public double OffsetEnthalpy
        {
            get
            {
                if (Experiment.Solution == null) return Enthalpy;
                //else return Enthalpy - Experiment.Solution.Offset; //A1 pattern
                else return Enthalpy - Experiment.Solution.Parameters[ParameterType.Offset].Value;
            }
        }

        public bool IsIntegrated { get; set; } = false;

        public InjectionData(int id, double volume, double mass, bool include)
        {
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
        }

        void SetIntegrationTimes()
        {
            IntegrationStartDelay = 0;
            IntegrationLength = 0.9f * Delay;
        }

        public void SetCustomIntegrationTimes(float? delay = null, float? lengthparameter = null, bool forcetime = false)
        {
            if (lengthparameter != null) switch (Experiment.IntegrationLengthMode)
                {
                    case IntegrationLengthMode.Fit when !forcetime:
                        var dps = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > this.Time && dp.Time < this.Time + this.Delay);
                        var max = dps.First(dp => Math.Abs(dp.Power) > (0.999 * dps.Max(dp => Math.Abs(dp.Power))));
                        dps = dps.Where(dp => dp.Time > max.Time);
                        double[] x = new double[dps.Count()];
                        for (int i = 0; i < x.Length; i++) x[i] = i;
                        double[] y;
                        double peaklen = 0;
                        switch (AppSettings.PeakFitAlgorithm)
                        {
                            case PeakFitAlgorithm.Exponential:
                                y = dps.Select(dp => (double)(dp.Power)).ToArray();
                                var exp = MathNet.Numerics.Fit.Curve(x, y, (v, k, x) => v * Math.Exp(-k*x), max.Power, 0.1);
                                peaklen = (max.Time - this.Time) + 10 * Math.Log(2) / (exp.P1); //TODO should probably be 5 * -ln(2)/k = 98% returned to baseline
                                break;
                            default:
                            case PeakFitAlgorithm.Default:
                                y = dps.Select(dp => (double)(Math.Abs(max.Power) - Math.Abs(dp.Power))).ToArray();
                                var fit = MathNet.Numerics.Fit.Curve(x, y, (v, k, x) => x * v / (k + x), Math.Abs(max.Power), 10);
                                peaklen = (max.Time - this.Time) + (float)lengthparameter * fit.P1; //TODO should probably be 5 * -ln(2)/k = 98% returned to baseline
                                break;
                        }
                        
                        IntegrationLength = Math.Clamp((float)peaklen, Duration, Delay - 1);
                        break;
                    case IntegrationLengthMode.Factor when !forcetime:
                        var _dps = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > Time && dp.Time < Time + Delay);
                        var height = HeatDirection == PeakHeatDirection.Endothermal ? _dps.Max(dp => dp.Power) : _dps.Min(dp => dp.Power);
                        var thresh = Math.Abs(height / 3);
                        var first = _dps.First(dp => Math.Abs(dp.Power) > thresh);
                        var last = _dps.Last(dp => Math.Abs(dp.Power) > thresh);
                        var d = last.Time - first.Time;
                        IntegrationLength = Math.Clamp((float)lengthparameter * d, Duration, Delay - 1);
                        break;
                    case IntegrationLengthMode.Time:
                    default: IntegrationLength = Math.Clamp((float)lengthparameter, Duration, Delay - 1); break;
                }

            if (delay != null) IntegrationStartDelay = Math.Clamp((float)delay, -5, IntegrationLength);
        }

        public void ToggleDataPointActive()
        {
            Include = !Include;
        }

        public void Integrate()
        {
            var data = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime);

            double area = 0;

            foreach (var dp in data)
            {
                area += dp.Power;
            }

            var sd = EstimateError();

            sd = (sd - Math.Abs(area)) / 2;

            var peakarea = new FloatWithError(area, sd);

            SetPeakArea(peakarea);
        }

        public void SetPeakArea(FloatWithError area)
        {
            PeakArea = area;

            IsIntegrated = true;
        }

        public double EstimateError()
        {
            float start = Time - IntegrationLength;
            float end = Time + Math.Min(2 * IntegrationLength, Delay);

            var baselinedata = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time >= start && dp.Time <= end);

            return ResidualSum(baselinedata);

            static double ResidualSum(IEnumerable<DataPoint> baselinedata)
            {
                double sum_of_squares = 0;

                foreach (var dp in baselinedata)
                {
                    var p = dp.Power;

                    sum_of_squares += Math.Abs(p);
                }

                return sum_of_squares;
            }
        }

        public enum IntegrationLengthMode
        {
            Time,
            Factor,
            Fit
        }
    }

    public struct DataPoint
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
            return new DataPoint(Time, Power - baseline, Temperature);
        }
    }

    public enum PeakHeatDirection
    {
        Unknown,
        Exothermal,
        Endothermal
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
