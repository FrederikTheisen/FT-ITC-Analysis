using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests;

public class ConstraintPresentationTests
{
    [Theory]
    [InlineData(VariableConstraint.TemperatureDependent, "Shared ΔG")]
    [InlineData(VariableConstraint.SameForAll, "Shared Kd")]
    [InlineData(VariableConstraint.ThermodynamicallyLinked, "Thermodynamically linked")]
    public async Task SequentialConstraintMeaningSurvivesReportsAndViewerRoundTrip(
        VariableConstraint constraint, string label)
    {
        var solution = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll,
            affinity: constraint, modelType: AnalysisModel.SequentialBindingSites, steps: 4);
        var result = new AnalysisResult(solution);
        foreach (var slot in ThermodynamicParameterSlots.All)
        {
            Assert.Equal(label, ConstraintPresentation.Description(slot.Affinity, constraint));
            Assert.Equal("Independent", ConstraintPresentation.Description(slot.Affinity, VariableConstraint.None));
        }
        Assert.Contains(label, result.GetResultString());
        Assert.Contains(label, result.GetListDescriptionString());
        var report = AnalysisReportBuilder.Build(result);
        Assert.Contains(report.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items), item => item.Value == label);

        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, solution.Model.Models.Select(model => model.Data), new[] { result });
        stream.Position = 0;
        var viewer = await new ViewerDocumentReader().ReadAsync(stream, "constraints.ftxtc", ViewerFileFormat.Ftxtc);
        var saved = Assert.Single(viewer.AnalysisResults);
        Assert.Equal(label, Assert.Single(saved.Constraints, item => item.Label == "Affinity").Value);
        Assert.Equal("Same for all", Assert.Single(saved.Constraints, item => item.Label == "Enthalpy").Value);
    }
}
