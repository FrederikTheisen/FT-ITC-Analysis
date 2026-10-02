using AnalysisITC.Core.Data;
using Xunit;

namespace AnalysisITC.Core.Tests;

[CollectionDefinition("Traceability metadata", DisableParallelization = true)]
public sealed class TraceabilityMetadataCollection { }

[Collection("Traceability metadata")]
public sealed class TraceabilityMetadataTests
{
    [Fact]
    public void ExperimentIdentifiersTrimOuterWhitespaceAndSignalEveryMetadataEditWithoutProcessingChanges()
    {
        var first = new ExperimentData("first.itc");
        var second = new ExperimentData("second.itc");
        var processingRevision = first.ProcessingRevision;
        var contentChanges = 0;
        first.ContentChanged += (_, _) => contentChanges++;

        first.ExternalExperimentId = "  Lab-β / 00017  ";
        first.ExternalExperimentId = "Lab-β / 00017";
        first.CellSampleId = "  cell α  ";
        first.SyringeSampleId = " syringe β ";

        Assert.Equal("Lab-β / 00017", first.ExternalExperimentId);
        Assert.Equal("cell α", first.CellSampleId);
        Assert.Equal("syringe β", first.SyringeSampleId);
        Assert.Equal("", second.ExternalExperimentId);
        Assert.Equal(3, contentChanges);
        Assert.Equal(processingRevision, first.ProcessingRevision);
        Assert.True(first.IsModified);
    }
}
