using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class FinalFigureWorkspaceControlTests
{
    [AvaloniaFact]
    public void UnrelatedFigureDefaultChangePreservesManualDimensionsAndDisplayChoices()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        Window? window = null;
        try
        {
            var initial = PreferencesState.Defaults();
            initial.FinalFigureWidthCentimeters = 9;
            initial.FitLineSmoothness = LineSmoothness.Smooth;
            initial.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation;
            initial.ShowResidualGraph = false;
            initial.Apply();

            var workspace = new FinalFigureWorkspaceControl();
            window = new Window { Content = workspace };
            window.Show();
            var width = (NumericUpDown)typeof(FinalFigureWorkspaceControl)
                .GetField("widthStepper", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            var smoothness = (ComboBox)typeof(FinalFigureWorkspaceControl)
                .GetField("fitLineSmoothnessCombo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            var uncertainty = (ComboBox)typeof(FinalFigureWorkspaceControl)
                .GetField("uncertaintyCombo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            var residuals = (CheckBox)typeof(FinalFigureWorkspaceControl)
                .GetField("residualsCheck", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;

            width.Value = 7;
            smoothness.SelectedIndex = 2;
            uncertainty.SelectedIndex = 3;
            AppSettings.ShowResidualGraph = true;
            AppSettings.Save();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(7, width.Value);
            Assert.Equal(2, smoothness.SelectedIndex);
            Assert.Equal(3, uncertainty.SelectedIndex);
            Assert.True(residuals.IsChecked);
        }
        finally
        {
            window?.Close();
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ContentDefaultsAndSwitchesRemainIndependent(bool details, bool model, bool parameters)
    {
        var original = PreferencesState.FromSettings();
        try
        {
            var selected = FinalFigureDisplayParameters.Temperature | FinalFigureDisplayParameters.Thermodynamic;
            var state = PreferencesState.FromSettings();
            state.FinalFigureShowDetailsAsDefault = details;
            state.FinalFigureShowModelInfoAsDefault = model;
            state.FinalFigureShowParameterBoxAsDefault = parameters;
            state.FinalFigureParameterDisplay = selected;
            state.Apply();
            var workspace = new FinalFigureWorkspaceControl();
            var controls = new[] { "experimentDetailsCheck", "modelInfoCheck", "fitParametersCheck" }
                .Select(name => (CheckBox)typeof(FinalFigureWorkspaceControl)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!).ToArray();
            var choices = new[] { details, model, parameters };
            var tooltips = new[] { FinalFigureModelPresentation.ExperimentDetailsTooltip,
                FinalFigureModelPresentation.ModelInfoTooltip, FinalFigureModelPresentation.FitParametersTooltip };
            void Verify()
            {
                var options = workspace.GetOptionsSnapshot();
                Assert.Equal(choices[0], options.ShowExperimentDetails);
                Assert.Equal(choices[1] || choices[2], options.ShowFitParameters);
                var expected = (choices[0] ? FinalFigureDisplayParameters.Temperature : 0)
                    | (choices[1] ? FinalFigureDisplayParameters.Model : 0)
                    | (choices[2] ? FinalFigureDisplayParameters.Thermodynamic : 0);
                Assert.Equal(expected, options.DisplayParameters);
                for (var i = 0; i < controls.Length; i++)
                {
                    Assert.Equal(choices[i], controls[i].IsChecked);
                    Assert.Equal(tooltips[i], ToolTip.GetTip(controls[i]));
                }
                Assert.Equal(selected, AppSettings.FinalFigureParameterDisplay);
            }
            Verify();
            for (var i = 0; i < controls.Length; i++)
            {
                choices[i] = !choices[i]; controls[i].IsChecked = choices[i]; Verify();
                choices[i] = !choices[i]; controls[i].IsChecked = choices[i]; Verify();
            }
        }
        finally { original.Apply(); }
    }

    [AvaloniaFact]
    public void TraceabilityModeDisablesExcludedPointVisibilityAndRestoresNormalChoice()
    {
        var original = AppSettings.TraceabilityModeEnabled;
        try
        {
            AppSettings.TraceabilityModeEnabled = false;
            var workspace = new FinalFigureWorkspaceControl();
            var control = workspace.ExcludedPointsForTesting;
            control.IsChecked = false;
            Assert.False(workspace.GetOptionsSnapshot().ShowBadData);
            var snapshot = workspace.GetOptionsSnapshot();

            AppSettings.TraceabilityModeEnabled = true;
            workspace.ApplySettingsDefaults();
            Assert.False(control.IsEnabled);
            Assert.True(control.IsChecked);
            Assert.Equal(ExcludedInjectionPresentation.VisibilityTooltip, ToolTip.GetTip(control));
            Assert.True(workspace.GetOptionsSnapshot().ShowBadData);
            Assert.True(snapshot.ShowBadData);

            control.IsChecked = false;
            Assert.True(control.IsChecked);
            Assert.True(workspace.GetOptionsSnapshot().ShowBadData);

            AppSettings.TraceabilityModeEnabled = false;
            workspace.ApplySettingsDefaults();
            Assert.True(control.IsEnabled);
            Assert.False(control.IsChecked);
            Assert.False(workspace.GetOptionsSnapshot().ShowBadData);

            AppSettings.TraceabilityModeEnabled = true;
            var traceabilityWorkspace = new FinalFigureWorkspaceControl();
            Assert.True(traceabilityWorkspace.ExcludedPointsForTesting.IsChecked);
            Assert.False(traceabilityWorkspace.ExcludedPointsForTesting.IsEnabled);
        }
        finally { AppSettings.TraceabilityModeEnabled = original; }
    }

    [AvaloniaTheory]
    [InlineData(300)]
    [InlineData(330)]
    public void InspectorGroupsModelControlsAndMovesGraphSettingsToBottom(int width)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var workspace = new FinalFigureWorkspaceControl();
            var inspector = workspace.GetLogicalDescendants().OfType<TabControl>().Single();
            foreach (var index in new[] { 1, 2 })
            {
                var tab = (TabItem)inspector.Items[index]!;
                var scroll = ((Control)tab.Content!).GetLogicalDescendants().OfType<ScrollViewer>().Single();
                var panel = (StackPanel)((Border)scroll.Content!).Child!;
                var availableWidth = width - 18; // Scroll border and content padding.
                panel.Measure(new global::Avalonia.Size(availableWidth, double.PositiveInfinity));
                panel.Arrange(new global::Avalonia.Rect(0, 0, availableWidth, panel.DesiredSize.Height));
                var sections = panel.Children.Cast<Border>().Select(border => (StackPanel)border.Child!).ToArray();
                var headings = sections.Select(section => ((TextBlock)section.Children[0]).Text).ToArray();
                Assert.Equal(index == 1
                    ? new[] { "Shared axes", "Baseline", "Integration ranges", "Data graph" }
                    : new[] { "Model", "Shared axes", "Residuals", "Display", "Fit graph" }, headings);
                if (index == 2)
                {
                    Assert.Contains(workspace.PlottedModelSelectorForTesting, sections[0].GetLogicalDescendants());
                    Assert.Contains(workspace.LineWidthForTesting, sections[0].GetLogicalDescendants());
                    Assert.Contains(workspace.ConfidenceForTesting, sections[0].GetLogicalDescendants());
                    Assert.Contains(workspace.OffsetCorrectionForTesting, sections[0].GetLogicalDescendants());
                    Assert.Equal(new[] { "Binding", "Null", "None" }, workspace.PlottedModelSelectorForTesting.Options);
                    var selector = workspace.PlottedModelSelectorForTesting;
                    Assert.True(selector.Bounds.Width >= 180);
                    Assert.True(selector.Bounds.Right <= ((Control)selector.Parent!).Bounds.Width + 0.01);
                }
            }
        });
    }

    [AvaloniaTheory]
    [InlineData(0, 3)]
    [InlineData(1, 7)]
    [InlineData(2, 14)]
    public void TickDensityMapsToItsConfiguredTarget(int densityIndex, int expectedTickCount)
    {
        Assert.Equal(expectedTickCount, FinalFigureWorkspaceControl.TickCountForDensity((FinalFigureTickDensity)densityIndex));
    }

    [AvaloniaFact]
    public void NewWorkspaceUsesNormalDensityForAllConfigurableAxes()
    {
        var workspace = new FinalFigureWorkspaceControl();
        var selectors = workspace.TickDensitySelectors;
        var options = workspace.GetOptionsSnapshot();

        Assert.Equal(4, selectors.Count);
        Assert.All(selectors, selector =>
        {
            Assert.Equal(new[] { "Sparse", "Normal", "Dense" }, selector.Options);
            Assert.Equal((int)FinalFigureTickDensity.Normal, selector.SelectedIndex);
        });
        Assert.Equal(7, options.DataXTickCount);
        Assert.Equal(7, options.DataYTickCount);
        Assert.Equal(7, options.FitXTickCount);
        Assert.Equal(7, options.FitYTickCount);
        Assert.True(options.SanitizeTicks);
    }

    [AvaloniaFact]
    public void TickDensitySelectorsRemainIndependentInTheOptionsSnapshot()
    {
        var workspace = new FinalFigureWorkspaceControl();
        var selectors = workspace.TickDensitySelectors;

        selectors[0].SelectedIndex = (int)FinalFigureTickDensity.Sparse;
        selectors[1].SelectedIndex = (int)FinalFigureTickDensity.Normal;
        selectors[2].SelectedIndex = (int)FinalFigureTickDensity.Dense;
        selectors[3].SelectedIndex = (int)FinalFigureTickDensity.Sparse;

        var options = workspace.GetOptionsSnapshot();

        Assert.Equal(3, options.DataXTickCount);
        Assert.Equal(7, options.DataYTickCount);
        Assert.Equal(14, options.FitXTickCount);
        Assert.Equal(3, options.FitYTickCount);
    }

    [AvaloniaFact]
    public void ExportControlsFillTheInspectorFooterWidth()
    {
        var workspace = new FinalFigureWorkspaceControl();
        var window = new Window
        {
            Width = 1024,
            Height = 768,
            Content = workspace
        };

        window.Show();
        try
        {
            var selector = workspace.ExportSelectionSelectorForTesting;
            var exportButton = workspace.ExportButtonForTesting;

            Assert.InRange(selector.Bounds.Width, 297, 299);
            Assert.Equal(selector.Bounds.Width, exportButton.Bounds.Width);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ExportFilePlannerReservesNaturalNamesBeforeAssigningCollisionSuffixes()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), "FinalFigureExportTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);

        try
        {
            File.WriteAllText(Path.Combine(folderPath, "Experiment.pdf"), "existing PDF");
            var experiments = new[]
            {
                new ExperimentData("one.itc") { Name = "Experiment" },
                new ExperimentData("two.itc") { Name = "Experiment" },
                new ExperimentData("three.itc") { Name = "Experiment (2)" },
                new ExperimentData("four.itc") { Name = "Bad/Name" }
            };

            var targets = FigureExportPlanner.CreateTargets(experiments, folderPath);

            Assert.Equal(new[] { "Experiment.pdf", "Experiment (3).pdf", "Experiment (2).pdf", "Bad_Name.pdf" },
                targets.Select(target => Path.GetFileName(target.Path)));
        }
        finally
        {
            Directory.Delete(folderPath, recursive: true);
        }
    }
}
