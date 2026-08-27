using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>
    /// Creates independent, thread-confined random streams for bootstrap
    /// replicates. Seeds are deliberately not persisted as part of an analysis.
    /// </summary>
    internal static class BootstrapRandomStreams
    {
        public static Random CreateOne() => Create(1)[0];

        public static Random[] Create(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            var streams = new Random[count];
            var seeds = new HashSet<int>();
            var bytes = new byte[sizeof(int)];

            using (var generator = RandomNumberGenerator.Create())
            {
                for (var i = 0; i < count; i++)
                {
                    int seed;
                    do
                    {
                        generator.GetBytes(bytes);
                        seed = BitConverter.ToInt32(bytes, 0) & int.MaxValue;
                    }
                    while (!seeds.Add(seed));

                    streams[i] = new Random(seed);
                }
            }

            return streams;
        }
    }
}
