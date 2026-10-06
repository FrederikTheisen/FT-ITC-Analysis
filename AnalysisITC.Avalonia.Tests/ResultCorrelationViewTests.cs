using System.Linq;
using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using Xunit;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Platform;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ResultCorrelationViewTests
{
    public ResultCorrelationViewTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void ResultViewMenuHasStableOrder()
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
            Assert.Equal("summary", Assert.IsType<ComboBoxItem>(workspace.ResultViewCombo.Items[2]).Tag);
            Assert.Equal(3, workspace.ResultViewCombo.Items.Count);
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
    public void CorrelationHoverIsEnabledByDefault()
    {
        var graph = new ResultCorrelationGraphControl();
        graph.SetMatrix(new[] { "Global · dG", "Experiment · N" }, new[,] { { 1.0, .25 }, { .25, 1.0 } });
        graph.Measure(new Size(500, 500));
        graph.Arrange(new Rect(0, 0, 500, 500));

        var cell = graph.MatrixBoundsForTesting.Center;
        graph.UpdateHoverAtForTesting(cell);

        Assert.Equal(ResultCorrelationGraphControl.CorrelationHoverPolicy.Always, graph.HoverPolicyForTesting);
        Assert.NotNull(graph.HoveredCellForTesting);
        Assert.Contains("Pearson r", graph.HoverToolTipForTesting);
        Assert.NotNull(graph.Cursor);
    }

    [Fact]
    public void CorrelationHoverPoliciesExposeLabelsAndValues()
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
        Assert.DoesNotContain("Replicates", graph.HoverToolTipForTesting);

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
    public void CorrelationHoverReportsFisherPrecisionAndUncertainSign()
    {
        var graph = new ResultCorrelationGraphControl();
        graph.SetMatrix(new[] { "Global · dG", "Experiment · N" }, new[,] { { 1.0, 0.0 }, { 0.0, 1.0 } });
        var self = Cell(1, 100, true, null, null);
        var pair = Cell(0, 100, false, -0.196418119191219, 0.196418119191219);
        graph.SetCellDiagnosticsForTesting(new[,] { { self, pair }, { pair, self } });
        Assert.Contains("Complete refits: 100", graph.AccessibleText);
        Assert.Contains("Approx. 95% MC precision", graph.AccessibleText);
        graph.Measure(new Size(500, 500));
        graph.Arrange(new Rect(0, 0, 500, 500));
        var matrix = graph.MatrixBoundsForTesting;

        graph.UpdateHoverAtForTesting(new Point(
            matrix.X + matrix.Width * .75,
            matrix.Y + matrix.Height * .25));

        Assert.Contains("Pearson r: 0.000", graph.HoverToolTipForTesting);
        Assert.DoesNotContain("Complete refits", graph.HoverToolTipForTesting);
        Assert.Contains("Approx. 95% MC precision: [-0.196, 0.196]", graph.HoverToolTipForTesting);
        Assert.Contains("Sign unresolved", graph.HoverToolTipForTesting);

        graph.UpdateHoverAtForTesting(new Point(
            matrix.X + matrix.Width * .25,
            matrix.Y + matrix.Height * .25));
        Assert.Contains("Self-correlation", graph.HoverToolTipForTesting);
        Assert.Contains("not applicable", graph.HoverToolTipForTesting);
    }

    static BootstrapCorrelationCellDiagnostic Cell(
        double r, int count, bool self, double? lower, double? upper)
        => (BootstrapCorrelationCellDiagnostic)typeof(BootstrapCorrelationCellDiagnostic)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(double), typeof(int), typeof(bool), typeof(double?), typeof(double?) }, null)!
            .Invoke(new object?[] { r, count, self, lower, upper });

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

    [Fact]
    public void IndependentMemberSelectionUpdatesCorrelationCountsScopeAndWarning()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            var members = new[] { CreateBootstrapMember("many-refits", 100), CreateBootstrapMember("few-refits", 30) };
            var model = new GlobalModel(members.Select(member => member.Model).ToList())
            {
                ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
            };
            foreach (var member in members) model.Parameters.AddIndivdualParameter(member.Model.Parameters);
            var solution = new GlobalSolution(new GlobalSolver { Model = model }, members.ToList(), members[0].Convergence);
            model.Solution = solution;
            var workspace = new AnalysisResultWorkspaceControl { Result = new AnalysisResult(solution) };
            var window = new Window { Content = workspace };
            window.Show();
            try
            {
                workspace.SetResultViewMode(ResultAnalysisViewMode.Correlation);
                var panel = (StackPanel)typeof(AnalysisResultWorkspaceControl)
                    .GetField("analysisPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
                foreach (var index in new[] { 0, 1, 0 })
                {
                    DataManager.SelectResultSolution(members[index]);
                    Assert.Equal(index == 0 ? 100 : 30, workspace.CorrelationGraphForTesting.UsedCount);
                    Assert.Equal("Single experiment", workspace.CorrelationGraphForTesting.Scope);
                    var warnings = panel.GetLogicalDescendants().OfType<TextBlock>()
                        .Where(block => block.Text?.Contains("makes Monte Carlo precision coarse") == true).ToList();
                    if (index == 0) Assert.Empty(warnings);
                    else Assert.Contains("Only 30 complete refits", Assert.Single(warnings).Text);
                }
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
                AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            }
        });
    }

    static SolutionInterface CreateBootstrapMember(string name, int count)
    {
        OneSetOfSites Model(int index)
        {
            var data = new ExperimentData(name + ".itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
            };
            data.Injections.Add(new InjectionData(data, volume: 1e-6) { IsIntegrated = true, Ratio = 1 });
            var model = new OneSetOfSites(data)
            {
                ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
            };
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1 + index * .01);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000 + index);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6 + index * .01);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            model.Solution.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
            return model;
        }

        var primary = Model(0).Solution;
        primary.SetBootstrapSolutions(Enumerable.Range(0, count).Select(index => Model(index).Solution).ToList());
        return primary;
    }
}
