using System;
using System.Collections.Generic;

namespace AnalysisITC
{
    class Distribution
    {
        static readonly Random rand = new Random();

        public static DistributionType Selected { get; private set; } = DistributionType.Normal;

        public static void SetDefaultDistribution(DistributionType distributionType)
        {
            Selected = distributionType;
        }

        public static double Default(FloatWithError number) => Default(number.Value, number.SD);
        public static double Default(double mean, double stdDev)
        {
            return Selected switch
            {
                DistributionType.Constant => Constant(mean, stdDev),
                DistributionType.Normal => Normal(mean, stdDev),
                _ => mean,
            };
        }

        public static double Default(double mean, double stdDev, List<double> distribution)
        {
            if (distribution != null && Selected != DistributionType.None)
            {
                return distribution[rand.Next(distribution.Count)];
            }
            else return Selected switch
            {
                DistributionType.Constant => Constant(mean, stdDev),
                DistributionType.Normal => Normal(mean, stdDev),
                _ => mean,
            };
        }

        public static double Normal(FloatWithError number) => Normal(number.Value, number.SD);
        public static double Normal(double mean, double stdDev)
        {
            double u1 = 1.0 - rand.NextDouble(); //uniform(0,1] random doubles
            double u2 = 1.0 - rand.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
            double randNormal = mean + stdDev * randStdNormal; //random normal(mean,stdDev^2)

            return randNormal;
        }

        public static double Constant(FloatWithError number) => Constant(number.Value, number.SD);
        public static double Constant(double mean, double stdDev)
        {
            double u = rand.NextDouble(); //uniform[0,1) random double
            double randStdCons = 2 * (0.5 - u); //uniform(-1,1] random double
            double randCons = mean + stdDev * randStdCons; //random constant within mean +/- SD

            return randCons;
        }

        public static double None(FloatWithError number) => None(number.Value, number.SD);
        public static double None(double mean, double stdDev)
        {
            return mean;
        }

        public enum DistributionType
        {
            Constant,
            Normal,
            None
        }
    }
}
