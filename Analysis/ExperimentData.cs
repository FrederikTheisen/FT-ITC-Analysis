using System;
using System.Collections.Generic;
using System.Linq;
using DataReaders;
using Utilities;

namespace AnalysisITC
{
    public class ExperimentData
    {
        public static EnergyUnit Unit => DataManager.Unit;

        public string FileName { get; private set; } = "";
        public DateTime Date { get; internal set; }

        public List<DataPoint> DataPoints = new List<DataPoint>();
        public List<DataPoint> BaseLineCorrectedDataPoints;
        public List<InjectionData> Injections = new List<InjectionData>();

        public float SyringeConcentration;
        public float CellConcentration;
        public float CellVolume;
        public float StirringSpeed;

        public float TargetTemperature;
        public float InitialDelay;
        public float TargetPowerDiff;
        public float MeasuredTemperature { get; internal set; }
        public int InjectionCount => Injections.Count;
        public PeakHeatDirection AverageHeatDirection { get; set; } = PeakHeatDirection.Unknown;
        public bool UseIntegrationFactorLength { get; set; } = false;

        public DataProcessor Processor { get; private set; }
        public Solution Solution { get; set; }

        public ExperimentData(string file)
        {
            FileName = file;

            Processor = new DataProcessor(this);
        }

        public void AddInjection(string dataline)
        {
            var inj = new InjectionData(this, dataline, Injections.Count);

            Injections.Add(inj);
        }

        public void SetCustomIntegrationTimes(float delay, float length)
        {
            foreach (var inj in Injections) inj.SetCustomIntegrationTimes(delay, length);
        }

        public void CalculatePeakHeatDirection()
        {
            var tot_diff = 0.0;

            foreach (var inj in Injections)
            {
                var dat = DataPoints.Where(dp => dp.Time > inj.Time && dp.Time < inj.Time + inj.Delay);

                var mean = dat.Average(dp => dp.Power.Value);
                var min = dat.Min(dp => dp.Power.Value.Value);
                var max = dat.Max(dp => dp.Power.Value.Value);

                if ((mean - min) > (max - mean)) inj.HeatDirection = PeakHeatDirection.Exothermal;
                else inj.HeatDirection = PeakHeatDirection.Endothermal;

                tot_diff += (mean - min) - (max - mean);
            }

            if (tot_diff > 0) AverageHeatDirection = PeakHeatDirection.Endothermal;
            else AverageHeatDirection = PeakHeatDirection.Exothermal;
        }

        public void SetProcessor(DataProcessor processor)
        {
            Processor = processor;
        }
    }

    public class InjectionData
    {
        public ExperimentData Experiment { get; private set; }
        public int ID { get; private set; }

        float time = -1; 

        public float Time { get => time; set { time = value; SetIntegrationTimes(); } }
        public float Volume { get; private set; }
        public float Duration { get; private set; }
        public float Delay { get; private set; }
        public float Filter { get; private set; }
        public float Temperature { get; internal set; }
        public float InjectionMass { get; internal set; }
      
        public float ActualCellConcentration { get; internal set; }
        public float ActualTitrantConcentration { get; internal set; }
        public float Ratio { get; internal set; }

        public bool Include { get; internal set; } = true;
        public float IntegrationStartDelay { get; private set; } = 0;
        public float IntegrationLength { get; private set; } = 90;
        public float IntegrationStartTime => Time + IntegrationStartDelay;
        public float IntegrationEndTime => Time + IntegrationLength;

        public PeakHeatDirection HeatDirection { get; set; } = PeakHeatDirection.Unknown;

        public Energy PeakArea { get; private set; } = new();
        public Energy Enthalpy => PeakArea / InjectionMass;
        public double SD => PeakArea.SD;

        public Energy OffsetEnthalpy
        {
            get
            {
                if (Experiment.Solution == null) return Enthalpy;
                else return Enthalpy - Experiment.Solution.Offset;
            }
        }

        public bool IsIntegrated { get; private set; }

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

        void SetIntegrationTimes()
        {
            IntegrationStartDelay = 0;
            IntegrationLength = 0.9f * Delay;
        }

