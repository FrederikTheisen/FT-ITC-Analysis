using System;
using System.Collections.Generic;
using System.Linq;
using DataReaders;

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

        public float PeakArea { get; private set; } = 0;
        public float Enthalpy { get; private set; } = 0;
        public float SD { get; private set; } = 0;

        public bool IsIntegrated { get; private set; }

        public InjectionData(string line, int id)
        {
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

        public void Integrate(List<DataPoint> baselinesubtracteddata)
        {
            var data = baselinesubtracteddata.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime);

            var area = 0f;

            foreach (var dp in data)
            {
                area += dp.Power;
            }

            PeakArea = area;
            Enthalpy = PeakArea / InjectionMass;

            EstimateError(baselinesubtracteddata);

            IsIntegrated = true;
        }

        public void EstimateError(List<DataPoint> baselinesubtracteddata)
        {
            var baselinedata = baselinesubtracteddata.Where(dp => dp.Time > IntegrationEndTime && dp.Time < Time + Delay);

            float sum_of_squares = 0;

            foreach (var dp in baselinedata)
            {
                sum_of_squares += dp.Power * dp.Power;
            }

            SD = (float)Math.Sqrt(sum_of_squares / (baselinedata.Count() - 1));
        }
    }

    public struct DataPoint
    {
        readonly float time;
        readonly float power;
        readonly float temperature;
        readonly EnergyUnit unit;

        public DataPoint(float time, float power, float temp, EnergyUnit unit = EnergyUnit.Cal)
        {
            this.time = time;
            this.power = power;
            this.temperature = temp;
            this.unit = unit;
        }

        public static DataPoint FromLine(string line, EnergyUnit unit = EnergyUnit.Cal)
        {
            var data = Utilities.StringParsers.ParseLine(line);

            return new DataPoint(data[0], data[1], data[2], unit);
        }

        public float Power
        {
            get
            {
                if (unit == ExperimentData.Unit) return power;
                else
                {
                    switch (unit)
                    {
                        default:
                        case EnergyUnit.Joule: return power / 4.184f;
                        case EnergyUnit.Cal: return power * 4.184f;
                    }
                }
            }
        }

        public float Time => time;
        public float Temperature => temperature;

        public static float Mean(List<DataPoint> list)
        {
            float sum = 0;

            foreach (var dp in list) sum += dp.Power;

            return sum / list.Count;
        }

        public static float Median(List<DataPoint> list)
        {
            int count = list.Count();

            if (count % 2 == 0)
                return list.Select(x => x.Power).OrderBy(o => o).Skip((count / 2) - 1).Take(2).Average();
            else
                return list.Select(x => x.Power).OrderBy(x => x).ElementAt(count / 2);
        }

        public static float VolatilityWeightedAverage(List<DataPoint> list)
        {
            float w = 0;
            float sum = 0;

            for (int i = 1; i < list.Count; i++)
            {
                float _w = .01f / (0.005f + Math.Abs(list[i].Power - list[i - 1].Power));

                w += _w;
                sum += _w * list[i].Power;
            }

            return sum / w;
        }

        public static float Slope(List<DataPoint> list)
        {
            if (list.Count == 0) return 0;

            var deltaX = (list.Last().Time - list.First().Time) / 2;

            var first = DataPoint.Mean(list.Take(list.Count / 2).ToList());
            var last = DataPoint.Mean(list.Skip(list.Count / 2).ToList());

            return (last - first) / deltaX;
        }
    }

    public enum EnergyUnit
    {
        Joule,
        Cal
    }

    public enum PeakHeatDirection
    {
        Unknown,
        Exothermal,
        Endothermal
    }
}
