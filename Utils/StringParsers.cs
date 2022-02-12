using System;
namespace Utilities
{
    public static class StringParsers
    {
        public static float[] ParseLine(string line, char delimiter = ',')
        {
            var sdat = line.Split(delimiter);

            var fdat = new float[sdat.Length];

            for (int i = 0; i < sdat.Length; i++)
            {
                fdat[i] = float.Parse(sdat[i].Trim());
            }

            return fdat;
        }
    }
}
