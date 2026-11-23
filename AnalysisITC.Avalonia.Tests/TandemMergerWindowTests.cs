using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Utilities;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class TandemMergerWindowTests
{
    [AvaloniaTheory]
    [InlineData(0, TandemMixingCriterion.OneSiteFit)]
    [InlineData(1, TandemMixingCriterion.ModelFree)]
    public async Task AutomaticMergePublishesAnEligibleTandem(int index, TandemMixingCriterion criterion)
    {
        var previousMethod = AppSettings.DilutionCalculationMethod;
        var previousError = FittingOptionsController.ErrorEstimationMethod;
        var previousIterations = FittingOptionsController.BootstrapIterations;
        var previousCreateResult = AppSettings.CreateSingleAnalysisResult;
        DataManager.Clear(DataClearMode.ResetSession);
        AppSettings.DilutionCalculationMethod = DilutionMethod.DiscreteDisplacement;
        try
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tandem",
                "280-430-D2mut-1p6mM-JNK-200uM-1_process2.ftxtc"));
            var sources = (await FTXTCReader.ReadStream(stream)).OfType<ExperimentData>().Take(2).ToArray();
            foreach (var source in sources)
                foreach (var injection in source.Injections)
                    injection.SetPeakArea(new FloatWithError(1e-6));
            DataManager.AddData(sources);
            var merger = new TandemMergerWindow();
            try
            {
                merger.ModeComboForTesting.SelectedIndex = 2;
                merger.AutoCriterionComboForTesting.SelectedIndex = index;
                var create = typeof(TandemMergerWindow).GetMethod("CreateMergedExperimentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await Assert.IsAssignableFrom<Task>(create.Invoke(merger, null));
                var merged = Assert.Single(DataManager.Data, data => data.IsTandemExperiment);
                Assert.Equal(criterion, merged.TandemMergeInfo.Criterion);
                Assert.True(merged.Processor.IsLocked);
                Assert.NotEqual(1e-6, merged.Injections[1].PeakArea.Value);

                var fit = new AnalysisWorkspaceControl { Experiment = merged };
                var window = new Window { Content = fit };
                window.Show();
                try
                {
                    fit.ErrorMethodComboForTesting.SelectedIndex = 1;
                    Assert.True(fit.TandemMixingCheck.IsVisible);
                    Assert.True(fit.TandemMixingCheck.IsEnabled);
                    Assert.False(fit.TandemMixingCheck.IsChecked);
                    fit.TandemMixingCheck.IsChecked = true;
                    fit.RefreshIncludedDataState();
                    Assert.True(fit.TandemMixingCheck.IsChecked);
                    fit.BootstrapIterationsBoxForTesting.Text = "10";
                    AppSettings.CreateSingleAnalysisResult = false;
                    var workspace = (AnalysisWorkspace)typeof(AnalysisWorkspaceControl)
                        .GetField("workspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fit)!;
                    fit.Experiment = sources[0];
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(fit.TandemMixingCheck.IsVisible);
                    fit.Experiment = merged;
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(fit.TandemMixingCheck.IsVisible);
                    Assert.True(fit.TandemMixingCheck.IsChecked);
                    foreach (var requested in new[] { true, false })
                    {
                        fit.TandemMixingCheck.IsChecked = requested;
                        workspace.ReestimateTandemMixing = !requested;

                        fit.RunFit();
                        var prepared = fit.ContextForTesting!.SingleModel;
                        var deadline = DateTime.UtcNow.AddSeconds(15);
                        while (ProcessingActivity.IsBusy && DateTime.UtcNow < deadline)
                            await Task.Delay(10);
                        Assert.False(ProcessingActivity.IsBusy);
                        Assert.NotNull(prepared.Solution);
                        Assert.Equal(requested, prepared.ModelCloneOptions.ReestimateTandemMixing);
                    }
                    fit.TandemMixingCheck.IsChecked = true;
                    fit.ErrorMethodComboForTesting.SelectedIndex = 2;
                    Assert.False(fit.TandemMixingCheck.IsVisible);
                    fit.ErrorMethodComboForTesting.SelectedIndex = 1;
                    Assert.True(fit.TandemMixingCheck.IsVisible);
                    Assert.True(fit.TandemMixingCheck.IsChecked);

                }
                finally { window.Close(); }
            }
            finally { if (merger.IsVisible) merger.Close(); }
        }
        finally
        {
            AppSettings.DilutionCalculationMethod = previousMethod;
            FittingOptionsController.ErrorEstimationMethod = previousError;
            FittingOptionsController.BootstrapIterations = previousIterations;
            AppSettings.CreateSingleAnalysisResult = previousCreateResult;
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US", "80.5")]
    [InlineData("da-DK", "80,5")]
    public void DeadVolumeValidationPreservesDraftsAndIgnoresTheInactiveField(string cultureName, string positive)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            RunWithExperiments(2, window =>
            {
                const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                var box = (TextBox)typeof(TandemMergerWindow).GetField("deadVolumeBox", hidden)!.GetValue(window)!;
                var refresh = typeof(TandemMergerWindow).GetMethod("RefreshControls", hidden)!;
                var read = typeof(TandemMergerWindow).GetMethod("TryReadSettings", hidden)!;
                var selected = window.ExperimentListForTesting.SelectedItems!.Cast<object>().ToArray();
                window.ModeComboForTesting.SelectedIndex = 1;
                var mixing = window.MixingSlidersForTesting[0].Value;

                foreach (var text in new[] { "", "abc", "80 uL", "-1", "NaN", "Infinity", "∞", "-∞", "1e999" })
                {
                    box.Text = text;
                    refresh.Invoke(window, null);
                    Assert.False((bool)read.Invoke(window, new object?[] { null })!);
                    Assert.False(window.CreateButtonForTesting.IsEnabled);
                    Assert.Equal(text, box.Text);
                    Assert.Equal(selected, window.ExperimentListForTesting.SelectedItems!.Cast<object>().ToArray());
                    Assert.Equal(mixing, window.MixingSlidersForTesting[0].Value);

                    window.ModeComboForTesting.SelectedIndex = 0;
                    Assert.True((bool)read.Invoke(window, new object?[] { null })!);
                    Assert.True(window.CreateButtonForTesting.IsEnabled);
                    Assert.Equal(text, box.Text);
                    window.ModeComboForTesting.SelectedIndex = 1;
                }

                foreach (var text in new[] { "0", positive })
                {
                    box.Text = text;
                    refresh.Invoke(window, null);
                    var arguments = new object?[] { null };
                    Assert.True((bool)read.Invoke(window, arguments)!);
                    Assert.True(window.CreateButtonForTesting.IsEnabled);
                    var settings = Assert.IsType<TandemConcatenation.BackMixingSettings>(arguments[0]);
                    Assert.Equal(text == "0" ? 0 : 80.5e-6, settings.DeadVolume, 15);
                }
            });
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task AutomaticScanFailureRemainsVisibleAfterCleanupAndCanBeRetried(int criterionIndex)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var sources = new[] { CreateExperiment(1), CreateExperiment(2) };
        DataManager.AddData(sources);
        // Keep the baseline-corrected fixture unintegrated when the processing workspace binds it.
        MainWindow main;
        using (DocumentDirtyTracker.RestoreDocument())
            main = new MainWindow();
        main.SetStartupReady();
        var merger = new TandemMergerWindow();
        Task<bool?>? mergerCompletion = null;
        try
        {
            main.Show();
            mergerCompletion = merger.ShowDialog<bool?>(main);
            merger.ModeComboForTesting.SelectedIndex = 2;
            merger.AutoCriterionComboForTesting.SelectedIndex = criterionIndex;
            Assert.True(merger.CreateButtonForTesting.IsEnabled);

            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            var createMethod = typeof(TandemMergerWindow).GetMethod("CreateMergedExperimentAsync", privateInstance)!;
            var cancel = (Button)typeof(TandemMergerWindow).GetField("cancelButton", privateInstance)!.GetValue(merger)!;
            Window? previousAlert = null;

            for (var attempt = 0; attempt < 2; attempt++)
            {
                await Assert.IsAssignableFrom<Task>(createMethod.Invoke(merger, null));
                Dispatcher.UIThread.RunJobs();

                var alert = Assert.Single(main.OwnedWindows, window => !ReferenceEquals(window, merger));
                Assert.NotSame(previousAlert, alert);
                Assert.Contains(alert.GetLogicalDescendants().OfType<TextBlock>(), block =>
                    block.Text?.Contains("All included injections must be integrated before running a tandem mixing scan.") == true);

                Dispatcher.UIThread.RunJobs();
                Assert.True(alert.IsVisible);
                Assert.Same(alert, Assert.Single(main.OwnedWindows, window => !ReferenceEquals(window, merger)));
                Assert.True(merger.IsVisible);
                Assert.False(mergerCompletion.IsCompleted);
                Assert.False(merger.Created);
                Assert.True(merger.CreateButtonForTesting.IsEnabled);
                Assert.True(cancel.IsEnabled);
                Assert.True(merger.ExperimentListForTesting.IsEnabled);
                Assert.False(ProcessingActivity.IsBusy);
                Assert.Collection(DataManager.Data,
                    source => Assert.Same(sources[0], source),
                    source => Assert.Same(sources[1], source));
                Assert.DoesNotContain(DataManager.Data, source => source.IsTandemExperiment);
                Assert.All(sources, source => Assert.All(source.Injections, injection =>
                {
                    Assert.True(injection.Include);
                    Assert.False(injection.IsIntegrated);
                }));

                alert.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "OK"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.False(alert.IsVisible);
                Assert.Same(merger, Assert.Single(main.OwnedWindows));
                previousAlert = alert;
            }
        }
        finally
        {
            foreach (var alert in main.OwnedWindows.Where(window => !ReferenceEquals(window, merger)).ToArray())
                alert.Close();
            merger.Close();
            if (mergerCompletion != null) await mergerCompletion;
            DocumentDirtyTracker.MarkClean();
            main.Close();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    [AvaloniaFact]
    public void SourceSelectionRequiresCompletedBaselineCorrection()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var corrected = CreateExperiment(1);
        var raw = CreateExperiment(2);
        raw.Processor.BaselineCompleted = false;
        DataManager.AddData(new[] { corrected, raw });
        var window = new TandemMergerWindow();
        try
        {
            Assert.Single(window.ExperimentListForTesting.Items);
            Assert.False(window.CreateButtonForTesting.IsEnabled);
        }
        finally
        {
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    [AvaloniaFact]
    public void AnotherCalculationDisablesTandemCreation()
    {
        RunWithExperiments(3, window =>
        {
            var create = window.CreateButtonForTesting;
            using (var lease = ProcessingActivity.TryBegin()!)
            {
                Dispatcher.UIThread.RunJobs();
                Assert.False(create.IsEnabled);
                Assert.Equal(FittingStatusPresentation.BusyTooltip, ToolTip.GetTip(create));
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(create.IsEnabled);
        });
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(5)]
    public void IndividualFractionsAreUnavailableOutsideThreeOrFourExperiments(int experimentCount)
    {
        RunWithExperiments(experimentCount, window =>
        {
            window.ModeComboForTesting.SelectedIndex = 1;

            Assert.False(window.IndividualMixingCheckForTesting.IsVisible);
            Assert.Null(window.IndividualTransitionMixingFractionsForTesting());
            Assert.True(window.MixingRowsForTesting[0].IsVisible);
            Assert.False(window.MixingRowsForTesting[1].IsVisible);
            Assert.False(window.MixingRowsForTesting[2].IsVisible);
        });
    }

    [AvaloniaTheory]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    public void IndividualFractionsExposeOneOrderedValuePerReload(
        int experimentCount,
        int reloadCount)
    {
        RunWithExperiments(experimentCount, window =>
        {
            window.ModeComboForTesting.SelectedIndex = 1;

            Assert.True(window.IndividualMixingCheckForTesting.IsVisible);
            Assert.Null(window.IndividualTransitionMixingFractionsForTesting());
            Assert.Single(window.MixingRowsForTesting, row => row.IsVisible);

            window.MixingSlidersForTesting[0].Value = 0.30;
            window.IndividualMixingCheckForTesting.IsChecked = true;

            Assert.Equal(reloadCount, window.MixingRowsForTesting.Count(row => row.IsVisible));
            Assert.All(window.MixingSlidersForTesting, slider => Assert.Equal(0.30, slider.Value, 6));

            var expected = new[] { 0.10, 0.25, 0.40 }.Take(reloadCount).ToArray();
            for (var index = 0; index < reloadCount; index++)
                window.MixingSlidersForTesting[index].Value = expected[index];

            Assert.Equal(expected, window.IndividualTransitionMixingFractionsForTesting());

            window.IndividualMixingCheckForTesting.IsChecked = false;

            Assert.Null(window.IndividualTransitionMixingFractionsForTesting());
            Assert.All(window.MixingSlidersForTesting, slider => Assert.Equal(expected[0], slider.Value, 6));
        });
    }

    [AvaloniaFact]
    public void AutomaticModeDoesNotUseManualIndividualFractions()
    {
        RunWithExperiments(3, window =>
        {
            window.ModeComboForTesting.SelectedIndex = 1;
            window.IndividualMixingCheckForTesting.IsChecked = true;
            window.ModeComboForTesting.SelectedIndex = 2;

            Assert.False(window.IndividualMixingCheckForTesting.IsVisible);
            Assert.False(window.IndividualMixingCheckForTesting.IsChecked);
            Assert.Null(window.IndividualTransitionMixingFractionsForTesting());
            Assert.False(window.MixingRowsForTesting[1].IsVisible);
            Assert.False(window.MixingRowsForTesting[2].IsVisible);
        });
    }

    [AvaloniaFact]
    public void AutomaticModeExposesBothCriteriaAndPreservesTheChoice()
    {
        RunWithExperiments(3, window =>
        {
            Assert.Equal(3, window.ModeComboForTesting.ItemCount);
            Assert.False(window.AutoCriterionRowForTesting.IsVisible);

            window.ModeComboForTesting.SelectedIndex = 1;
            Assert.Null(window.AutomaticCriterionForTesting);
            Assert.False(window.AutoCriterionRowForTesting.IsVisible);

            window.ModeComboForTesting.SelectedIndex = 2;
            Assert.Equal(TandemMixingCriterion.OneSiteFit, window.AutomaticCriterionForTesting);

            Assert.True(window.AutoCriterionRowForTesting.IsVisible);
            Assert.True(window.AutoCriterionComboForTesting.IsEnabled);
            Assert.Equal(2, window.AutoCriterionComboForTesting.ItemCount);
            window.AutoCriterionComboForTesting.SelectedIndex = 1;
            Assert.Equal(TandemMixingCriterion.ModelFree, window.AutomaticCriterionForTesting);
            window.ModeComboForTesting.SelectedIndex = 0;
            Assert.False(window.AutoCriterionRowForTesting.IsVisible);
            Assert.Null(window.AutomaticCriterionForTesting);
            window.ModeComboForTesting.SelectedIndex = 2;
            Assert.Equal(TandemMixingCriterion.ModelFree, window.AutomaticCriterionForTesting);
            Assert.False(window.IndividualMixingCheckForTesting.IsVisible);
            Assert.Null(window.IndividualTransitionMixingFractionsForTesting());
        });
    }

    [AvaloniaFact]
    public void LeavingAnEligibleSelectionRestoresSharedFractionMode()
    {
        RunWithExperiments(5, window =>
        {
            window.SelectExperimentCountForTesting(4);
            window.ModeComboForTesting.SelectedIndex = 1;
            window.IndividualMixingCheckForTesting.IsChecked = true;
            window.MixingSlidersForTesting[0].Value = 0.15;
            window.MixingSlidersForTesting[1].Value = 0.25;
            window.MixingSlidersForTesting[2].Value = 0.35;

            window.SelectExperimentCountForTesting(5);

            Assert.False(window.IndividualMixingCheckForTesting.IsVisible);
            Assert.False(window.IndividualMixingCheckForTesting.IsChecked);
            Assert.Null(window.IndividualTransitionMixingFractionsForTesting());
            Assert.All(window.MixingSlidersForTesting, slider => Assert.Equal(0.15, slider.Value, 6));

            window.SelectExperimentCountForTesting(4);

            Assert.True(window.IndividualMixingCheckForTesting.IsVisible);
            Assert.False(window.IndividualMixingCheckForTesting.IsChecked);
            Assert.Single(window.MixingRowsForTesting, row => row.IsVisible);
        });
    }

    [AvaloniaFact]
    public void IndividualRowsFollowSelectionBetweenTwoAndThreeReloads()
    {
        RunWithExperiments(4, window =>
        {
            window.SelectExperimentCountForTesting(3);
            window.ModeComboForTesting.SelectedIndex = 1;
            window.IndividualMixingCheckForTesting.IsChecked = true;

            Assert.Equal(2, window.MixingRowsForTesting.Count(row => row.IsVisible));
            Assert.Equal(2, window.IndividualTransitionMixingFractionsForTesting()?.Count);

            window.SelectExperimentCountForTesting(4);

            Assert.True(window.IndividualMixingCheckForTesting.IsChecked);
            Assert.Equal(3, window.MixingRowsForTesting.Count(row => row.IsVisible));
            Assert.Equal(3, window.IndividualTransitionMixingFractionsForTesting()?.Count);

            window.SelectExperimentCountForTesting(3);

            Assert.True(window.IndividualMixingCheckForTesting.IsChecked);
            Assert.Equal(2, window.MixingRowsForTesting.Count(row => row.IsVisible));
            Assert.Equal(2, window.IndividualTransitionMixingFractionsForTesting()?.Count);
        });
    }

    static void RunWithExperiments(int count, Action<TandemMergerWindow> assertion)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            DataManager.AddData(Enumerable.Range(1, count).Select(CreateExperiment));
            var window = new TandemMergerWindow();

            try
            {
                Assert.Equal(count, window.ExperimentListForTesting.SelectedItems?.Count);
                assertion(window);
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    static ExperimentData CreateExperiment(int index)
    {
        var experiment = new ExperimentData($"tandem-source-{index}.itc")
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 200e-6,
            DataPoints = new List<DataPoint>
            {
                new(0, 0),
                new(1, 0),
            },
        };
        experiment.Injections.Add(new InjectionData(experiment, 1e-6));
        experiment.BaseLineCorrectedDataPoints = experiment.DataPoints.ToList();
        experiment.Processor.BaselineCompleted = true;
        return experiment;
    }
}
