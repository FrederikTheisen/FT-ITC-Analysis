
using System;
using System.Globalization;

namespace AnalysisITC.Core.Utilities
{
    public static class StringParsers
    {
        public static float[] ParseLine(string line, char delimiter = ',')
        {
            var sdat = line.Split(delimiter);

            var fdat = new float[sdat.Length];

            for (int i = 0; i < sdat.Length; i++)
            {
                // Instrument rows use decimal points regardless of the UI locale.
                fdat[i] = float.Parse(sdat[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            return fdat;
        }
    }
}
