using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
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
}
