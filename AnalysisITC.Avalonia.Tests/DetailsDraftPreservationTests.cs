using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AnalysisITC.Avalonia.Details;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;
using BufferKind = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class DetailsDraftPreservationTests : IDisposable
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly PreferencesState preferences = PreferencesState.FromSettings();
    readonly CultureInfo culture = CultureInfo.CurrentCulture;

    public DetailsDraftPreservationTests()
    {
        PreferencesState.Defaults().ApplyToSettings();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        DataManager.Clear(DataClearMode.ResetSession);
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        preferences.ApplyToSettings();
        CultureInfo.CurrentCulture = culture;
    }

    [AvaloniaTheory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("change-key")]
    public void ListChangesPreserveDraftsAndApplyTheirValues(string action)
    {
        var data = Data();
        var window = new ExperimentDetailsWindow(data);
        var buffer = Row(window, AttributeKey.Buffer);
        var salt = Row(window, AttributeKey.Salt);
        var species = Row(window, AttributeKey.Species);
        var competitor = Row(window, AttributeKey.PreboundLigandConc);
        var flag = Row(window, AttributeKey.PeptideInCell);
        Edit(window);

        ChangeList(window, action);

        Assert.Same(buffer, Row(window, AttributeKey.Buffer));
        Assert.Same(salt, Row(window, AttributeKey.Salt));
        Assert.Same(species, Row(window, AttributeKey.Species));
        Assert.Same(competitor, Row(window, AttributeKey.PreboundLigandConc));
        Assert.Same(flag, Row(window, AttributeKey.PeptideInCell));
        Assert.Equal("50", Field<TextBox>(buffer, "valueBox").Text);
        Assert.Equal("8.15", Field<TextBox>(buffer, "doubleBox").Text);
        Assert.Equal(BufferKind.Hepes, Selected<BufferKind>(buffer, "enumCombo"));
        Assert.Equal(Salt.KCl, Selected<Salt>(salt, "enumCombo"));
        Assert.Equal("175", Field<TextBox>(salt, "valueBox").Text);
        AssertOtherDrafts(window);
        AssertOriginalData(data);

        Apply(window);

        Assert.True(window.Applied);
        var savedBuffer = Stored(data, AttributeKey.Buffer);
        Assert.Equal(0.05, savedBuffer.ParameterValue.Value);
        Assert.Equal(8.15, savedBuffer.DoubleValue);
        Assert.Equal((int)BufferKind.Hepes, savedBuffer.IntValue);
        Assert.Equal(0.175, Stored(data, AttributeKey.Salt).ParameterValue.Value);
        Assert.Equal((int)Salt.KCl, Stored(data, AttributeKey.Salt).IntValue);
        AssertOtherSavedValues(data);
    }

    [AvaloniaTheory]
    [InlineData(BufferKind.PBS)]
    [InlineData(BufferKind.TBS)]
    public void PresetsReplaceOnlyBufferAndSaltDrafts(BufferKind preset)
    {
        var data = Data();
        var window = new ExperimentDetailsWindow(data);
        var species = Row(window, AttributeKey.Species);
        var oldBuffer = Row(window, AttributeKey.Buffer);
        var oldSalt = Row(window, AttributeKey.Salt);
        var ionic = Row(window, AttributeKey.IonicStrength);
        var ionicValue = ionic.Attribute.ParameterValue;
        Edit(window);

        Select(oldBuffer, "enumCombo", preset);

        Assert.Same(species, Row(window, AttributeKey.Species));
        Assert.Same(ionic, Row(window, AttributeKey.IonicStrength));
        Assert.DoesNotContain(oldBuffer, Rows(window));
        Assert.DoesNotContain(oldSalt, Rows(window));
        AssertOtherDrafts(window);
        AssertOriginalData(data);
        Apply(window);

        Assert.True(window.Applied);
        AssertOtherSavedValues(data);
        Assert.Equal(ionicValue, Stored(data, AttributeKey.IonicStrength).ParameterValue);
        AssertPreset(data, preset);
    }

    [AvaloniaTheory]
    [InlineData("add", "-")]
    [InlineData("remove", "invalid")]
    [InlineData("change-key", "-")]
    [InlineData("PBS", "invalid")]
    [InlineData("TBS", "-")]
    public void StructuralChangesKeepInvalidInputUntilApply(string action, string text)
    {
        var data = Data();
        var window = new ExperimentDetailsWindow(data);
        var competitor = Row(window, AttributeKey.PreboundLigandConc);
        Field<TextBox>(competitor, "valueBox").Text = text;
        if (action is "PBS" or "TBS")
            Select(Row(window, AttributeKey.Buffer), "enumCombo", Enum.Parse<BufferKind>(action));
        else ChangeList(window, action);

        Assert.Equal(text, Field<TextBox>(Row(window, AttributeKey.PreboundLigandConc), "valueBox").Text);
        Apply(window);
        Assert.False(window.Applied);
        Assert.NotEmpty(Field<TextBlock>(window, "statusText").Text!);
        AssertOriginalData(data);
        window.Close();
    }

    [AvaloniaFact]
    public void KeyMenusTrackAvailabilityWithoutResettingSelectedKeysOrDrafts()
    {
        var window = new ExperimentDetailsWindow(Data());
        var buffer = Row(window, AttributeKey.Buffer);
        var blank = Row(window, AttributeKey.Null);
        var events = 0;
        buffer.KeyChanged += (_, _) => events++;
        Field<TextBox>(buffer, "valueBox").Text = "50";
        Assert.DoesNotContain(AttributeKey.PreboundLigandConc, Keys(blank));
        Assert.DoesNotContain(AttributeKey.IonicStrength, Keys(blank));
        Assert.Contains(AttributeKey.Buffer, Keys(blank));
        Assert.Contains(AttributeKey.Salt, Keys(blank));
        Remove(Row(window, AttributeKey.PreboundLigandConc));
        Assert.Contains(AttributeKey.PreboundLigandConc, Keys(blank));
        Select(blank, "keyCombo", AttributeKey.PreboundLigandConc);
        Add(window);
        var newBlank = Row(window, AttributeKey.Null);
        Assert.DoesNotContain(AttributeKey.PreboundLigandConc, Keys(newBlank));
        Select(blank, "keyCombo", AttributeKey.Null);
        Assert.Contains(AttributeKey.PreboundLigandConc, Keys(newBlank));
        Assert.Equal(0, events);
        Assert.Equal(AttributeKey.Buffer, Selected<AttributeKey>(buffer, "keyCombo"));
        Assert.Equal("50", Field<TextBox>(buffer, "valueBox").Text);
        Assert.Equal(AttributeKey.Null, Selected<AttributeKey>(newBlank, "keyCombo"));
        window.Close();
    }

    [AvaloniaFact]
    public void ExplicitReplacementAndRemovalDiscardOnlyTheirOwnDraftsAndCancelDoesNotApply()
    {
        var data = Data();
        var window = new ExperimentDetailsWindow(data);
        Edit(window);
        var competitor = Row(window, AttributeKey.PreboundLigandConc);
        Select(competitor, "keyCombo", AttributeKey.Species);
        Assert.Equal("", Field<TextBox>(competitor, "stringBox").Text);
        Remove(Row(window, AttributeKey.Buffer));
        Assert.DoesNotContain(Rows(window), row => row.Attribute.Key == AttributeKey.Buffer);
        Assert.Equal("Edited species", Field<TextBox>(Row(window, AttributeKey.Species), "stringBox").Text);
        Select(competitor, "keyCombo", AttributeKey.PreboundLigandConc);
        Assert.NotEqual("31.25", Field<TextBox>(competitor, "valueBox").Text);
        window.Close();
        AssertOriginalData(data);
        var reopened = new ExperimentDetailsWindow(data);
        Assert.Equal("20", Field<TextBox>(Row(reopened, AttributeKey.Buffer), "valueBox").Text);
        reopened.Close();
    }

    [AvaloniaFact]
    public void EmptyListAndRepeatedChangesKeepOrderAndUntouchedPrecision()
    {
        var data = Data();
        var exact = Stored(data, AttributeKey.PreboundLigandConc).ParameterValue;
        var window = new ExperimentDetailsWindow(data);
        Add(window); Add(window);
        Remove(Row(window, AttributeKey.Null));
        Assert.Equal(Field<System.Collections.Generic.List<ExperimentAttribute>>(window, "attributes"),
            Rows(window).Select(row => row.Attribute));
        Apply(window);
        Assert.True(window.Applied);
        Assert.Equal(exact, Stored(data, AttributeKey.PreboundLigandConc).ParameterValue);

        var empty = new ExperimentDetailsWindow(new ExperimentData("Empty"));
        Assert.Single(Field<StackPanel>(empty, "attributePanel").Children.OfType<TextBlock>());
        Add(empty);
        Remove(Row(empty, AttributeKey.Null));
        Assert.Empty(Rows(empty));
        Assert.Single(Field<StackPanel>(empty, "attributePanel").Children.OfType<TextBlock>());
        Add(empty);
        Assert.Single(Rows(empty));
        Assert.Empty(Field<StackPanel>(empty, "attributePanel").Children.OfType<TextBlock>());
        empty.Close();
    }

    static void Edit(ExperimentDetailsWindow window)
    {
        var buffer = Row(window, AttributeKey.Buffer);
        Field<TextBox>(buffer, "valueBox").Text = "50";
        Field<TextBox>(buffer, "doubleBox").Text = "8.15";
        Select(buffer, "enumCombo", BufferKind.Hepes);
        var salt = Row(window, AttributeKey.Salt);
        Select(salt, "enumCombo", Salt.KCl);
        Field<TextBox>(salt, "valueBox").Text = "175";
        var species = Row(window, AttributeKey.Species);
        Field<TextBox>(species, "stringBox").Text = "Edited species";
        Select(species, "enumCombo", (int)ExperimentSpeciesLocation.Syringe);
        var competitor = Row(window, AttributeKey.PreboundLigandConc);
        Field<TextBox>(competitor, "valueBox").Text = "31.25";
        Field<TextBox>(competitor, "errorBox").Text = "2.5";
        Field<CheckBox>(Row(window, AttributeKey.PeptideInCell), "boolBox").IsChecked = true;
    }

    static void AssertOtherDrafts(ExperimentDetailsWindow window)
    {
        var species = Row(window, AttributeKey.Species);
        Assert.Equal("Edited species", Field<TextBox>(species, "stringBox").Text);
        Assert.Equal((int)ExperimentSpeciesLocation.Syringe, Selected<int>(species, "enumCombo"));
        Assert.Equal("31.25", Field<TextBox>(Row(window, AttributeKey.PreboundLigandConc), "valueBox").Text);
        Assert.Equal("2.5", Field<TextBox>(Row(window, AttributeKey.PreboundLigandConc), "errorBox").Text);
        Assert.True(Field<CheckBox>(Row(window, AttributeKey.PeptideInCell), "boolBox").IsChecked);
    }

    static void AssertOtherSavedValues(ExperimentData data)
    {
        Assert.Equal("Edited species", Stored(data, AttributeKey.Species).StringValue);
        Assert.Equal((int)ExperimentSpeciesLocation.Syringe, Stored(data, AttributeKey.Species).IntValue);
        Assert.Equal(31.25e-6, Stored(data, AttributeKey.PreboundLigandConc).ParameterValue.Value);
        Assert.Equal(2.5e-6, Stored(data, AttributeKey.PreboundLigandConc).ParameterValue.SD);
        Assert.True(Stored(data, AttributeKey.PeptideInCell).BoolValue);
    }

    static void AssertOriginalData(ExperimentData data)
    {
        Assert.Equal(0.02, Stored(data, AttributeKey.Buffer).ParameterValue.Value);
        Assert.Equal(7.4, Stored(data, AttributeKey.Buffer).DoubleValue);
        Assert.Equal((int)BufferKind.Tris, Stored(data, AttributeKey.Buffer).IntValue);
        Assert.Equal("Original species", Stored(data, AttributeKey.Species).StringValue);
        Assert.False(Stored(data, AttributeKey.PeptideInCell).BoolValue);
        Assert.Equal(12.345678901e-6, Stored(data, AttributeKey.PreboundLigandConc).ParameterValue.Value);
    }

    static void AssertPreset(ExperimentData data, BufferKind preset)
    {
        var buffers = data.Attributes.Where(a => a.Key == AttributeKey.Buffer).ToArray();
        Assert.Equal(preset == BufferKind.PBS ? new[] { (int)BufferKind.SodiumPhosphate, (int)BufferKind.PotassiumPhosphate } : new[] { (int)BufferKind.Tris }, buffers.Select(a => a.IntValue));
        Assert.Equal(preset == BufferKind.PBS ? new[] { 0.01, 0.0018 } : new[] { 0.025 }, buffers.Select(a => a.ParameterValue.Value));
        Assert.All(buffers, b => Assert.Equal(7.4, b.DoubleValue));
        var salts = data.Attributes.Where(a => a.Key == AttributeKey.Salt).ToArray();
        Assert.Equal(new[] { (int)Salt.NaCl, (int)Salt.KCl }, salts.Select(a => a.IntValue));
        Assert.Equal(new[] { 0.137, 0.0027 }, salts.Select(a => a.ParameterValue.Value));
    }

    static ExperimentData Data()
    {
        var data = new ExperimentData("Draft regression") { CellVolume = 200e-6,
            CellConcentration = new FloatWithError(20e-6), SyringeConcentration = new FloatWithError(400e-6),
            MeasuredTemperature = 25, DateSource = ExperimentDateSource.DataFile };
        foreach (var key in new[] { AttributeKey.Buffer, AttributeKey.Salt, AttributeKey.Species,
            AttributeKey.PreboundLigandConc, AttributeKey.PeptideInCell, AttributeKey.IonicStrength, AttributeKey.Null })
            data.Attributes.Add(ExperimentAttribute.FromKey(key));
        Stored(data, AttributeKey.Buffer).IntValue = (int)BufferKind.Tris;
        Stored(data, AttributeKey.Buffer).DoubleValue = 7.4;
        Stored(data, AttributeKey.Buffer).ParameterValue = new FloatWithError(0.02);
        Stored(data, AttributeKey.Salt).IntValue = (int)Salt.NaCl;
        Stored(data, AttributeKey.Salt).ParameterValue = new FloatWithError(0.15);
        Stored(data, AttributeKey.Species).StringValue = "Original species";
        Stored(data, AttributeKey.PreboundLigandConc).ParameterValue = new FloatWithError(12.345678901e-6, 1.23456789e-6, 8e-6, 20e-6);
        Stored(data, AttributeKey.IonicStrength).ParameterValue = new FloatWithError(0.123456789, 0.012345678, 0.1, 0.15);
        RawDataReader.ProcessInjections(data, DilutionMethod.MicroCal);
        return data;
    }

    static void ChangeList(ExperimentDetailsWindow window, string action)
    {
        if (action == "add") Add(window);
        else if (action == "remove") Remove(Row(window, AttributeKey.IonicStrength));
        else Select(Row(window, AttributeKey.IonicStrength), "keyCombo", AttributeKey.Null);
    }

    static void Add(ExperimentDetailsWindow window) => Click(window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add Attribute")));
    static void Remove(ExperimentAttributeEditorControl row) => Click(row.GetLogicalDescendants().OfType<Button>().Single());
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Apply(ExperimentDetailsWindow window) => Click(window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Apply")));
    static ExperimentAttribute Stored(ExperimentData data, AttributeKey key) => data.Attributes.First(a => a.Key == key);
    static ExperimentAttributeEditorControl[] Rows(ExperimentDetailsWindow window) => Field<StackPanel>(window, "attributePanel").Children.OfType<ExperimentAttributeEditorControl>().ToArray();
    static ExperimentAttributeEditorControl Row(ExperimentDetailsWindow window, AttributeKey key) => Rows(window).First(r => r.Attribute.Key == key);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    static object Value(object choice) => choice.GetType().GetProperty("Value")!.GetValue(choice)!;
    static AttributeKey[] Keys(ExperimentAttributeEditorControl row) => ((IEnumerable)Field<ComboBox>(row, "keyCombo").ItemsSource!).Cast<object>().Select(c => (AttributeKey)Value(c)).ToArray();
    static T Selected<T>(ExperimentAttributeEditorControl row, string name) => (T)Value(Field<ComboBox>(row, name).SelectedItem!);
    static void Select(ExperimentAttributeEditorControl row, string name, object value)
    {
        var combo = Field<ComboBox>(row, name);
        combo.SelectedItem = ((IEnumerable)combo.ItemsSource!).Cast<object>().Single(c => Equals(Value(c), value));
    }
}
