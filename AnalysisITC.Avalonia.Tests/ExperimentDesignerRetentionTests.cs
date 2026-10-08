using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ExperimentDesignerRetentionTests
{
    static readonly Dictionary<ParameterType, double> OneSiteValues = new()
    {
        [ParameterType.Nvalue1] = 1.7,
        [ParameterType.Enthalpy1] = -43210,
        [ParameterType.Affinity1] = 7.25,
        [ParameterType.Offset] = 1234,
    };

    public ExperimentDesignerRetentionTests() => AvaloniaTestBootstrap.EnsureInitialized();

    public static IEnumerable<object[]> SetupTriggers() => new[]
    {
        "instrument", "cell", "syringe", "count", "auto volume", "manual volume",
        "small first injection", "tandem", "tandem segments", "model switch",
    }.Select(trigger => new object[] { trigger });

    [Theory]
    [MemberData(nameof(SetupTriggers))]
    public void SetupChangesKeepParameterValues(string trigger)
    {
        WithWindow(window =>
        {
            foreach (var (key, value) in OneSiteValues)
                window.ApplyUserParameter(key, value);
            var before = window.GenerationFactoryForTesting!;

            Trigger(window, trigger);

            var after = window.GenerationFactoryForTesting!;
            Assert.NotSame(before, after);
            Assert.Equal(AnalysisModel.OneSetOfSites, after.ModelType);
            foreach (var (key, value) in OneSiteValues)
                Assert.Equal(value, Value(after, key));
            Assert.Equal(OneSiteValues.Count, window.ParameterPanelForTesting.Children.Count);
        });
    }

    public static IEnumerable<object[]> DesignerModels() =>
        AnalysisModelAttribute.GetAll().Select(model => new object[] { model });

    [Theory]
    [MemberData(nameof(DesignerModels))]
    public void InjectionCountChangeKeepsEveryParameterInEachModel(AnalysisModel model)
    {
        WithWindow(window =>
        {
            SelectModel(window, model);
            var expected = new Dictionary<ParameterType, double>();
            foreach (var parameter in window.GenerationFactoryForTesting!.GetExposedParameters().ToList())
            {
                var value = parameter.Value == 0 ? 123.0 : parameter.Value * 1.07;
                window.ApplyUserParameter(parameter.Key, value);
                expected[parameter.Key] = value;
            }

            window.InjectionCountStepperForTesting.Value = 21;

            var factory = window.GenerationFactoryForTesting!;
            Assert.Equal(model, factory.ModelType);
            Assert.Equal(21, factory.Model.Data.Injections.Count);
            Assert.Equal(expected.Keys.OrderBy(key => key), factory.GetExposedParameters().Select(p => p.Key).OrderBy(key => key));
            foreach (var (key, value) in expected)
                Assert.Equal(value, Value(factory, key));
        });
    }

    [Fact]
    public void UnchangedSetupCommitRegeneratesNoiseWithoutChangingParameters()
    {
        WithWindow(window =>
        {
            window.SimulateNoiseCheckForTesting.IsChecked = true;
            window.ApplyUserParameter(ParameterType.Nvalue1, 1.7);
            var before = window.GenerationFactoryForTesting!;
            var peaks = Peaks(before);

            PressEnter(window.CellConcentrationBoxForTesting);

            var after = window.GenerationFactoryForTesting!;
            Assert.NotSame(before, after);
            Assert.False(peaks.SequenceEqual(Peaks(after)));
            Assert.Equal(1.7, Value(after, ParameterType.Nvalue1));
            Assert.Equal(-30000, Value(after, ParameterType.Enthalpy1));
        });
    }

    [Fact]
    public void ValuesEnteredInParameterRowsSurviveSetupChanges()
    {
        WithWindow(window =>
        {
            var keys = window.GenerationFactoryForTesting!.GetExposedParameters().Select(p => p.Key).ToList();
            var row = window.ParameterPanelForTesting.Children[keys.IndexOf(ParameterType.Nvalue1)];
            var box = row.GetLogicalDescendants().OfType<TextBox>().First();
            box.Text = 1.7.ToString(CultureInfo.CurrentCulture);
            PressEnter(box);

            window.InjectionCountStepperForTesting.Value = 21;

            Assert.Equal(1.7, Value(window.GenerationFactoryForTesting!, ParameterType.Nvalue1), 10);
        });
    }

    [Fact]
    public void OptionChangesRebuildParameterRowsAndSurviveSetupChanges()
    {
        WithWindow(window =>
        {
            SelectModel(window, AnalysisModel.SequentialBindingSites);
            window.ApplyUserParameter(ParameterType.Affinity1, 6.5);
            Assert.Equal(5, window.ParameterPanelForTesting.Children.Count);
            var siteCount = window.OptionPanelForTesting.GetLogicalDescendants().OfType<TextBox>().Single(box => box.Text == "2");

            siteCount.Text = "3";
            siteCount.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

            Assert.Equal(7, window.ParameterPanelForTesting.Children.Count);
            Assert.Equal(-30000, Value(window.GenerationFactoryForTesting!, ParameterType.Enthalpy3));

            window.InjectionCountStepperForTesting.Value = 21;

            var factory = window.GenerationFactoryForTesting!;
            Assert.Equal(7, factory.GetExposedParameters().Count());
            Assert.Equal(7, window.ParameterPanelForTesting.Children.Count);
            Assert.Equal(6.5, Value(factory, ParameterType.Affinity1));
        });
    }

    [Fact]
    public void FittingLocksInputsAndRestoresTheirStates()
    {
        WithWindow(window =>
        {
            var factory = window.GenerationFactoryForTesting!;
            Assert.False(window.InjectionVolumeBoxForTesting.IsEnabled);
            Assert.False(window.NoiseLevelSliderForTesting.IsEnabled);
            var solver = new Solver();

            window.BeginFit(solver);

            Assert.True(window.IsFittingForTesting);
            Assert.False(window.CellConcentrationBoxForTesting.IsEffectivelyEnabled);
            Assert.False(window.ModelComboForTesting.IsEffectivelyEnabled);
            Assert.False(window.ParameterPanelForTesting.IsEffectivelyEnabled);
            Assert.False(window.FitButtonForTesting.IsEnabled);

            window.InjectionCountStepperForTesting.Value = 25;
            window.ApplyUserParameter(ParameterType.Nvalue1, 2.5);
            Assert.Same(factory, window.GenerationFactoryForTesting);
            Assert.Equal(20, factory.Model.Data.Injections.Count);
            Assert.Equal(1, Value(factory, ParameterType.Nvalue1));

            new Solver().ReportAnalysisFinished(SolverConvergence.ReportStopped(DateTime.Now));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsFittingForTesting);

            solver.ReportAnalysisFinished(SolverConvergence.ReportStopped(DateTime.Now));
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.IsFittingForTesting);
            Assert.True(window.CellConcentrationBoxForTesting.IsEffectivelyEnabled);
            Assert.True(window.ParameterPanelForTesting.IsEffectivelyEnabled);
            Assert.True(window.FitButtonForTesting.IsEnabled);
            Assert.False(window.InjectionVolumeBoxForTesting.IsEnabled);
            Assert.False(window.NoiseLevelSliderForTesting.IsEnabled);
        });
    }

    static void Trigger(ExperimentDesignerWindow window, string trigger)
    {
        switch (trigger)
        {
            case "instrument":
                window.InstrumentComboForTesting.SelectedIndex = (window.InstrumentComboForTesting.SelectedIndex + 1) % window.InstrumentComboForTesting.ItemCount;
                break;
            case "cell":
                window.CellConcentrationBoxForTesting.Text = "25";
                window.CellConcentrationBoxForTesting.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                break;
            case "syringe":
                window.SyringeConcentrationBoxForTesting.Text = "250";
                PressEnter(window.SyringeConcentrationBoxForTesting);
                break;
            case "count":
                window.InjectionCountStepperForTesting.Value = 21;
                break;
            case "auto volume":
                window.AutoVolumeCheckForTesting.IsChecked = false;
                break;
            case "manual volume":
                window.AutoVolumeCheckForTesting.IsChecked = false;
                window.InjectionVolumeBoxForTesting.Text = "1.5";
                PressEnter(window.InjectionVolumeBoxForTesting);
                break;
            case "small first injection":
                window.SmallFirstInjectionCheckForTesting.IsChecked = false;
                break;
            case "tandem":
                window.TandemCheckForTesting.IsChecked = true;
                break;
            case "tandem segments":
                window.TandemCheckForTesting.IsChecked = true;
                window.TandemSegmentCountStepperForTesting.Value = 3;
                break;
            case "model switch":
                SelectModel(window, AnalysisModel.Dissociation);
                SelectModel(window, AnalysisModel.OneSetOfSites);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(trigger));
        }
    }

    static void SelectModel(ExperimentDesignerWindow window, AnalysisModel model)
    {
        window.ModelComboForTesting.SelectedIndex = AnalysisModelAttribute.GetAll().IndexOf(model);
        Assert.Equal(model, window.GenerationFactoryForTesting!.ModelType);
    }

    static void PressEnter(TextBox box) =>
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

    static double[] Peaks(SingleModelFactory factory) =>
        factory.Model.Data.Injections.Select(injection => injection.PeakArea.Value).ToArray();

    static double Value(SingleModelFactory factory, ParameterType key) =>
        factory.GetExposedParameters().Single(parameter => parameter.Key == key).Value;

    static void WithWindow(Action<ExperimentDesignerWindow> test)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var window = new ExperimentDesignerWindow();
            try
            {
                test(window);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
