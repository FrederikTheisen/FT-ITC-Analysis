using System.Globalization;
using System.Linq;

using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ExperimentDesignerWindowTests
{
    [AvaloniaTheory]
    [InlineData(ITCInstrument.TAInstrumentsNanoITCLowVolume, 170, 5)]
    [InlineData(ITCInstrument.TAInstrumentsNanoITCStandard, 950, 25)]
    [InlineData(ITCInstrument.TAInstrumentsAffinityITCLowVolume, 185, 25)]
    [InlineData(ITCInstrument.TAInstrumentsAffinityITCStandard, 965, 25)]
    public void TaPresetsGenerateCorrectVolumesThroughRealControls(ITCInstrument instrument, double cellMicrolitres, double injectionMicrolitres)
    {
        var window = new ExperimentDesignerWindow();
        try
        {
            var choices = ITCInstrumentAttribute.GetITCInstruments();
            Assert.Equal(4, choices.Count(ITCInstrumentAttribute.IsTAInstrument));
            window.InstrumentComboForTesting.SelectedIndex = choices.IndexOf(instrument);
            window.SmallFirstInjectionCheckForTesting.IsChecked = false;
            window.AutoVolumeCheckForTesting.IsChecked = true;
            window.InjectionCountStepperForTesting.Value = 10;

            var data = window.GenerationFactoryForTesting!.Model.Data;
            Assert.Equal(instrument, data.Instrument);
            Assert.Equal(cellMicrolitres * 1e-6, data.CellVolume, 12);
            Assert.All(data.Injections, injection => Assert.Equal(injectionMicrolitres * 1e-6, injection.Volume, 12));
            Assert.Equal(instrument.GetProperties().Name, window.InstrumentComboForTesting.SelectedItem);
            Assert.Equal(instrument.GetProperties().Name, ToolTip.GetTip(window.InstrumentComboForTesting));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DesignerOpensWithoutBlockingWorkspaceAndClosesWithItsOwner()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var owner = new MainWindow();
        owner.SetStartupReady();
        owner.Show();
        try
        {
            var opening = owner.OpenExperimentDesignerAsync();
            var designer = Assert.IsType<ExperimentDesignerWindow>(Assert.Single(owner.OwnedWindows));

            Assert.True(designer.IsVisible);
            Assert.False(designer.IsDialog);
            Assert.True(opening.IsCompletedSuccessfully);

            owner.Close();

            Assert.False(designer.IsVisible);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            foreach (var child in owner.OwnedWindows.ToArray()) child.Close();
            DocumentDirtyTracker.MarkClean();
            owner.Close();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    [AvaloniaFact]
    public void NoiseLevelDefaultsToTheExistingSimulationMagnitudeAndFollowsNoiseEnablement()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var window = new ExperimentDesignerWindow();
            try
            {
                var slider = window.NoiseLevelSliderForTesting;

                Assert.Equal(0.1, slider.Minimum, 6);
                Assert.Equal(5, slider.Maximum, 6);
                Assert.Equal(0.1, slider.TickFrequency, 6);
                Assert.Equal(40, slider.Height, 6);
                Assert.Equal(1, slider.Value, 6);
                Assert.Equal(1, window.NoiseMultiplierForTesting, 6);
                Assert.Equal(FormatNoiseLevel(1), window.NoiseLevelTextForTesting.Text);
                Assert.False(slider.IsEnabled);

                window.SimulateNoiseCheckForTesting.IsChecked = true;

                Assert.True(slider.IsEnabled);
                slider.Value = 2.5;
                Assert.Equal(2.5, window.NoiseMultiplierForTesting, 6);
                Assert.Equal(FormatNoiseLevel(2.5), window.NoiseLevelTextForTesting.Text);

                window.SimulateNoiseCheckForTesting.IsChecked = false;

                Assert.False(slider.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    static string FormatNoiseLevel(double value) => value.ToString("0.0", CultureInfo.CurrentCulture) + "×";

    [AvaloniaFact]
    public void RegenerationResamplesNoiseButKeepsTheGenerationModel()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var window = new ExperimentDesignerWindow();
            try
            {
                var generation = window.GenerationFactoryForTesting!;
                var parameter = generation.GetExposedParameters().First();
                var originalValue = parameter.Value;

                var noiseless = generation.Model.Data.Injections.Select(injection => injection.PeakArea.Value).ToArray();
                window.RegenerateSyntheticData();
                Assert.Equal(noiseless,
                    generation.Model.Data.Injections.Select(injection => injection.PeakArea.Value));

                window.SimulateNoiseCheckForTesting.IsChecked = true;
                var firstNoiseDraw = generation.Model.Data.Injections.Select(injection => injection.PeakArea.Value).ToArray();
                window.RegenerateSyntheticData();
                var secondNoiseDraw = generation.Model.Data.Injections.Select(injection => injection.PeakArea.Value).ToArray();

                Assert.False(firstNoiseDraw.SequenceEqual(secondNoiseDraw));
                Assert.Equal(originalValue, parameter.Value);
                Assert.Same(generation.Model, generation.Model.Data.Model);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [AvaloniaFact]
    public void FitUsesIndependentModelAndKeepsGenerationParameters()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var window = new ExperimentDesignerWindow();
            try
            {
                var generation = window.GenerationFactoryForTesting!;
                var parameter = generation.GetExposedParameters().First(candidate => candidate.Value != 0);
                var originalValue = parameter.Value;
                generation.UpdateParameter(parameter.Key, originalValue * 1.1, false);
                var generationValue = parameter.Value;

                var fit = window.CreateFitFactory();
                var fitParameter = fit.Model.Parameters.Table[parameter.Key];

                Assert.NotSame(generation.Model, fit.Model);
                Assert.NotSame(generation.Model.Data, fit.Model.Data);
                Assert.NotSame(parameter, fitParameter);
                Assert.Equal(generationValue, fitParameter.Value);
                Assert.Equal(generation.Model.Data.Injections.Count, fit.Model.Data.Injections.Count);
                Assert.Equal(generation.Model.Data.Injections.Select(injection => injection.PeakArea.Value),
                    fit.Model.Data.Injections.Select(injection => injection.PeakArea.Value));

                fitParameter.Update(generationValue * 0.9);

                Assert.Equal(generationValue, parameter.Value);
                Assert.Same(generation.Model, generation.Model.Data.Model);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [AvaloniaFact]
    public void DesignerParameterSlidersMakeSmallUsefulAdjustments()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AssertSmallSliderMove(ParameterType.Nvalue1, 1, 0.11);
            AssertSmallSliderMove(ParameterType.Enthalpy1, -30000, 2100);
            AssertSmallSliderMove(ParameterType.Affinity1, 6, 0.1);
            AssertSmallSliderMove(ParameterType.Offset, 0, 650);
            AssertSmallSliderMove(ParameterType.HeatCapacity1, 1000, 150);
            AssertSmallSliderMove(ParameterType.Gibbs1, -30000, 1000);
            AssertSmallSliderMove(ParameterType.Entropy1, 10, 2);
            AssertSmallSliderMove(ParameterType.IsomerizationRate, 0.01, 0.001);
            AssertSmallSliderMove(ParameterType.IsomerizationEquilibriumConstant, 1, 0.1);
            AssertSmallSliderMove(ParameterType.CisIsomerPopulationPercentage, 50, 2);
        });
    }

    [AvaloniaFact]
    public void CommonDesignerParameterSlidersMatchMacOSRanges()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var previousLimitSetting = AppSettings.ParameterLimitSetting;
            try
            {
                AppSettings.ParameterLimitSetting = ParameterLimitSetting.NoLimit;
                AssertSliderEndpoints(ParameterType.Nvalue1, 1, 0.1, 10);
                AssertNSliderLogarithmicMidpoints();
                AssertSliderEndpoints(ParameterType.Affinity1, 6, 9, 3);
                AssertSliderEndpoints(ParameterType.Offset, 0, -30000, 30000);
                AssertSliderEndpoints(ParameterType.Enthalpy1, -30000, -100000, 100000);
            }
            finally
            {
                AppSettings.ParameterLimitSetting = previousLimitSetting;
            }
        });
    }

    static void AssertNSliderLogarithmicMidpoints()
    {
        var parameter = new Parameter(ParameterType.Nvalue1, 1);
        var row = AnalysisParameterRowBuilder.BuildDesigner(
            parameter,
            (_, value) => parameter.Update(value),
            _ => { },
            () => false);
        var slider = Assert.Single(row.GetLogicalDescendants().OfType<Slider>());

        Assert.Equal(0.5, slider.Value, 6);
        slider.Value = 0.25;
        Assert.Equal(System.Math.Sqrt(0.1), parameter.Value, 6);
        slider.Value = 0.75;
        Assert.Equal(System.Math.Sqrt(10), parameter.Value, 6);
    }

    static void AssertSliderEndpoints(ParameterType key, double initialValue, double leftValue, double rightValue)
    {
        var parameter = new Parameter(key, initialValue);
        var row = AnalysisParameterRowBuilder.BuildDesigner(
            parameter,
            (_, value) => parameter.Update(value),
            _ => { },
            () => false);
        var slider = Assert.Single(row.GetLogicalDescendants().OfType<Slider>());

        slider.Value = 0;
        Assert.Equal(leftValue, parameter.Value, 6);
        slider.Value = 1;
        Assert.Equal(rightValue, parameter.Value, 6);
    }

    static void AssertSmallSliderMove(ParameterType key, double initialValue, double maximumChange)
    {
        var parameter = new Parameter(key, initialValue);
        var row = AnalysisParameterRowBuilder.BuildDesigner(
            parameter,
            (_, value) => parameter.Update(value),
            _ => { },
            () => false);
        var slider = Assert.Single(row.GetLogicalDescendants().OfType<Slider>());
        slider.Value += 0.01;

        Assert.InRange(System.Math.Abs(parameter.Value - initialValue), 0.000001, maximumChange);
    }
}
