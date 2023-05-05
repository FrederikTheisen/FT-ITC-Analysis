using System;

namespace AnalysisITC
{
    public static class FWEMath
    {
        public static FloatWithError Log(FloatWithError number)
        {
            return new FloatWithError(Math.Log(number.Value), number.SD / number.Value);
        }

        public static FloatWithError Exp(FloatWithError number)
        {
            var f = Math.Abs(Math.Exp(number.Value));
            var sd = Math.Abs(number.SD);

            return new FloatWithError(f, f * sd);
        }
    }

}
