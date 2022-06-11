using System;
using System.Collections.Generic;

namespace AnalysisITC
{
    static class Distribution
    {
        static readonly Random Random = new Random();

        public static DistributionType Selected { get; private set; } = DistributionType.Normal;

        public static void SetDefaultDistribution(DistributionType distributionType)
        {
            Selected = distributionType;
        }

        public static double Default(FloatWithError number, Random rand = null) => Default(number.Value, number.SD, rand);
        public static double Default(double mean, double stdDev, Random rand = null)
        {
            return Selected switch
            {
                DistributionType.Constant => Constant(mean, stdDev, rand),
                DistributionType.Normal => Normal(mean, stdDev, rand),
                _ => mean,
            };
        }

        public static double Default(double mean, double stdDev, List<double> distribution, Random rand = null)
        {
            if (distribution != null && Selected != DistributionType.None)
            {
                if (rand == null) rand = Random;
                return distribution[rand.Next(distribution.Count)];
            }
            else return Selected switch
            {
                DistributionType.Constant => Constant(mean, stdDev, rand),
                DistributionType.Normal => Normal(mean, stdDev, rand),
                _ => mean,
            };
        }

        public static double Normal(FloatWithError number, Random rand = null) => Normal(number.Value, number.SD, rand);
        public static double Normal(double mean, double stdDev, Random rand = null)
        {
            if (rand == null) rand = Random;
            double u1 = 1.0 - rand.NextDouble(); //uniform(0,1] random doubles
            double u2 = 1.0 - rand.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
            double randNormal = mean + stdDev * randStdNormal; //random normal(mean,stdDev^2)

            return randNormal;
        }

        public static double Constant(FloatWithError number, Random rand = null) => Constant(number.Value, number.SD, rand);
        public static double Constant(double mean, double stdDev, Random rand = null)
        {
            if (rand == null) rand = Random;
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
