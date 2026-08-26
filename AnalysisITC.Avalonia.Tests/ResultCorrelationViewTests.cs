using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Xunit;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Application;
using AnalysisITC.Platform;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ResultCorrelationViewTests
{
    public ResultCorrelationViewTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void ResultViewMenuHasStableOrderAndRealSeparator()
    {
        AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
        try
        {
            var workspace = new AnalysisResultWorkspaceControl();
            Assert.Equal(ResultAnalysisViewMode.Summary, workspace.ActiveViewMode);
            Assert.Equal(new[] { ResultAnalysisViewMode.Fit, ResultAnalysisViewMode.Correlation, ResultAnalysisViewMode.Summary },
                workspace.AvailableViewModes.ToArray());

            Assert.IsType<ComboBoxItem>(workspace.ResultViewCombo.Items[0]);
            Assert.Equal("fit", Assert.IsType<ComboBoxItem>(workspace.ResultViewCombo.Items[0]).Tag);
            Assert.IsType<ComboBoxItem>(workspace.ResultViewCombo.Items[1]);
            Assert.Equal("correlation", Assert.IsType<ComboBoxItem>(workspace.ResultViewCombo.Items[1]).Tag);
            Assert.IsType<Separator>(workspace.ResultViewCombo.Items[2]);
            Assert.Equal("summary", Assert.IsType<ComboBoxItem>(workspace.ResultViewCombo.Items[3]).Tag);
        }
        finally { AnalysisResultWorkspaceControl.ResetSessionViewForTesting(); }
    }

    [Fact]
    public void ResultViewIsRememberedForSessionButNotStoredInSettings()
    {
        var originalStore = PlatformServices.SettingsStore;
        var store = new InMemorySettingsStore();
        PlatformServices.RegisterSettingsStore(store);
        AnalysisResultWorkspaceControl.ResetSessionViewForTesting();

        try
        {
            var first = new AnalysisResultWorkspaceControl();
            Assert.Equal(ResultAnalysisViewMode.Summary, first.ActiveViewMode);

            first.SetResultViewMode(ResultAnalysisViewMode.Correlation);
            var recreated = new AnalysisResultWorkspaceControl();
            Assert.Equal(ResultAnalysisViewMode.Correlation, recreated.ActiveViewMode);

            AppSettings.Save();
            Assert.False(store.Contains("LastAnalysisResultViewId"));

            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            var newSession = new AnalysisResultWorkspaceControl();
            Assert.Equal(ResultAnalysisViewMode.Summary, newSession.ActiveViewMode);
        }
        finally
        {
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    [Fact]
    public void CorrelationGraphUsesSymmetricTwoDecimalValuesAndAccessibleText()
    {
        var graph = new ResultCorrelationGraphControl();
        graph.SetMatrix(new[] { "Shared ΔH", "Local Kd" }, new[,] { { 1.0, -.1254 }, { -.1254, 1.0 } });

        Assert.Equal(2, graph.Count);
        Assert.Equal(1, graph.GetValue(0, 0));
        Assert.Equal(-.1254, graph.GetValue(0, 1), 4);
        Assert.Contains("correlation matrix", graph.AccessibleText);
    }

    [Fact]
    public void CorrelationLayoutCentersLargeMatrixAndCompactsMemberLabels()
    {
        var graph = new ResultCorrelationGraphControl();
        graph.SetMatrix(
            new[]
            {
                "Shared · dG",
                "Local (PRLR_W392A_run2) · N",
                "Local (PRLR_W392A_run2) · dH",
                "Local (PRLR_W392A_run2) · offset"
            },
            new[,]
            {
                { 1.0, .3, -.28, .31 },
                { .3, 1.0, -.04, .64 },
                { -.28, -.04, 1.0, -.76 },
                { .31, .64, -.76, 1.0 }
            });

        graph.Measure(new Size(800, 700));
        graph.Arrange(new Rect(0, 0, 800, 700));

        var matrix = graph.MatrixBoundsForTesting;
        Assert.True(matrix.Width >= 440);
        Assert.InRange(matrix.X, 120, 260);
        Assert.InRange(matrix.Right, 540, 680);
        Assert.InRange(graph.LegendYForTesting - matrix.Bottom, 10, 16);
        Assert.Equal("Global · ΔG", graph.CompactLabelForTesting(0));
        Assert.Equal("Experiment · N", graph.CompactLabelForTesting(1));
    }

    [Fact]
    public void CorrelationLabelsUseExplicitScopeAndPreserveSitesAndUnlockedMarkers()
    {
        var graph = new ResultCorrelationGraphControl();
        graph.SetMatrix(
            new[]
            {
                "Shared · dCp",
                "Local (PRLR_W392A_run2) · log10 Ka1*",
                "Local · dH2",
                "N1"
            },
            new double[4, 4]);

        Assert.Equal("Global · ΔCp", graph.CompactLabelForTesting(0));
        Assert.Equal("Experiment · log₁₀Ka1*", graph.CompactLabelForTesting(1));
        Assert.Equal("Experiment · ΔH2", graph.CompactLabelForTesting(2));
        Assert.Equal("N1", graph.CompactLabelForTesting(3));
    }

    [Fact]
    public void CorrelationHoverIsDisabledByDefaultAndReservesNoPresentation()
    {
        var graph = new ResultCorrelationGraphControl();
        graph.SetMatrix(new[] { "Global · dG", "Experiment · N" }, new[,] { { 1.0, .25 }, { .25, 1.0 } });
        graph.Measure(new Size(500, 500));
        graph.Arrange(new Rect(0, 0, 500, 500));

        var cell = graph.MatrixBoundsForTesting.Center;
        graph.UpdateHoverAtForTesting(cell);

        Assert.Equal(ResultCorrelationGraphControl.CorrelationHoverPolicy.Disabled, graph.HoverPolicyForTesting);
        Assert.Null(graph.HoveredCellForTesting);
        Assert.Null(graph.HoverToolTipForTesting);
        Assert.Null(graph.Cursor);
    }

    [Fact]
    public void CorrelationHoverPoliciesExposeLabelsValuesAndReplicateSummary()
    {
        var graph = new ResultCorrelationGraphControl();
        graph.SetMatrix(new[] { "Global · dG", "Experiment · N" }, new[,] { { 1.0, .25 }, { .25, 1.0 } });
        graph.Measure(new Size(500, 500));
        graph.Arrange(new Rect(0, 0, 500, 500));
        var matrix = graph.MatrixBoundsForTesting;
        var offDiagonalCell = new Point(
            matrix.X + matrix.Width * .75,
            matrix.Y + matrix.Height * .25);

        graph.HoverPolicyForTesting = ResultCorrelationGraphControl.CorrelationHoverPolicy.Always;
        graph.UpdateHoverAtForTesting(offDiagonalCell);
        Assert.Equal((0, 1), graph.HoveredCellForTesting);
        Assert.Contains("Global · ΔG vs Experiment · N", graph.HoverToolTipForTesting);
        Assert.Contains("Pearson r: 0.25", graph.HoverToolTipForTesting);
        Assert.Contains("Replicates: 0", graph.HoverToolTipForTesting);

        graph.HoverPolicyForTesting = ResultCorrelationGraphControl.CorrelationHoverPolicy.WhenValuesHidden;
        Assert.True(graph.ShowValuesForTesting);
        graph.UpdateHoverAtForTesting(offDiagonalCell);
        Assert.Null(graph.HoveredCellForTesting);

        const int count = 20;
        var compactLabels = Enumerable.Range(1, count)
            .Select(index => $"Experiment · N{index}")
            .ToArray();
        var compactMatrix = new double[count, count];
        for (var index = 0; index < count; index++) compactMatrix[index, index] = 1;
        graph.SetMatrix(compactLabels, compactMatrix);
        graph.HoverPolicyForTesting = ResultCorrelationGraphControl.CorrelationHoverPolicy.WhenValuesHidden;
        graph.Measure(new Size(500, 400));
        graph.Arrange(new Rect(0, 0, 500, 400));
        Assert.False(graph.ShowValuesForTesting);
        matrix = graph.MatrixBoundsForTesting;
        Assert.InRange(matrix.Left, 0, 500);
        Assert.InRange(matrix.Right, 0, 500);
        Assert.InRange(matrix.Top, 0, 400);
        Assert.InRange(matrix.Bottom, 0, 400);
        var firstCell = new Point(
            matrix.X + matrix.Width / count / 2,
            matrix.Y + matrix.Height / count / 2);
        graph.UpdateHoverAtForTesting(firstCell);
        Assert.Equal((0, 0), graph.HoveredCellForTesting);
        Assert.Contains("Pearson r: 1.00", graph.HoverToolTipForTesting);
    }

    [Fact]
    public void CorrelationUsesOnePersistentGraphDirectlyInHostWithoutScrolling()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            var workspace = new AnalysisResultWorkspaceControl();
            var window = new Window
            {
                Width = 900,
                Height = 700,
                Content = workspace
            };

            window.Show();
            try
            {
                workspace.SetResultViewMode(ResultAnalysisViewMode.Correlation);
                var graph = workspace.CorrelationGraphForTesting;
                Assert.Same(graph, workspace.GraphHostContentForTesting);
                Assert.IsNotType<ScrollViewer>(workspace.GraphHostContentForTesting);
                workspace.Refresh();
                workspace.Refresh();
                Assert.Same(graph, workspace.GraphHostContentForTesting);
                workspace.SetResultViewMode(ResultAnalysisViewMode.Summary);
                workspace.SetResultViewMode(ResultAnalysisViewMode.Correlation);
                workspace.Refresh();
                Assert.Same(graph, workspace.GraphHostContentForTesting);
            }
            finally
            {
                window.Close();
                AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            }
        });
    }
}
