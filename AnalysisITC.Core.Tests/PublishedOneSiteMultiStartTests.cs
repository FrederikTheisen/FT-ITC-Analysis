using System;
using System.IO;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection("Published model reproduction")]
public sealed class PublishedOneSiteMultiStartTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ITestOutputHelper output;

    public PublishedOneSiteMultiStartTests(ITestOutputHelper output)
    {
        this.output = output;
        PreferencesState.Defaults().ApplyToSettings();
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(new MicrocalPrompt());
    }

    public void Dispose()
    {
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
        original.ApplyToSettings();
    }

    [Theory]
    [InlineData(SolverAlgorithm.LevenbergMarquardt, DilutionMethod.MicroCal)]
    [InlineData(SolverAlgorithm.NelderMead, DilutionMethod.MicroCal)]
    [InlineData(SolverAlgorithm.LevenbergMarquardt, DilutionMethod.Exponential)]
    [InlineData(SolverAlgorithm.NelderMead, DilutionMethod.Exponential)]
    public void PublishedPytcFitRecoversFromPredeterminedAlternativeStarts(SolverAlgorithm algorithm, DilutionMethod dilution)
    {
        // These starts are specified independently of the source fit and are
        // not modified in response to optimizer outcomes. No parameters locked.
        var starts = new[] { (.7, 6.5, -8000.0, -100.0), (1.3, 8.5, -16000.0, 100.0), (1.0, 7.0, -12000.0, 0.0) };
        foreach (var (n, logK, enthalpyCal, offsetCal) in starts)
        {
            AppSettings.DilutionCalculationMethod = dilution;
            var data = IntegratedHeatReader.ReadFile(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "PublishedBenchmarks", "pytc-ca-edta-tris-01.DH"));
            data.Injections[0].Include = false;
            data.Injections[1].Include = false;
            var model = new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, n);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, logK);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, enthalpyCal * 4.184);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, offsetCal * 4.184);
            var convergence = new Solver
            {
                Model = model, SolverAlgorithm = algorithm, UseErrorWeightedFitting = false,
                ErrorEstimationMethod = ErrorEstimationMethod.None, MaxOptimizerIterations = 20000, Silent = true,
            }.Solve();
            Assert.True(convergence.Success, convergence.Message);
            var fittedN = model.Parameters.Table[ParameterType.Nvalue1].Value;
            var fittedK = Math.Pow(10, model.Parameters.Table[ParameterType.Affinity1].Value);
            var fittedH = model.Parameters.Table[ParameterType.Enthalpy1].Value / 4.184;
            output.WriteLine($"{algorithm}/{dilution} start=({n},{logK},{enthalpyCal},{offsetCal}): N={fittedN:G10}, Ka={fittedK:G10}, dH={fittedH:G10} cal/mol");
            // Same 2% acceptance as the independent published/source targets;
            // a different starting point is not a reason to relax agreement.
            Assert.InRange(Math.Abs(fittedN / .973948 - 1), 0, .02);
            Assert.InRange(Math.Abs(fittedK / 4.05476e7 - 1), 0, .02);
            Assert.InRange(Math.Abs(fittedH / -11566.9 - 1), 0, .02);
        }
    }

    sealed class MicrocalPrompt : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse) =>
            new(EnergyUnit.MicroCal, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
