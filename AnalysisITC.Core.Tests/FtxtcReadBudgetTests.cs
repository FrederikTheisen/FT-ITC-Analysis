using AnalysisITC.Core.DataReaders;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class FtxtcReadBudgetTests
    {
        [Fact]
        public void BootstrapInjectionRecordsCountReplicatesTimesDeclaredInjections()
        {
            const int replicateCount = 1_000;
            const int injectionCount = 24;

            var records = FtxtcReadBudget.BootstrapInjectionRecords(replicateCount, injectionCount);

            Assert.Equal(24_000L, records);
            Assert.NotEqual((long)replicateCount * replicateCount, records);
        }
    }
}