        public void SetCustomIntegrationTimes(float delay, float length)
        {
            IntegrationStartDelay = delay;

            if (Experiment.UseIntegrationFactorLength)
            {
                float maxtime = 0;

                var dps = Experiment.DataPoints.Where(dp => dp.Time > Time && dp.Time < Time + Delay);
                var ordered = dps.OrderBy(dp => dp.Power.Value.Value);
                var first = ordered.First();

                if (HeatDirection is PeakHeatDirection.Exothermal) maxtime = first.Time;
                else maxtime = Experiment.DataPoints.Where(dp => dp.Time > Time && dp.Time < Time + Delay).OrderBy(dp => dp.Power.Value).Last().Time;

                length = length * (maxtime - Time);

                IntegrationLength = Math.Clamp(length, Duration, Delay);
            }
            else IntegrationLength = Math.Clamp(length, Duration, Delay);
        }

        public void ToggleDataPointActive()
        {
            Include = !Include;
        }

        public void Integrate()
        {
            var data = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime);

            Energy area = new();

            foreach (var dp in data)
            {
                area += dp.Power;
            }

            var sd = EstimateError();

            var peakarea = new FloatWithError(area/1000000, sd/1000000);

            PeakArea = new(peakarea);

            IsIntegrated = true;
        }

        public double EstimateError()
        {
            var baselinedata = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationEndTime && dp.Time < Time + Delay);

            double sum_of_squares = 0;

            foreach (var dp in baselinedata)
            {
                var p = dp.Power.Value;

                sum_of_squares += p * p;
            }

            return Math.Sqrt(sum_of_squares / (baselinedata.Count() - 1));
        }
    }

    public struct DataPoint
    {
        public readonly Energy Power { get; }

        readonly EnergyUnit unit;

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

        public DataPoint(float time, Energy power, float temp)
        {
            this.Time = time;
            this.Power = power;
            this.Temperature = temp;
            this.unit = DataManager.Unit;
            this.ATP = 0;
            this.JFBI = 0;
            this.DT = 0;
            this.ShieldT = 0;
        }

        public DataPoint(float time, double power, float temp, EnergyUnit unit = EnergyUnit.Cal)
        {
            this.Time = time;
            this.Power = new Energy(power, unit);
            this.Temperature = temp;
            this.unit = unit;
            this.ATP = 0;
            this.JFBI = 0;
            this.DT = 0;
            this.ShieldT = 0;
        }

        public DataPoint(float time, double power, float temp = 0, float dt = 0, float shieldt = 0, float atp = 0, float jfbi = 0, EnergyUnit unit = EnergyUnit.Cal)
        {
            this.Time = time;
            this.Power = new Energy(power, unit);
            this.Temperature = temp;
            this.unit = unit;
            this.ATP = atp;
            this.JFBI = jfbi;
            this.DT = dt;
            this.ShieldT = shieldt;
        }

        public DataPoint(string line, ITCDataFormat format)
        {
            var dat = StringParsers.ParseLine(line);

            switch (format)
            {
                case ITCDataFormat.ITC200:
                case ITCDataFormat.VPITC:
                default:
                    this = new DataPoint(dat[0], dat[1], dat[2], dat[3], dat[4], dat[5], dat[6], EnergyUnit.Cal);
                    break;
            }
        }

        public static Energy Mean(List<DataPoint> list)
        {
            Energy sum = new(0.0);

            foreach (var dp in list) sum += dp.Power;

            return sum / list.Count;
        }

        public static Energy Median(List<DataPoint> list)
        {
            int count = list.Count();

            if (count % 2 == 0)
                return list.Select(x => x.Power).OrderBy(o => o).Skip((count / 2) - 1).Take(2).Average();
            else
                return list.Select(x => x.Power).OrderBy(x => x).ElementAt(count / 2);
        }

        public static Energy VolatilityWeightedAverage(List<DataPoint> list)
        {
            double w = 0;
            Energy sum = new(0.0);

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

            var first = DataPoint.Mean(list.Take(list.Count / 2).ToList()).Value;
            var last = DataPoint.Mean(list.Skip(list.Count / 2).ToList()).Value;

            return (last - first) / deltaX;
        }

        public DataPoint SubtractBaseline(Energy baseline)
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
}
