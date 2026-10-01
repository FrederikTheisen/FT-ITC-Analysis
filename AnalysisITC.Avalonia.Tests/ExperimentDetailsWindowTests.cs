using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
    [InlineData("Ideal continuous mixing", InjectionHeatMethod.IdealContinuousMixing)]
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
    public void OptionalIdentifiersApplyAsMetadataAndDoNotChangeProcessing() => Run(() =>
    {
        var data = Data("identified");
        var revision = data.ProcessingRevision;
        var window = new ExperimentDetailsWindow(data);
        Field<TextBox>(window, "externalExperimentIdBox").Text = "  Lab-Ω / 00042  ";
        Field<TextBox>(window, "cellSampleIdBox").Text = " cell-batch-007 ";
        Field<TextBox>(window, "syringeSampleIdBox").Text = " syringe-batch-003 ";

        Assert.Equal("", data.ExternalExperimentId);
        Apply(window);

        Assert.True(window.Applied);
        Assert.Equal("Lab-Ω / 00042", data.ExternalExperimentId);
        Assert.Equal("cell-batch-007", data.CellSampleId);
        Assert.Equal("syringe-batch-003", data.SyringeSampleId);
        Assert.Equal(revision, data.ProcessingRevision);
    });

    [Fact]
    public void UnappliedIdentifierEditsAreDiscardedWhenDetailsCloses() => Run(() =>
    {
        var data = Data("cancelled identifiers");
        data.ExternalExperimentId = "saved-experiment";
        data.CellSampleId = "saved-cell";
        data.SyringeSampleId = "saved-syringe";
        var window = new ExperimentDetailsWindow(data);
        Field<TextBox>(window, "externalExperimentIdBox").Text = "pending-experiment";
        Field<TextBox>(window, "cellSampleIdBox").Text = "pending-cell";
        Field<TextBox>(window, "syringeSampleIdBox").Text = "pending-syringe";

        window.Close();

        Assert.Equal("saved-experiment", data.ExternalExperimentId);
        Assert.Equal("saved-cell", data.CellSampleId);
        Assert.Equal("saved-syringe", data.SyringeSampleId);
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
        Assert.Equal(InjectionBookkeeping.Description(null), Field<TextBlock>(window, "bookkeepingDescription").Text);
        Field<TextBox>(window, "nameBox").Text = "Renamed";
        Apply(window);
        Assert.True(window.Applied);
        Assert.Equal("Renamed", data.Name);
        Assert.Null(data.AppliedDilutionMethod);
        Assert.Equal(InjectionHeatMethod.MicroCal, data.HeatMethod);
        Assert.Equal(cell, data.CellConcentration.Value);
        Assert.Equal(heats, data.Injections.Select(i => i.PeakArea.Value));
    });

    [Fact]
    public void BookkeepingDescriptionTracksSelectedMethod() => Run(() =>
    {
        var data = Data("sample");
        var window = new ExperimentDetailsWindow(data);
        var combo = Field<ComboBox>(window, "bookkeepingCombo");
        var description = Field<TextBlock>(window, "bookkeepingDescription");

        Assert.Equal(InjectionBookkeeping.Description(DilutionMethod.MicroCal), description.Text);
        combo.SelectedItem = "Discrete displacement";
        Assert.Equal(InjectionBookkeeping.Description(DilutionMethod.DiscreteDisplacement), description.Text);
    });

    [Fact]
    public void TrustedExperimentDateIsReadOnlyAndFilesystemDateCanBeProvided() => Run(() =>
    {
        var trusted = Data("trusted");
        trusted.Date = new DateTime(2026, 9, 1, 8, 0, 0);
        trusted.DateSource = ExperimentDateSource.DataFile;
        var trustedWindow = new ExperimentDetailsWindow(trusted);
        var trustedDate = Field<TextBox>(trustedWindow, "dateBox");
        Assert.False(trustedDate.IsEnabled);
        Assert.Contains(trustedWindow.GetLogicalDescendants().OfType<TextBlock>(), text =>
            text.Text?.Contains("Data file: date and time are trusted", StringComparison.Ordinal) == true);
        trustedDate.Text = "2026-09-15 10:30";
        Apply(trustedWindow);
        Assert.Equal(new DateTime(2026, 9, 1, 8, 0, 0), trusted.Date);
        Assert.Equal(ExperimentDateSource.DataFile, trusted.DateSource);

        var placeholder = Data("placeholder");
        placeholder.Date = new DateTime(2026, 9, 1, 8, 0, 0);
        placeholder.DateSource = ExperimentDateSource.FileSystem;
        var placeholderWindow = new ExperimentDetailsWindow(placeholder);
        var placeholderDate = Field<TextBox>(placeholderWindow, "dateBox");
        Assert.True(placeholderDate.IsEnabled);
        placeholderDate.Text = "2026-09-15 10:30";
        Apply(placeholderWindow);
        Assert.True(placeholderWindow.Applied);
        Assert.Equal(new DateTime(2026, 9, 15, 10, 30, 0), placeholder.Date);
        Assert.Equal(ExperimentDateSource.UserModified, placeholder.DateSource);
    });

    [Fact]
    public void UnknownConcentrationEditsRequireExplicitModeAndTandemIsReadOnly() => Run(() =>
    {
        var data = Data("saved", processed: false);
        var window = new ExperimentDetailsWindow(data);
        Field<TextBox>(window, "cellBox").Text = "30";
        Apply(window);
        Assert.False(window.Applied);
        Assert.Contains("Select MicroCal, Ideal continuous mixing, or Discrete displacement", Field<TextBlock>(window, "statusText").Text);
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
        Assert.Equal(InjectionHeatMethod.MicroCal, data.HeatMethod);
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
