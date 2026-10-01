using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ModelAvailabilityTests
{
    [Theory]
    [InlineData(AnalysisModel.Offset, true)]
    [InlineData(AnalysisModel.Dissociation, true)]
    [InlineData(AnalysisModel.OneSetOfSites, false)]
    [InlineData(AnalysisModel.TwoSetsOfSites, false)]
    public void ZeroCellConcentrationAllowsOnlyModelsWithoutCellMacromolecule(AnalysisModel model, bool expected)
    {
        // Buffer in the cell: Offset and Dissociation heats depend only on the injected titrant.
        var experiment = CreateExperiment(cellConcentration: 0, syringeConcentration: 1e-3);

        Assert.Equal(expected, AnalysisBuilder.IsModelAvailable(model, false, new[] { experiment }));
    }

    [Fact]
    public void OffsetRequiresSyringeConcentration()
    {
        var experiment = CreateExperiment(cellConcentration: 1e-4, syringeConcentration: 0);

        Assert.False(AnalysisBuilder.IsModelAvailable(AnalysisModel.Offset, false, new[] { experiment }));
    }

    static ExperimentData CreateExperiment(double cellConcentration, double syringeConcentration)
    {
        var experiment = new ExperimentData("availability.itc")
        {
            CellConcentration = new FloatWithError(cellConcentration),
            SyringeConcentration = new FloatWithError(syringeConcentration),
            CellVolume = 1e-3,
        };
        for (var index = 0; index < 4; index++)
        {
            var injection = new InjectionData(experiment, index, 1e-6, 0, include: true);
            injection.SetPeakArea(new FloatWithError(-1e-6, 1e-8));
            experiment.Injections.Add(injection);
        }
        return experiment;
    }
}
