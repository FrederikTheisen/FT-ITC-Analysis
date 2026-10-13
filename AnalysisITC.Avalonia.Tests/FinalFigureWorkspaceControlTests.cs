using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class FinalFigureWorkspaceControlTests
{
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
    public void ExportFileNamesSkipExistingFilesAndDuplicates()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), "FinalFigureExportTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);

        try
        {
            File.WriteAllText(Path.Combine(folderPath, "Experiment.pdf"), "existing PDF");
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var firstPath = FinalFigureWorkspaceControl.GetAvailableExportPath(folderPath, "Experiment", usedNames);
            var secondPath = FinalFigureWorkspaceControl.GetAvailableExportPath(folderPath, "Experiment", usedNames);
            var sanitizedPath = FinalFigureWorkspaceControl.GetAvailableExportPath(folderPath, "Bad/Name", usedNames);

            Assert.Equal(Path.Combine(folderPath, "Experiment (2).pdf"), firstPath);
            Assert.Equal(Path.Combine(folderPath, "Experiment (3).pdf"), secondPath);
            Assert.Equal(Path.Combine(folderPath, "Bad_Name.pdf"), sanitizedPath);
        }
        finally
        {
            Directory.Delete(folderPath, recursive: true);
        }
    }
}
