using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public class MicroCalSavedStateTests
{
    [Fact]
    public async Task RationalEraConcentrationsAndFitSurviveNativeLoadUntilExplicitReprocessing()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: true, method: DilutionMethod.MicroCal);
        InstallRationalEraState(model);
        foreach (var replicate in model.Solution.BootstrapSolutions) InstallRationalEraState(replicate.Model);
        model.Solution.ComputeErrorsFromBootstrapSolutions();
        var expectedConcentrations = model.Data.Injections.Select(i => i.ActualTitrantConcentration).ToArray();
        var expectedHeats = model.Data.Injections.Select(i => model.Evaluate(i.ID)).ToArray();
        var expectedParameters = model.Solution.Parameters.ToDictionary(p => p.Key, p => p.Value);

        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, new[] { model.Data });
        stream.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<ExperimentData>());
        Assert.True(restored.Solution.IsValid);
        Assert.Equal(DilutionMethod.MicroCal, restored.AppliedDilutionMethod);
        Assert.Equal(expectedConcentrations, restored.Injections.Select(i => i.ActualTitrantConcentration));
        Assert.Equal(expectedHeats, restored.Injections.Select(i => restored.Model.Evaluate(i.ID)));
        foreach (var parameter in expectedParameters)
        {
            Assert.Equal(parameter.Value.Value, restored.Solution.Parameters[parameter.Key].Value);
            Assert.Equal(parameter.Value.SD, restored.Solution.Parameters[parameter.Key].SD);
        }
        Assert.Equal(2, restored.Solution.BootstrapSolutions.Count);
        for (var index = 0; index < 2; index++)
        {
            var original = model.Solution.BootstrapSolutions[index];
            var copy = restored.Solution.BootstrapSolutions[index];
            Assert.Equal(original.Data.Injections.Select(i => i.ActualTitrantConcentration),
                copy.Data.Injections.Select(i => i.ActualTitrantConcentration));
            Assert.Equal(original.Data.Injections.Select(i => original.Model.Evaluate(i.ID)),
                copy.Data.Injections.Select(i => copy.Model.Evaluate(i.ID)));
        }

        var savedSolution = restored.Solution;
        RawDataReader.ReprocessInjections(restored, DilutionMethod.MicroCal);
        double cumulativeVolume = 0;
        foreach (var injection in restored.Injections)
        {
            cumulativeVolume += injection.Volume;
            var u = cumulativeVolume / restored.CellVolume;
            var expected = restored.SyringeConcentration.Value * u * (1 - u / 2);
            Assert.Equal(expected, injection.ActualTitrantConcentration, 14);
        }
        Assert.False(expectedConcentrations.SequenceEqual(restored.Injections.Select(i => i.ActualTitrantConcentration)));
        Assert.False(savedSolution.IsValid);
        foreach (var parameter in expectedParameters)
            Assert.Equal(parameter.Value.Value, savedSolution.Parameters[parameter.Key].Value);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.None)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    public void SyntheticClonesPreserveCapturedRationalEraConcentrations(ErrorEstimationMethod method)
    {
        var model = InjectionProcessingMethodTests.FittedModel(method: DilutionMethod.MicroCal);
        InstallRationalEraState(model);
        model.ModelCloneOptions = new ModelCloneOptions
        {
            ErrorEstimationMethod = method, DiscardedDataPoint = 2,
            IncludeConcentrationErrorsInBootstrap = false,
        };
        var clone = model.GenerateSyntheticModel(new Random(17));
        Assert.Equal(model.Data.Injections.Select(i => i.ActualCellConcentration),
            clone.Data.Injections.Select(i => i.ActualCellConcentration));
        Assert.Equal(model.Data.Injections.Select(i => i.ActualTitrantConcentration),
            clone.Data.Injections.Select(i => i.ActualTitrantConcentration));
        Assert.Equal(model.Data.Injections.Select(i => i.Ratio), clone.Data.Injections.Select(i => i.Ratio));
    }

    static void InstallRationalEraState(Model model)
    {
        // Reconstruct the former stored convention independently of either helper.
        double volume = 0;
        foreach (var injection in model.Data.Injections)
        {
            volume += injection.Volume;
            injection.ActualTitrantConcentration = model.Data.SyringeConcentration.Value * volume
                / (model.Data.CellVolume + volume / 2);
            injection.Ratio = injection.ActualTitrantConcentration / injection.ActualCellConcentration;
        }
    }
}
