using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection("Solver events")]
    public sealed class TandemMixingBootstrapTests : IDisposable
    {
        readonly PreferencesState preferences = PreferencesState.FromSettings();
        readonly ErrorEstimationMethod previousErrorMethod = FittingOptionsController.ErrorEstimationMethod;
        readonly bool previousPool = FittingOptionsController.PoolBootstrapResiduals;
        readonly bool previousConcentrations = FittingOptionsController.IncludeConcentrationVariance;
        static readonly TandemConcatenation.BackMixingSettings Settings = new TandemConcatenation.BackMixingSettings
        {
            UseBackMixingMethod = true,
            DeadVolume = 80e-6,
            DidRemoveOverflow = true,
        };

        public TandemMixingBootstrapTests()
        {
            PreferencesState.Defaults().ApplyToSettings();
            AppSettings.DilutionCalculationMethod = DilutionMethod.DiscreteDisplacement;
            FittingOptionsController.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            FittingOptionsController.PoolBootstrapResiduals = true;
            FittingOptionsController.IncludeConcentrationVariance = true;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }

        public void Dispose()
        {
            preferences.ApplyToSettings();
            FittingOptionsController.ErrorEstimationMethod = previousErrorMethod;
            FittingOptionsController.PoolBootstrapResiduals = previousPool;
            FittingOptionsController.IncludeConcentrationVariance = previousConcentrations;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }

        [Theory]
        [InlineData(TandemMixingCriterion.ModelFree)]
        [InlineData(TandemMixingCriterion.OneSiteFit)]
        public async Task AutomaticMergeSelectsFractionsFromItsIntegratedHeats(TandemMixingCriterion criterion)
        {
            var sources = await ReadSources();
            // Deliberately replace the source peak areas without changing their corrected traces.
            foreach (var source in sources)
                foreach (var injection in source.Injections)
                    injection.SetPeakArea(new FloatWithError(1e-6 * (1 + injection.ID % 4)));

            var merged = await TandemConcatenation.ConcatAutomaticAsync(sources, Settings, criterion, showProgress: false);
            Assert.True(merged.Processor.IsLocked);
            Assert.True(merged.Processor.IntegrationCompleted);
            Assert.Equal(criterion, merged.TandemMergeInfo.Criterion);
            Assert.Equal(sources.Select(source => source.UniqueID), merged.TandemSourceExperimentIds);
            Assert.NotEqual(sources[0].Injections[1].PeakArea.Value, merged.Injections[1].PeakArea.Value);

            var scratch = merged.GetSynthClone(new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None }, new Random(0));
            var ranges = Ranges(merged);
            var method = merged.TandemMergeInfo.Bookkeeping;
            var replay = criterion == TandemMixingCriterion.ModelFree
                ? TandemContinuityScanner.FindBest(scratch, ranges, Settings, null, method, logResults: false)
                : TandemMixingScanner.FindBestOneSite(scratch, ranges, Settings, method);
            Assert.Equal(replay.TransitionMixingFractions, merged.TandemMergeInfo.Fractions);
            TandemConcatenation.ProcessInjectionsWithBackMixingModel(scratch, ranges, Settings, replay.TransitionMixingFractions, method);
            Assert.Equal(Concentrations(scratch), Concentrations(merged));
            AssertFirstReloadMassBalance(merged);

            if (criterion == TandemMixingCriterion.OneSiteFit)
            {
                var seed = TandemMixingBootstrap.PrepareOneSite(merged);
                var fast = seed.FindBest(scratch, ranges, Settings);
                Assert.NotNull(fast);
                foreach (var (grid, fitted) in replay.TransitionMixingFractions.Zip(fast.TransitionMixingFractions))
                    Assert.InRange(Math.Abs(grid - fitted), 0, TandemMixingScanner.AdaptiveRefinementStep);
                Assert.InRange(fast.Rmsd, 0, replay.Rmsd + 1e-5);
                Assert.Null(seed.FindBest(scratch, ranges, Settings, evaluationLimit: 1));
                SolverInterface.TerminateAnalysisFlag.Raise();
                try { Assert.Throws<OptimizerStopException>(() => seed.FindBest(scratch, ranges, Settings)); }
                finally { SolverInterface.TerminateAnalysisFlag.Lower(); }
            }
        }

        [Fact]
        public async Task MixedPooledBootstrapRetainsReplicateHistoriesThroughStrictReopen()
        {
            var sources = await ReadSources();
            var modelFree = await TandemConcatenation.ConcatAutomaticAsync(sources, Settings, TandemMixingCriterion.ModelFree, showProgress: false);
            var oneSite = await TandemConcatenation.ConcatAutomaticAsync(sources, Settings, TandemMixingCriterion.OneSiteFit, showProgress: false);
            modelFree.Name = "Model-free tandem";
            oneSite.Name = "One-site tandem";
            var ordinary = sources[0];
            ordinary.SetID(Guid.NewGuid().ToString());
            ordinary.Name = "Ordinary experiment";
            var experiments = new[] { modelFree, oneSite, ordinary };
            foreach (var experiment in experiments)
            {
                experiment.CellConcentration = new FloatWithError(experiment.CellConcentration.Value, 1e-7);
                experiment.SyringeConcentration = new FloatWithError(experiment.SyringeConcentration.Value, 2e-7);
            }

            var workspace = new AnalysisWorkspace { ReestimateTandemMixing = true };
            workspace.SetGlobalMode(true);
            workspace.SetConstraint(ParameterType.Affinity1, VariableConstraint.SameForAll, rebuild: false);
            Assert.True(workspace.TryRebuild(experiments));
            Assert.True(workspace.CanReestimateTandemMixing);
            // Desktop inspectors check starting values when the prepared fit is published.
            workspace.ContextRebuilt += (_, _) => workspace.Context.DetectInitialParameterLimitViolations();
            var solver = Assert.IsType<GlobalSolver>(workspace.PrepareForSolve(false));
            solver.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            solver.BootstrapIterations = 8;
            solver.Silent = true;
            var concentrationBefore = experiments.Select(Concentrations).ToArray();
            Assert.True(solver.Solve().Success);
            var solution = solver.Solution;
            Assert.NotEmpty(solution.BootstrapSolutions);
            Assert.True(solution.ModelCloneOptions.ReestimateTandemMixing);
            Assert.True(solution.ModelCloneOptions.PoolBootstrapResiduals);
            Assert.Equal(new[] { true, true, false }, solution.Solutions.Select(m => m.Model.ModelCloneOptions.ReestimateTandemMixing));

            // Both paths consume exactly the same heat and stock draws.
            var pool = PooledBootstrapResiduals.Prepare(solver.Model, weighted: false);
            var remerged = solver.Model.GenerateSyntheticModel(new Random(552), solver.Model.ModelCloneOptions, pool);
            foreach (var member in solver.Model.Models) member.ModelCloneOptions.ReestimateTandemMixing = false;
            var fixedFractions = solver.Model.GenerateSyntheticModel(new Random(552), solver.Model.ModelCloneOptions, pool);
            for (int i = 0; i < experiments.Length; i++)
            {
                var original = experiments[i];
                var a = remerged.Models[i].Data;
                var b = fixedFractions.Models[i].Data;
                Assert.Equal(b.Injections.Select(Observation), a.Injections.Select(Observation));
                Assert.Equal(b.CellConcentration.Value, a.CellConcentration.Value);
                Assert.Equal(b.SyringeConcentration.Value, a.SyringeConcentration.Value);
                Assert.Equal(concentrationBefore[i], Concentrations(original));
                if (i < 2)
                {
                    Assert.Same(original.TandemMergeInfo, a.TandemMergeInfo);
                    // Stock draws are shared, so a difference after the reload comes from mixing.
                    Assert.NotEqual(b.Injections.Last().Ratio, a.Injections.Last().Ratio);
                    solver.Model.Models[i].ModelCloneOptions.ReestimateTandemMixing = true;
                }
                else Assert.Equal(Concentrations(b), Concentrations(a));
            }

            var result = new AnalysisResult(solution);
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, experiments, new[] { result });
            FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(package);
            package.Position = 0;
            var restoredContents = await FTXTCReader.ReadStream(package);
            var restored = Assert.Single(restoredContents.OfType<AnalysisResult>());
            Assert.Equal(experiments.Select(e => e.UniqueID), restored.Solution.Solutions.Select(m => m.Data.UniqueID));
            Assert.True(restored.Model.ModelCloneOptions.ReestimateTandemMixing);

            foreach (var (saved, reopened) in solution.Solutions.Zip(restored.Solution.Solutions))
            {
                Assert.Equal(saved.Model.ModelCloneOptions.ReestimateTandemMixing, reopened.Model.ModelCloneOptions.ReestimateTandemMixing);
                Assert.Equal(saved.BootstrapSolutions.Count, reopened.BootstrapSolutions.Count);
                foreach (var (before, after) in saved.BootstrapSolutions.Zip(reopened.BootstrapSolutions))
                {
                    Assert.Equal(Concentrations(before.Data), Concentrations(after.Data));
                    Assert.Equal(before.Data.Segments?.Select(s => s.SegmentInitialActiveTitrantConc),
                        after.Data.Segments?.Select(s => s.SegmentInitialActiveTitrantConc));
                }
                if (!reopened.Data.IsTandemExperiment) continue;
                int reload = reopened.Data.Segments[1].FirstInjectionID;
                var x = reopened.Data.Injections[reload].Ratio;
                var bandLimits = reopened.Model.EvaluateBootstrap(reload).WithConfidence();
                Assert.Equal(saved.Model.EvaluateBootstrap(reload).WithConfidence(), bandLimits);
                Assert.Contains(reopened.BootstrapSolutions, refit => refit.Data.Injections[reload].Ratio != x);
                var options = new PublicationFigureOptions { DrawFitOffsetCorrected = false, ShowConfidenceBand = true };
                var band = Assert.Single(PublicationFigureBuilder.Build(reopened.Data, options).Panels
                    .Single(panel => panel.Kind == PublicationPanelKind.Fit).Bands);
                double scale = Energy.ScaleFactor(options.ResolvedEnergyUnit);
                Assert.Contains(band.Lower, point => point.X == x && point.Y == bandLimits[0] * scale);
                Assert.Contains(band.Upper, point => point.X == x && point.Y == bandLimits[1] * scale);
            }
            var mixingRows = UncertaintyEstimationPresentation.Section(restored.Solution).Rows;
            Assert.Contains(mixingRows, row => row.Label == "Experiment 1 mixing" && row.Value == "Included"
                && row.Tooltip.Contains(modelFree.Name));
            Assert.Contains(mixingRows, row => row.Label == "Experiment 2 mixing" && row.Value == "Included"
                && row.Tooltip.Contains(oneSite.Name));

            DataManager.Init();
            try
            {
                DataManager.AddData(restoredContents);
                var updated = await AnalysisResultUpdater.UpdateAsync(restored);
                Assert.True(updated.Success);
                Assert.NotEmpty(restored.Solution.BootstrapSolutions);
                Assert.Equal(new[] { true, true, false }, restored.Solution.Solutions
                    .Select(m => m.Model.ModelCloneOptions.ReestimateTandemMixing));
            }
            finally { DataManager.Clear(DataClearMode.ResetSession); }
        }

        [Fact]
        public async Task UnscorableModelFreeWindowFailsRefitsAndKeepsThePrimaryFit()
        {
            var merged = await TandemConcatenation.ConcatAutomaticAsync(await ReadSources(), Settings,
                TandemMixingCriterion.ModelFree, showProgress: false);
            int reload = merged.Segments[1].FirstInjectionID;
            for (int i = reload - TandemContinuityScanner.PointsPerSide; i < reload; i++)
                merged.Injections[i].Include = false;

            var workspace = new AnalysisWorkspace { ReestimateTandemMixing = true };
            Assert.True(workspace.TryRebuild(new[] { merged }));
            var solver = Assert.IsType<Solver>(workspace.PrepareForSolve(false));
            solver.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            solver.BootstrapIterations = 3;
            solver.Silent = true;
            var history = Concentrations(merged);
            var fit = solver.Solve();
            Assert.True(fit.Success);
            Assert.Equal(ErrorEstimationOutcome.CompleteFailure, fit.ErrorEstimationOutcome);
            Assert.Equal(3, fit.ErrorEstimationAttemptedRefits);
            Assert.Empty(solver.Solution.BootstrapSolutions);
            Assert.Equal(history, Concentrations(merged));
        }

        static (int, bool, double, double) Observation(InjectionData i) => (i.ID, i.Include, i.PeakArea.Value, i.PeakArea.SD);

        static void AssertFirstReloadMassBalance(ExperimentData experiment)
        {
            var reload = experiment.Segments[1];
            var shotVolumes = experiment.Injections.Take(reload.FirstInjectionID).Select(i => i.Volume).ToArray();
            double cell = experiment.CellConcentration.Value;
            double syringe = experiment.SyringeConcentration.Value;
            double volume = experiment.CellVolume;
            double retention = shotVolumes.Aggregate(1.0, (value, shot) => value * (1 - shot / volume));
            double displacedVolume = shotVolumes.Sum();
            double deadVolume = experiment.TandemMergeInfo.DeadVolume;
            double mixedVolume = deadVolume * experiment.TandemMergeInfo.Fractions[0];
            double activeCell = cell * retention;
            double activeTitrant = syringe * (1 - retention);
            // The overflow is removed from a well-mixed compartment, so its concentration is unchanged.
            double deadCell = (cell * deadVolume + (cell - activeCell) * volume) / (deadVolume + displacedVolume);
            double deadTitrant = (syringe * displacedVolume - activeTitrant * volume) / (deadVolume + displacedVolume);
            Assert.Equal((volume * activeCell + mixedVolume * deadCell) / (volume + mixedVolume),
                reload.SegmentInitialActiveCellConc, 14);
            Assert.Equal((volume * activeTitrant + mixedVolume * deadTitrant) / (volume + mixedVolume),
                reload.SegmentInitialActiveTitrantConc, 14);
        }

        static (double, double, double)[] Concentrations(ExperimentData experiment) =>
            experiment.Injections.Select(i => (i.ActualCellConcentration, i.ActualTitrantConcentration, i.Ratio)).ToArray();

        static List<TandemConcatenation.TandemInjectionSegment> Ranges(ExperimentData experiment) =>
            experiment.Segments.Select((segment, index) => new TandemConcatenation.TandemInjectionSegment(segment.FirstInjectionID,
                (index + 1 < experiment.Segments.Count ? experiment.Segments[index + 1].FirstInjectionID : experiment.InjectionCount)
                - segment.FirstInjectionID)).ToList();

        static async Task<List<ExperimentData>> ReadSources()
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tandem", "280-430-D2mut-1p6mM-JNK-200uM-1_process2.ftxtc"));
            return (await FTXTCReader.ReadStream(stream)).OfType<ExperimentData>().Take(3).ToList();
        }
    }
}
