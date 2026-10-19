using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using AnalysisITC.Avalonia.Details;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class DetailsValuePreservationTests : IDisposable
{
    readonly PreferencesState preferences = PreferencesState.FromSettings();
    readonly CultureInfo culture = CultureInfo.CurrentCulture;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public DetailsValuePreservationTests()
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
    [InlineData(AttributeKey.Buffer, false)]
    [InlineData(AttributeKey.Buffer, true)]
    [InlineData(AttributeKey.Salt, false)]
    [InlineData(AttributeKey.Salt, true)]
    [InlineData(AttributeKey.IonicStrength, false)]
    [InlineData(AttributeKey.IonicStrength, true)]
    [InlineData(AttributeKey.PreboundLigandConc, false)]
    [InlineData(AttributeKey.PreboundLigandConc, true)]
    [InlineData(AttributeKey.Percentage, false)]
    [InlineData(AttributeKey.Percentage, true)]
    public void ApplyPreservesUntouchedScientificValuesAndIntervals(AttributeKey key, bool editComment)
    {
        var data = Data();
        var attribute = Attribute(key);
        data.Attributes.Add(attribute.Copy());
        var cell = data.CellConcentration;
        var syringe = data.SyringeConcentration;
        var revision = data.ProcessingRevision;
        var heats = data.Injections.Select(i => i.PeakArea).ToArray();
        var window = new ExperimentDetailsWindow(data);
        if (editComment) Field<TextBox>(window, "commentsBox").Text = "Changed comment";

        Apply(window);

        Assert.True(window.Applied);
        var after = Assert.Single(data.Attributes);
        Assert.Equal(attribute.DoubleValue, after.DoubleValue);
        Same(attribute.ParameterValue, after.ParameterValue);
        Same(cell, data.CellConcentration);
        Same(syringe, data.SyringeConcentration);
        Assert.Equal(25.123456789, data.MeasuredTemperature);
        Assert.Equal(200.123456789e-6, data.CellVolume);
        Assert.Equal(revision, data.ProcessingRevision);
        Assert.Equal(heats, data.Injections.Select(i => i.PeakArea));
        Assert.Equal(editComment ? "Changed comment" : "", data.Comments ?? "");
    }

    [AvaloniaTheory]
    [InlineData(AttributeKey.PreboundLigandConc)]
    [InlineData(AttributeKey.Percentage)]
    public void UntouchedExactDisplayPreservesAsymmetricUncertainty(AttributeKey key)
    {
        var attribute = Attribute(key);
        attribute.ParameterValue = new FloatWithError(12.5e-6, 1.25e-6, 8e-6, 22e-6);
        var before = attribute.ParameterValue;
        var row = new ExperimentAttributeEditorControl(attribute, (_, _) => true);
        Assert.True(row.TryApply(Data(), out var error), error);
        Same(before, attribute.ParameterValue);
    }

    [AvaloniaFact]
    public void CommentOnlyApplyPreservesMainConcentrationUncertainty()
    {
        var data = Data();
        var cell = data.CellConcentration;
        var syringe = data.SyringeConcentration;
        var window = new ExperimentDetailsWindow(data);
        Field<TextBox>(window, "commentsBox").Text = "Metadata only";
        Apply(window);
        Assert.True(window.Applied);
        Same(cell, data.CellConcentration);
        Same(syringe, data.SyringeConcentration);
    }

    [AvaloniaTheory]
    [InlineData("valueBox", "-1")]
    [InlineData("errorBox", "-1")]
    [InlineData("valueBox", "invalid")]
    [InlineData("errorBox", "invalid")]
    public void InvalidCompetitorEditsAreStillRejected(string field, string text)
    {
        var attribute = Attribute(AttributeKey.PreboundLigandConc);
        var before = attribute.ParameterValue;
        var row = new ExperimentAttributeEditorControl(attribute, (_, _) => true);
        Field<TextBox>(row, field).Text = text;
        Assert.False(row.TryApply(Data(), out var error));
        Assert.NotEmpty(error);
        Same(before, attribute.ParameterValue);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditingOneCompetitorComponentPreservesTheOtherExactly(bool editSd)
    {
        var attribute = Attribute(AttributeKey.PreboundLigandConc);
        var before = attribute.ParameterValue;
        var row = new ExperimentAttributeEditorControl(attribute, (_, _) => true);
        Field<TextBox>(row, editSd ? "errorBox" : "valueBox").Text = "31.25";

        Assert.True(row.TryApply(Data(), out var error), error);

        Assert.Equal(editSd ? before.Value : 31.25e-6, attribute.ParameterValue.Value);
        Assert.Equal(editSd ? 31.25e-6 : before.SD, attribute.ParameterValue.SD);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BufferEditsPreserveTheUntouchedComponent(bool editPh)
    {
        var attribute = Attribute(AttributeKey.Buffer);
        var before = attribute.ParameterValue;
        var row = new ExperimentAttributeEditorControl(attribute, (_, _) => true);
        Field<TextBox>(row, editPh ? "doubleBox" : "valueBox").Text = editPh ? "8.15" : "31.25";

        Assert.True(row.TryApply(Data(), out var error), error);

        Assert.Equal(editPh ? 8.15 : 7.456789123, attribute.DoubleValue);
        if (editPh) Same(before, attribute.ParameterValue);
        else
        {
            Assert.Equal(0.03125, attribute.ParameterValue.Value);
            Assert.Equal(before.SD, attribute.ParameterValue.SD);
        }
    }

    [AvaloniaFact]
    public void NewBufferSavesTheDisplayedDefaultPh()
    {
        var attribute = new ExperimentAttribute();
        var row = new ExperimentAttributeEditorControl(attribute, (_, _) => true);
        Select(row, "keyCombo", AttributeKey.Buffer);
        Select(row, "enumCombo", AnalysisITC.Core.Data.Buffer.Tris);
        Assert.Equal("7.4", Field<TextBox>(row, "doubleBox").Text);

        Assert.True(row.TryApply(Data(), out var error), error);

        Assert.Equal(7.4, attribute.DoubleValue);
    }

    [AvaloniaTheory]
    [InlineData("74")]
    [InlineData("-1")]
    [InlineData("14.000001")]
    [InlineData("invalid")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectedBufferPhPreservesDataAndAllowsCorrectedRetry(string text)
    {
        var data = Data();
        var original = Attribute(AttributeKey.Buffer);
        data.Attributes.Add(original);
        var before = original.Copy();
        var revision = data.ProcessingRevision;
        var heats = data.Injections.Select(i => i.PeakArea).ToArray();
        var window = new ExperimentDetailsWindow(data);
        var row = window.GetLogicalDescendants().OfType<ExperimentAttributeEditorControl>().Single();
        var ph = Field<TextBox>(row, "doubleBox");
        Select(row, "enumCombo", AnalysisITC.Core.Data.Buffer.Hepes);
        ph.Text = text;
        Field<TextBox>(row, "valueBox").Text = "50";
        Field<TextBox>(window, "commentsBox").Text = "Pending comment";

        Apply(window);

        Assert.False(window.Applied);
        Assert.Equal(ExperimentAttributeInputPolicy.BufferPhValidationMessage, Field<TextBlock>(window, "statusText").Text);
        Assert.Equal(text, ph.Text);
        Assert.Same(original, Assert.Single(data.Attributes));
        Assert.Equal(before.IntValue, original.IntValue);
        Assert.Equal(before.DoubleValue, original.DoubleValue);
        Same(before.ParameterValue, original.ParameterValue);
        Assert.Equal(before.IntValue, row.Attribute.IntValue);
        Assert.Equal(before.DoubleValue, row.Attribute.DoubleValue);
        Same(before.ParameterValue, row.Attribute.ParameterValue);
        Assert.Equal("", data.Comments ?? "");
        Assert.Equal(revision, data.ProcessingRevision);
        Assert.Equal(heats, data.Injections.Select(i => i.PeakArea));

        ph.Text = "8.15";
        Apply(window);

        Assert.True(window.Applied);
        var saved = Assert.Single(data.Attributes);
        Assert.Equal(8.15, saved.DoubleValue);
        Assert.Equal((int)AnalysisITC.Core.Data.Buffer.Hepes, saved.IntValue);
        Assert.Equal(0.05, saved.ParameterValue.Value);
        Assert.Equal(before.ParameterValue.SD, saved.ParameterValue.SD);
        Assert.Equal("Pending comment", data.Comments);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(14)]
    public void BufferPhBoundsSurviveApplyAndReopening(double pH)
    {
        var data = Data();
        data.Attributes.Add(Attribute(AttributeKey.Buffer));
        var text = pH.ToString(CultureInfo.CurrentCulture);
        var window = new ExperimentDetailsWindow(data);
        var row = window.GetLogicalDescendants().OfType<ExperimentAttributeEditorControl>().Single();
        Field<TextBox>(row, "doubleBox").Text = text;
        Apply(window);
        Assert.True(window.Applied);
        Assert.Equal(pH, Assert.Single(data.Attributes).DoubleValue);

        var reopened = new ExperimentDetailsWindow(data);
        var reopenedRow = reopened.GetLogicalDescendants().OfType<ExperimentAttributeEditorControl>().Single();
        Assert.Equal(text, Field<TextBox>(reopenedRow, "doubleBox").Text);
        var revision = data.ProcessingRevision;
        Apply(reopened);
        Assert.True(reopened.Applied);
        Assert.Equal(pH, Assert.Single(data.Attributes).DoubleValue);
        Assert.Equal(revision, data.ProcessingRevision);
    }

    [AvaloniaFact]
    public void RevertingTextAfterAnApplyAttemptRestoresTheOriginalValueAndUncertainty()
    {
        var attribute = Attribute(AttributeKey.PreboundLigandConc);
        var before = attribute.ParameterValue;
        var row = new ExperimentAttributeEditorControl(attribute, (_, _) => true);
        var field = Field<TextBox>(row, "valueBox");
        var originalText = field.Text;
        field.Text = "31.25";
        Assert.True(row.TryApply(Data(), out var firstError), firstError);
        field.Text = originalText;

        Assert.True(row.TryApply(Data(), out var error), error);
        Same(before, attribute.ParameterValue);
    }

    [AvaloniaFact]
    public void UntouchedMissingParameterIsNotConvertedToANumericZero()
    {
        var attribute = Attribute(AttributeKey.PreboundLigandConc);
        attribute.ParameterValue = FloatWithError.NaN;
        var row = new ExperimentAttributeEditorControl(attribute, (_, _) => true);
        Assert.True(row.TryApply(Data(), out var error), error);
        Assert.True(FloatWithError.IsNaN(attribute.ParameterValue));
    }

    [AvaloniaTheory]
    [InlineData(ExperimentDateSource.Unknown, DateTimeKind.Unspecified, "en-GB")]
    [InlineData(ExperimentDateSource.Unknown, DateTimeKind.Utc, "da-DK")]
    [InlineData(ExperimentDateSource.FileSystem, DateTimeKind.Local, "en-GB")]
    [InlineData(ExperimentDateSource.FileSystem, DateTimeKind.Utc, "da-DK")]
    [InlineData(ExperimentDateSource.UserModified, DateTimeKind.Unspecified, "en-GB")]
    [InlineData(ExperimentDateSource.UserModified, DateTimeKind.Local, "da-DK")]
    [InlineData(ExperimentDateSource.DataFile, DateTimeKind.Utc, "en-GB")]
    public void CommentOnlyApplyPreservesAllDateTicksKindAndProvenance(ExperimentDateSource source, DateTimeKind kind, string locale)
    {
        AppSettings.Locale = locale;
        var data = Data();
        data.Date = new DateTime(2026, 10, 16, 13, 24, 56, kind).AddTicks(1234567);
        data.DateSource = source;
        var original = data.Date;
        var window = new ExperimentDetailsWindow(data);
        Field<TextBox>(window, "commentsBox").Text = "Date untouched";

        Apply(window);

        Assert.True(window.Applied);
        Assert.Equal(original.Ticks, data.Date.Ticks);
        Assert.Equal(kind, data.Date.Kind);
        Assert.Equal(source, data.DateSource);
    }

    [AvaloniaTheory]
    [InlineData(ExperimentDateSource.Unknown)]
    [InlineData(ExperimentDateSource.FileSystem)]
    [InlineData(ExperimentDateSource.UserModified)]
    [InlineData(ExperimentDateSource.DataFile)]
    public void ExplicitDateEditsUpdateOnlyEditableDates(ExperimentDateSource source)
    {
        AppSettings.Locale = "en-GB";
        var data = Data();
        data.Date = new DateTime(2026, 10, 16, 13, 24, 56, DateTimeKind.Utc).AddTicks(1234567);
        data.DateSource = source;
        var original = data.Date;
        var window = new ExperimentDetailsWindow(data);
        Field<TextBox>(window, "dateBox").Text = "23/11/2026 17:42:13";

        Apply(window);

        Assert.True(window.Applied);
        Assert.Equal(source == ExperimentDateSource.DataFile ? original : new DateTime(2026, 11, 23, 17, 42, 13), data.Date);
        Assert.Equal(source == ExperimentDateSource.DataFile ? source : ExperimentDateSource.UserModified, data.DateSource);
    }

    static ExperimentAttribute Attribute(AttributeKey key)
    {
        var attribute = ExperimentAttribute.Parameter(key, key.ToString(),
            new FloatWithError(12.345678901e-6, 1.2345678901e-6, 8.765432109e-6, 19.876543219e-6));
        attribute.IntValue = key == AttributeKey.Buffer ? (int)AnalysisITC.Core.Data.Buffer.Tris : (int)Salt.NaCl;
        attribute.DoubleValue = 7.456789123;
        return attribute;
    }

    static ExperimentData Data()
    {
        var data = new ExperimentData("Details regression")
        {
            CellVolume = 200.123456789e-6,
            CellConcentration = new FloatWithError(20.123456789e-6, 1.23456789e-6, 18e-6, 25e-6),
            SyringeConcentration = new FloatWithError(400.123456789e-6, 12.3456789e-6, 380e-6, 450e-6),
            MeasuredTemperature = 25.123456789,
            DateSource = ExperimentDateSource.DataFile,
        };
        for (var i = 0; i < 4; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, 8e-10, true);
            injection.SetPeakArea(new FloatWithError(-1e-5 * (i + 1), 1e-9));
            data.Injections.Add(injection);
        }
        RawDataReader.ProcessInjections(data, DilutionMethod.MicroCal);
        return data;
    }

    static void Same(FloatWithError expected, FloatWithError actual)
    {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.SD, actual.SD);
        Assert.Equal(expected.Lower, actual.Lower);
        Assert.Equal(expected.Upper, actual.Upper);
        Assert.Equal(expected.IsAsymmetric, actual.IsAsymmetric);
        Assert.Equal(FloatWithError.IsNaN(expected), FloatWithError.IsNaN(actual));
    }

    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    static void Select<T>(ExperimentAttributeEditorControl row, string name, T value)
    {
        var combo = Field<ComboBox>(row, name);
        combo.SelectedItem = combo.Items.Single(item => Equals(item!.GetType().GetProperty("Value")!.GetValue(item), value));
    }
    static void Apply(ExperimentDetailsWindow window) => typeof(ExperimentDetailsWindow).GetMethod("Apply", Private)!.Invoke(window, null);
}
