using System;
namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public class ParameterChecker
    {
        public static bool IsWithinOnePercent(double parameter, double lowerLimit, double upperLimit)
        {
            // Handling the case where limits can be both positive and negative.
            if (lowerLimit < 0 && upperLimit > 0)
            {
                return IsWithinRange(parameter, 0.99 * lowerLimit, 0.99 * upperLimit);
            }

            // Handling strictly positive parameters.
            if (lowerLimit >= 0)
            {
                return IsWithinRange(parameter, 1.01 * lowerLimit, 0.99 * upperLimit);
            }

            // Handling strictly negative parameters.
            if (upperLimit <= 0)
            {
                return IsWithinRange(parameter, 0.99 * lowerLimit, 1.01 * upperLimit);
            }

            return false;
        }

        private static bool IsWithinRange(double parameter, double lowerLimit, double upperLimit)
        {
            return parameter >= lowerLimit && parameter <= upperLimit;
        }
    }
}

