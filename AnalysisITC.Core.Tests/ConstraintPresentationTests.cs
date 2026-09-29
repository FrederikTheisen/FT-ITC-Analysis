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
    [InlineData(VariableConstraint.None, "Independent")]
    [InlineData(VariableConstraint.SameForAll, "Shared")]
    [InlineData(VariableConstraint.TemperatureDependent, "Temperature dependent")]
    [InlineData(VariableConstraint.ThermodynamicallyLinked, "Thermodynamically linked")]
    public void GenericConstraintPresentationIsConsistent(
        VariableConstraint constraint, string label)
    {
        Assert.Equal(label, ConstraintPresentation.Description(
            ParameterType.Enthalpy1, constraint));
        Assert.Equal(label, ConstraintPresentation.Description(
            ParameterType.Nvalue1, constraint));

        var tooltip = ConstraintPresentation.Tooltip(
            ParameterType.Enthalpy1, constraint);
        if (constraint == VariableConstraint.None)
            Assert.Equal("Each experiment has its own fitted value.", tooltip);
        else
            Assert.Equal(label, tooltip);
    }

    [Theory]
    [InlineData(VariableConstraint.None, "Independent", "Each experiment has its own fitted affinity.")]
    [InlineData(VariableConstraint.TemperatureDependent, "Shared ΔG", "One Gibbs-energy coordinate is shared; Kd varies with temperature.")]
    [InlineData(VariableConstraint.SameForAll, "Shared Kd", "One Kd value is shared across all experiments.")]
    [InlineData(VariableConstraint.ThermodynamicallyLinked, "Thermodynamically linked", "Gibbs energy is shared at the fit reference and follows the selected enthalpy relationship.")]
    public void AffinityConstraintPresentationKeepsThermodynamicMeaning(
        VariableConstraint constraint, string label, string tooltip)
    {
        Assert.Equal(label, ConstraintPresentation.Description(
            ParameterType.Affinity1, constraint));
        Assert.Equal(tooltip, ConstraintPresentation.Tooltip(
            ParameterType.Affinity1, constraint));
    }

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
        Assert.Equal("Shared", Assert.Single(saved.Constraints, item => item.Label == "Enthalpy").Value);
    }
}
