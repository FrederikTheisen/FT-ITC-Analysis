using AnalysisITC.Core.Application;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class SupportReportBuilderTests
    {
        [Fact]
        public void UsesPublicSupportAddress()
        {
            Assert.Equal("support@ft-itc.org", SupportReportBuilder.SupportAddress);
        }
    }
}
