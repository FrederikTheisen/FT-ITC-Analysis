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
        first.ExternalExperimentId = "Lab-β / 00018";
        second.ExternalExperimentId = first.ExternalExperimentId;

        Assert.Equal("Lab-β / 00018", first.ExternalExperimentId);
        Assert.Equal(first.ExternalExperimentId, second.ExternalExperimentId);
        Assert.Equal(2, contentChanges);
        Assert.Equal(processingRevision, first.ProcessingRevision);
        Assert.True(first.IsModified);
    }
}
