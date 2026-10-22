using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class AnalysisReportBuilderTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    [InlineData(true, 4)]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(false, 4)]
    public void ReportOmitsIntegrationCountsAndRetainsProcessingDetails(bool baselineComplete, int integratedCount)
    {
        var result = CreateResult(1);
        var fitted = result.Solution.Solutions[0].Data;
        var supporting = CreateExperiment(4, 25);
        foreach (var data in new[] { fitted, supporting })
        {
            data.Processor.BaselineCompleted = baselineComplete;
            for (var index = 0; index < data.Injections.Count; index++)
                data.Injections[index].IsIntegrated = index < integratedCount;
        }
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { supporting.UniqueID });

        var document = AnalysisReportBuilder.Build(report,
            id => id == result.UniqueID ? result : null,
            id => id == supporting.UniqueID ? supporting : null);

        Assert.True(document.IsValid);
        var processing = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Processing and integration");
        Assert.DoesNotContain(processing.Items, item => item.Label == "Integrated injections");
        Assert.Contains(processing.Items, item => item.Label == "Baseline method");
        Assert.Contains(processing.Items, item => item.Label == "Injection use" && item.Value.Contains("3 included"));
        Assert.Contains(processing.Items, item => item.Label == "Integration regions");
        Assert.Contains(processing.Items, item => item.Label == "Start after injection" && item.IndentLevel == 1);
        Assert.Contains(processing.Items, item => item.Label == "End after injection" && item.IndentLevel == 1);
        var baseline = processing.Items.SingleOrDefault(item => item.Label == "Baseline status");
        if (baselineComplete) Assert.Null(baseline);
        else Assert.Equal("Incomplete", Assert.IsType<AnalysisReportKeyValueItem>(baseline).Value);

        var notes = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().SingleOrDefault(block => block.Title == "Notes");
        if (baselineComplete) Assert.Null(notes);
        else
        {
            var note = Assert.Single(Assert.IsType<AnalysisReportKeyValueBlock>(notes).Items);
            Assert.Equal("Baseline", note.Label);
            Assert.Equal("Incomplete", note.Value);
        }
    }
}
