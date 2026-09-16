using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Details;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ExperimentDetailsWindowTests
{
    public ExperimentDetailsWindowTests() => AvaloniaTestBootstrap.EnsureInitialized();

    static void Run(Action test)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var original = PreferencesState.FromSettings();
            PreferencesState.Defaults().ApplyToSettings();
            DataManager.Clear(DataClearMode.ResetSession);
            try { test(); }
            finally
            {
                DataManager.Clear(DataClearMode.ResetSession);
                original.ApplyToSettings();
            }
        });
    }

    [Theory]
    [InlineData("Dumas", InjectionHeatMethod.DumasSimpson)]
    [InlineData("Discrete displacement", InjectionHeatMethod.DiscreteDisplacement)]
    public void ModeOnlyChangePreservesMeasuredHeatsAndBufferSubtraction(string label, InjectionHeatMethod method) => Run(() =>
    {
        var data = Data("sample");
        var blank = Data("blank");
        DataManager.AddData(data); DataManager.AddData(blank);
        data.SetBufferSubtraction(blank, BufferSubtractionMethod.MatchedInjection);
        var raw = data.Injections.Select(i => i.RawPeakArea.Value).ToArray();
        var corrected = data.Injections.Select(i => i.PeakArea.Value).ToArray();
        var processor = data.Processor;
        var window = new ExperimentDetailsWindow(data);
        Field<ComboBox>(window, "bookkeepingCombo").SelectedItem = label;
        Apply(window);
        Assert.True(window.Applied);
        Assert.Equal(method, data.HeatMethod);
        Assert.Same(processor, data.Processor);
        Assert.Equal(blank.UniqueID, data.BufferSubtractionSettings.ReferenceExperimentId);
        Assert.Equal(raw, data.Injections.Select(i => i.RawPeakArea.Value));
        Assert.Equal(corrected, data.Injections.Select(i => i.PeakArea.Value));
    });

    [Fact]
    public void NameOnlyEditPreservesUnknownSavedProcessingAndFullPrecision() => Run(() =>
    {
        var data = Data("saved", processed: false);
        var cell = data.CellConcentration.Value;
        var heats = data.Injections.Select(i => i.PeakArea.Value).ToArray();
        AppSettings.DilutionCalculationMethod = DilutionMethod.Exponential;
        var window = new ExperimentDetailsWindow(data);
        Assert.Equal(InjectionBookkeeping.SavedProcessingLabel, Field<ComboBox>(window, "bookkeepingCombo").SelectedItem);
        Field<TextBox>(window, "nameBox").Text = "Renamed";
        Apply(window);
        Assert.True(window.Applied);
        Assert.Equal("Renamed", data.Name);
        Assert.Null(data.AppliedDilutionMethod);
        Assert.Equal(InjectionHeatMethod.Legacy, data.HeatMethod);
        Assert.Equal(cell, data.CellConcentration.Value);
        Assert.Equal(heats, data.Injections.Select(i => i.PeakArea.Value));
    });

    [Fact]
    public void UnknownConcentrationEditsRequireExplicitModeAndTandemIsReadOnly() => Run(() =>
    {
        var data = Data("saved", processed: false);
        var window = new ExperimentDetailsWindow(data);
        Field<TextBox>(window, "cellBox").Text = "30";
        Apply(window);
        Assert.False(window.Applied);
        Assert.Contains("Select MicroCal, Dumas or Discrete displacement", Field<TextBlock>(window, "statusText").Text);
        window.Close();
        data.AddSegment(new TandemExperimentSegment(0, 20e-6, 0));
        var tandem = new ExperimentDetailsWindow(data);
        Assert.False(Field<ComboBox>(tandem, "bookkeepingCombo").IsEnabled);
    });

    [Fact]
    public void InvalidPytcVolumeEditLeavesMetadataAndProcessingUntouched() => Run(() =>
    {
        var data = Data("sample");
        var cells = data.Injections.Select(i => i.ActualCellConcentration).ToArray();
        var window = new ExperimentDetailsWindow(data);
        Field<ComboBox>(window, "bookkeepingCombo").SelectedItem = "Discrete displacement";
        Field<TextBox>(window, "cellVolumeBox").Text = "1";
        Apply(window);
        Assert.False(window.Applied);
        Assert.Equal(200e-6, data.CellVolume);
        Assert.Equal(InjectionHeatMethod.Legacy, data.HeatMethod);
        Assert.Equal(cells, data.Injections.Select(i => i.ActualCellConcentration));
        window.Close();
    });

    static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    static void Apply(ExperimentDetailsWindow window) => typeof(ExperimentDetailsWindow)
        .GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    static ExperimentData Data(string name, bool processed = true)
    {
        var data = new ExperimentData(name)
        {
            CellVolume = 200e-6, CellConcentration = new(20.123456789e-6),
            SyringeConcentration = new(400e-6), MeasuredTemperature = 25,
        };
        for (var i = 0; i < 4; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, 8e-10, true);
            injection.SetPeakArea(new FloatWithError(-1e-5 * (i + 1), 1e-9));
            data.Injections.Add(injection);
        }
        if (processed) RawDataReader.ProcessInjections(data, DilutionMethod.MicroCal);
        return data;
    }
}
