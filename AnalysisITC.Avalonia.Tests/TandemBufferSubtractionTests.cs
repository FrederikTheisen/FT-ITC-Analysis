using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Details;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class TandemBufferSubtractionTests : IDisposable
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public TandemBufferSubtractionTests() => DataManager.Clear(DataClearMode.ResetSession);
    public void Dispose() => DataManager.Clear(DataClearMode.ResetSession);

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CorrectedSourcesDisableCreationInEveryModeAndExplainHowToProceed(int mode)
    {
        var first = Experiment("first");
        var second = Experiment("second");
        foreach (var source in new[] { first, second })
        {
            source.BaseLineCorrectedDataPoints = source.DataPoints.ToList();
            source.Processor.BaselineCompleted = true;
        }
        DataManager.AddData(new[] { first, second });
        Correct(second);
        var window = new TandemMergerWindow();
        try
        {
            Assert.Equal(2, window.ExperimentListForTesting.SelectedItems?.Count);
            window.ModeComboForTesting.SelectedIndex = mode;
            Assert.False(window.CreateButtonForTesting.IsEnabled);
            Assert.Equal(BufferSubtractionPolicy.CorrectedSourceReason, ToolTip.GetTip(window.CreateButtonForTesting));
            Assert.Equal(BufferSubtractionPolicy.CorrectedSourceReason, Field<TextBlock>(window, "statusText").Text);

            using (var lease = ProcessingActivity.TryBegin()!)
            {
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.CreateButtonForTesting.IsEnabled);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.CreateButtonForTesting.IsEnabled);
            Assert.Equal(BufferSubtractionPolicy.CorrectedSourceReason, ToolTip.GetTip(window.CreateButtonForTesting));

            second.ClearBufferSubtraction(notify: false);
            Invoke(window, "RefreshControls");
            Assert.True(window.CreateButtonForTesting.IsEnabled);
            Assert.NotEqual(BufferSubtractionPolicy.CorrectedSourceReason, ToolTip.GetTip(window.CreateButtonForTesting));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SubtractionToolExcludesTandemTargets()
    {
        var reference = Experiment("blank");
        var ordinary = Experiment("ordinary");
        var tandem = Experiment("tandem");
        tandem.AddSegment(new TandemExperimentSegment(0, tandem.CellConcentration, 0));
        DataManager.AddData(new[] { reference, ordinary, tandem });
        var window = new BufferSubtractionWindow();
        try
        {
            var targets = Field<ListBox>(window, "targetList");
            Assert.Contains(ordinary, targets.Items.Cast<ExperimentData>());
            Assert.DoesNotContain(tandem, targets.Items.Cast<ExperimentData>());
            Assert.Equal(BufferSubtractionPolicy.TandemTargetReason, ToolTip.GetTip(targets));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SubtractionToolAndDetailsOfferMeanButRejectLegacySnapshotsWithoutVolumes()
    {
        var reference = Experiment("blank", 2e-6);
        var secondReferenceInjection = InjectionData.FromPEAQFile(reference, 1, true, 0, 3e-6, 10, 1, 25);
        secondReferenceInjection.SetPeakArea(new FloatWithError(15e-6));
        reference.Injections.Add(secondReferenceInjection);
        var target = Experiment("target", 5e-6);
        var matching = Experiment("matching", 2e-6);
        DataManager.AddData(new[] { reference, target, matching });
        var window = new BufferSubtractionWindow();
        try
        {
            var methods = Field<ComboBox>(window, "methodCombo");
            Assert.Contains(methods.Items.Cast<object>(), item => item.ToString() == "Mean");
            Assert.Equal((int)AppSettings.BufferSubtractionDefaultMethod, methods.SelectedIndex);
            methods.SelectedIndex = (int)BufferSubtractionMethod.Mean;
            var preview = Field<BufferSubtractionGraphControl>(window, "graph");
            var previewModel = (BufferSubtractionModel?)typeof(BufferSubtractionGraphControl)
                .GetField("model", Private)!.GetValue(preview);
            Assert.NotNull(previewModel);
            var previewPoints = previewModel!.GetLinePoints(1, 2);
            Assert.Equal(new[] { 1d, 2d }, previewPoints.Select(point => point.InjectionNumber));
            Assert.InRange(Math.Abs(previewPoints[0].Heat - 10e-6), 0, 1e-18);
            Assert.InRange(Math.Abs(previewPoints[1].Heat - 15e-6), 0, 1e-18);
            Assert.True(BufferSubtractionCalculator.TryGetReferenceHeat(target.Injections[0], previewModel, out var targetReferenceHeat));
            Assert.Equal(25e-6, targetReferenceHeat.Value, 12);
            var targets = Field<ListBox>(window, "targetList");
            var warning = Field<TextBlock>(window, "volumeWarning");
            Assert.False(warning.IsVisible);
            targets.SelectedIndex = targets.Items.Cast<ExperimentData>().ToList().IndexOf(target);
            Assert.True(warning.IsVisible);
            Assert.Equal("Injection volumes differ from the reference.", warning.Text);
            targets.SelectedItems!.Add(matching);
            Assert.Equal("Injection volumes differ from the reference for 1 of 2 selected targets.", warning.Text);
            targets.SelectedItems.Remove(target);
            Assert.False(warning.IsVisible);
            targets.SelectedItems.Add(target);
            Assert.True(warning.IsVisible);
            Assert.True(Field<Button>(window, "applyButton").IsEnabled);
            Invoke(window, "Apply");
            Assert.True(window.Applied);
            Assert.Equal(BufferSubtractionMethod.Mean, target.BufferSubtractionSettings!.Method);
            Assert.Equal(reference.UniqueID, target.BufferSubtractionSettings.ReferenceExperimentId);

            var settings = new BufferSubtractionSettings("missing", BufferSubtractionMethod.MatchedInjection,
                new BufferSubtractionReferenceSnapshot("blank", new[] { new BufferSubtractionReferencePoint(1, new FloatWithError(2e-6)) }));
            target.AddOrUpdateAttribute(settings.ToAttribute(), notify: false);
            var editor = new ExperimentAttributeEditorControl(target.Attributes.Single().Copy(), (_, _) => true, target);
            var detailMethods = Field<ComboBox>(editor, "methodCombo");
            Assert.Contains(detailMethods.Items.Cast<object>(), item => item.ToString() == "Mean");
            detailMethods.SelectedIndex = detailMethods.Items.Cast<object>()
                .Select((item, index) => (item, index)).Single(pair => pair.item.ToString() == "Mean").index;
            Assert.False(editor.TryApply(target, out var error));
            Assert.Equal(BufferSubtractionPolicy.MeanRequiresVolumesReason, error);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DetailsCannotAddSubtractionToTandemButOrdinaryExperimentsCan()
    {
        var tandem = Experiment("tandem");
        tandem.AddSegment(new TandemExperimentSegment(0, tandem.CellConcentration, 0));
        foreach (var data in new[] { tandem, Experiment("ordinary") })
        {
            var editor = new ExperimentAttributeEditorControl(new ExperimentAttribute(), (_, _) => true, data);
            var keys = Field<ComboBox>(editor, "keyCombo").Items.Cast<object>()
                .Select(choice => (AttributeKey)choice.GetType().GetProperty("Value")!.GetValue(choice)!);
            Assert.Equal(!data.IsTandemExperiment, keys.Contains(AttributeKey.BufferSubtraction));
        }
    }

    [AvaloniaFact]
    public void ExistingTandemCorrectionControlsAreReadOnly()
    {
        var tandem = Experiment("tandem");
        Correct(tandem);
        tandem.AddSegment(new TandemExperimentSegment(0, tandem.CellConcentration, 0));
        var editor = new ExperimentAttributeEditorControl(tandem.Attributes.Single().Copy(), (_, _) => true, tandem);

        foreach (var name in new[] { "referenceCombo", "methodCombo" })
        {
            var control = Field<ComboBox>(editor, name);
            Assert.False(control.IsEnabled);
            Assert.Equal(BufferSubtractionPolicy.TandemTargetReason, ToolTip.GetTip(control));
        }
        Assert.True(editor.TryApply(tandem, out var error), error);
    }

    static void Correct(ExperimentData data) => data.AddOrUpdateAttribute(new BufferSubtractionSettings(
        "stored-blank", BufferSubtractionMethod.MatchedInjection,
        new BufferSubtractionReferenceSnapshot("blank", new[] { new BufferSubtractionReferencePoint(1, new FloatWithError(2e-6)) }))
        .ToAttribute(), notify: false);

    static ExperimentData Experiment(string name, double volume = 2e-6)
    {
        var data = new ExperimentData(name)
        {
            CellVolume = 200e-6, CellConcentration = new FloatWithError(20e-6), SyringeConcentration = new FloatWithError(200e-6)
        };
        data.DataPoints.Add(new DataPoint(0, 0, 25));
        data.DataPoints.Add(new DataPoint(1, 0, 25));
        var injection = InjectionData.FromPEAQFile(data, 0, true, 0, volume, 10, 1, 25);
        injection.SetPeakArea(new FloatWithError(10e-6));
        data.Injections.Add(injection);
        return data;
    }

    static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    static void Invoke(object instance, string name) => instance.GetType().GetMethod(name, Private)!.Invoke(instance, null);
}
