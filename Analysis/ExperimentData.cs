using System;
using System.Collections.Generic;
using System.Linq;
using DataReaders;
using Utilities;

namespace AnalysisITC
{
    public class ExperimentData
    {
        public static EnergyUnit Unit = EnergyUnit.Joule;

        public string FileName { get; private set; } = "";

        public List<DataPoint> DataPoints = new List<DataPoint>();
        public List<DataPoint> BaseLineCorrectedDataPoints;
        public List<InjectionData> Injections = new List<InjectionData>();

        public float TargetTemperature;
        public float InitialDelay;
        public float StirringSpeed;
        public float TargetPowerDiff;

        public float SyringeConcentration;
        public float CellConcentration;
        public float CellVolume;

        public PeakHeatDirection AverageHeatDirection { get; set; } = PeakHeatDirection.Unknown;

        public int InjectionCount => Injections.Count;

        public float MeasuredTemperature { get; internal set; }
        public DateTime Date { get; internal set; }

        public DataProcessor Processor { get; private set; }
        public Analyzer Analyzer { get; private set; }

        public ExperimentData(string file)
        {
            FileName = file;

            Processor = new DataProcessor(this);
            Analyzer = new Analyzer(this);
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
            var tot_diff = 0f;

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

        float time;

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
        public float IntegrationStartTime { get; set; }
        public float IntegrationEndTime { get; set; }

        public PeakHeatDirection HeatDirection { get; set; } = PeakHeatDirection.Unknown;

        public Energy PeakArea { get; private set; } = new(0);
        public Energy Enthalpy => PeakArea / InjectionMass;
        public double SD => PeakArea.SD;

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
            IntegrationStartTime = Time;
            IntegrationEndTime = Time + 0.9f * Delay;
        }

        public void SetCustomIntegrationTimes(float delay, float length)
        {
            IntegrationStartTime = Time + delay;
            IntegrationEndTime = IntegrationStartTime + length;
        }

        public void ToggleDataPointActive()
        {
            Include = !Include;
        }

        public void Integrate()
        {
            var data = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime);

            double area = 0.0;

            foreach (var dp in data)
            {
                area += dp.Power;
            }

            var sd = EstimateError();

            PeakArea = new(new FloatWithError(area / 1000000, sd));

            IsIntegrated = true;
        }

        public double EstimateError()
        {
            var baselinedata = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationEndTime && dp.Time < Time + Delay);

            double sum_of_squares = 0;

            foreach (var dp in baselinedata)
            {
                var p = dp.Power / 1000000;

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
        public float Temperature { get; }
        //public EnergyUnit Unit => unit;

        public DataPoint(float time, double power, float temp, EnergyUnit unit = EnergyUnit.Cal)
        {
            this.Time = time;
            this.Power = new Energy(power, unit);
            this.Temperature = temp;
            this.unit = unit;
        }

        public static DataPoint FromLine(string line, EnergyUnit unit = EnergyUnit.Cal)
        {
            var data = Utilities.StringParsers.ParseLine(line);

            return new DataPoint(data[0], data[1], data[2], unit);
        }

        

        public static Energy Mean(List<DataPoint> list)
        {
            Energy sum = new(0);

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
            Energy sum = new(0);

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

        public DataPoint Copy(double subtract = 0)
        {
            return new DataPoint(Time, Power - subtract, Temperature, unit);
        }
    }

    public enum PeakHeatDirection
    {
        Unknown,
        Exothermal,
        Endothermal
    }
}
