using System;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>
    /// Per-run overrides for updating an existing Analysis Result.
    /// A null bootstrap override preserves the result's stored rerun behavior.
    /// </summary>
    public sealed class AnalysisResultUpdateOptions
    {
        public static AnalysisResultUpdateOptions StoredSettings { get; } = new AnalysisResultUpdateOptions();

        public int? BootstrapIterationsOverride { get; }

        public AnalysisResultUpdateOptions(int? bootstrapIterationsOverride = null)
        {
            if (bootstrapIterationsOverride.HasValue && bootstrapIterationsOverride.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(bootstrapIterationsOverride));

            BootstrapIterationsOverride = bootstrapIterationsOverride;
        }
    }
}
