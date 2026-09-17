using System;
using System.IO;

namespace AnalysisITC.Core.DataReaders
{
    /// <summary>Optional web-supplied budgets for bounded FTXTC restoration.</summary>
    public sealed class FtxtcReadLimits
    {
        public long ExpandedArchiveBytes { get; set; } = long.MaxValue;
        public long IndividualMaterializedPayloadBytes { get; set; } = long.MaxValue;
        public long IndividualJsonPayloadBytes { get; set; } = long.MaxValue;
        public long CumulativeJsonBytes { get; set; } = long.MaxValue;
        public long CumulativeBinaryBytes { get; set; } = long.MaxValue;
        public long TotalRestoredSamples { get; set; } = long.MaxValue;
        public long RootComponents { get; set; } = long.MaxValue;
        public long BootstrapReplicates { get; set; } = long.MaxValue;
        public long InjectionRecords { get; set; } = long.MaxValue;
        public long ViewerArrayBytes { get; set; } = long.MaxValue;
    }

    public sealed class FtxtcResourceLimitException : Exception
    {
        public string Budget { get; }
        public FtxtcResourceLimitException(string budget, string message) : base(message) => Budget = budget;
    }
}
