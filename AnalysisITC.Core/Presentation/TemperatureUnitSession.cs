using System;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Session-scoped temperature choices for results and result export.</summary>
    public static class TemperatureUnitSession
    {
        static TemperatureUnit results;
        static TemperatureUnit resultExporter;

        public static event EventHandler ResultsChanged;
        public static event EventHandler ResultExporterChanged;

        public static TemperatureUnit Results
        {
            get => results;
            set
            {
                if (results == value) return;
                results = value;
                ResultsChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static TemperatureUnit ResultExporter
        {
            get => resultExporter;
            set
            {
                if (resultExporter == value) return;
                resultExporter = value;
                ResultExporterChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static void Reset(TemperatureUnit unit)
        {
            Results = unit;
            ResultExporter = unit;
        }
    }
}
