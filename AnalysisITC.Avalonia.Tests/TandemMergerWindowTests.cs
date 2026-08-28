using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Threading;

using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class TandemMergerWindowTests
{
    public TandemMergerWindowTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Theory]
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

    [Theory]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
        return experiment;
    }
}
