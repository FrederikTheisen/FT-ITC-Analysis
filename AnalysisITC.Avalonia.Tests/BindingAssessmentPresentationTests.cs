using System.Linq;
using System.Globalization;
using System.Reflection;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class BindingAssessmentPresentationTests
{
    public BindingAssessmentPresentationTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void SummaryAssessmentSelectorSupportsBothManualChoicesOnly()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(CreateComparison(delta: 6));
            var workspace = new AnalysisResultWorkspaceControl { Result = result };

            var text = TextFrom(workspace.SummaryPanelForTesting);
            Assert.Contains("Null hypothesis test", text);
            Assert.Contains("Model", text);
            Assert.Contains("RMSD / ΔAICc", text);
            Assert.Contains("No binding detected", text);
            Assert.DoesNotContain("Change…", text);
            Assert.DoesNotContain("These are chosen cutoffs", text);
            Assert.True(IndexOf(text, "Result") < IndexOf(text, "Null hypothesis test"));
            Assert.True(IndexOf(text, "Null hypothesis test") < IndexOf(text, "Information criteria"));
            Assert.Equal(4, NullAssessmentSection(workspace).Children.OfType<Control>().Count());

            var automaticSelector = CurrentSelector(workspace);
            Assert.Equal(-1, automaticSelector.SelectedIndex);
            Assert.Equal("No binding detected", automaticSelector.PlaceholderText);
            Assert.Equal(new[] { "Binding detected", "No binding detected" }, automaticSelector.ItemsSource!.Cast<string>().ToArray());
            Assert.Equal(24, automaticSelector.Height);
            Assert.Equal(new Thickness(8, 0), automaticSelector.Padding);
            Assert.Equal(HorizontalAlignment.Stretch, automaticSelector.HorizontalAlignment);

            // Selecting the displayed automatic conclusion still records an explicit manual choice.
            Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, result.BindingAssessment.AutomaticOutcome);
            Assert.Null(result.BindingAssessment.ManualOverride);
            SelectChoice(automaticSelector, 1);
            Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, result.BindingAssessment.ManualOverride);
            Assert.Contains("No binding detected", TextFrom(workspace.SummaryPanelForTesting));

            SelectChoice(CurrentSelector(workspace), 0);
            Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.BindingAssessment.ManualOverride);
            Assert.Contains("Binding detected", TextFrom(workspace.SummaryPanelForTesting));
            Assert.Equal(2, CurrentSelector(workspace).ItemsSource!.Cast<string>().Count());

            // Changing the selected result detaches the previous assessment event.
            AssertAssessmentHandler(result, workspace, expected: true);
            workspace.Result = CreateResult();
            AssertAssessmentHandler(result, workspace, expected: false);
            var selectedResultSection = NullAssessmentSection(workspace);
            result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
            Assert.Same(selectedResultSection, NullAssessmentSection(workspace));
            workspace.Refresh();
            Assert.Contains("Not assessed", TextFrom(workspace.SummaryPanelForTesting));
        });
    }

    [Theory]
    [InlineData(null, "Not assessed")]
    [InlineData(8d, "Inconclusive")]
    public void AutomaticOutcomesUsePlaceholderWithoutChangingSavedAssessment(double? delta, string expected)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(delta.HasValue ? CreateComparison(delta.Value) : null);
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            var selector = CurrentSelector(workspace);

            Assert.Equal(-1, selector.SelectedIndex);
            Assert.Equal(expected, selector.PlaceholderText);
            Assert.Null(result.BindingAssessment.ManualOverride);
        });
    }

    [Fact]
    public void SavedManualAssessmentInitializesAndSurvivesSummaryRefresh()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(CreateComparison(delta: 8));
            result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
            var workspace = new AnalysisResultWorkspaceControl { Result = result };

            Assert.Equal(0, CurrentSelector(workspace).SelectedIndex);
            Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.BindingAssessment.ManualOverride);
            workspace.Refresh();
            Assert.Equal(0, CurrentSelector(workspace).SelectedIndex);
            Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.BindingAssessment.ManualOverride);
        });
    }

    [Fact]
    public void ManualConclusionTooltipKeepsTheSavedAutomaticRecommendation()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var comparison = CreateComparison(delta: 10);
            var result = CreateResult(comparison);
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);

            comparison.DeltaAicc = -5;
            workspace.Refresh();

            var conclusion = CurrentSelector(workspace);
            Assert.Equal(1, conclusion.SelectedIndex);
            Assert.Equal("No binding detected", conclusion.SelectedItem);
            Assert.Equal("No binding detected", AutomationProperties.GetName(conclusion));
            var tooltip = ToolTip.GetTip(conclusion)?.ToString();
            Assert.Contains("Current assessment: No binding detected (Manual)", tooltip);
            Assert.Contains("Automatic recommendation: Binding detected", tooltip);
            var evidenceRow = Assert.Single(workspace.SummaryPanelForTesting.GetLogicalDescendants()
                .OfType<Border>().Where(border => border.Child is Grid grid
                    && grid.Children.OfType<TextBlock>().Any(block => block.Text == "RMSD / ΔAICc")));
            Assert.Contains("Null AICc: 15", ToolTip.GetTip(evidenceRow)?.ToString());
        });
    }

    [Fact]
    public void ConclusionSelectorFitsAtNarrowInspectorWidth()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(CreateComparison(delta: 6));
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            var window = new Window { Content = workspace, Width = 350, Height = 800 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var section = NullAssessmentBorder(workspace);
                section.Measure(new Size(320, double.PositiveInfinity));
                section.Arrange(new Rect(0, 0, 320, section.DesiredSize.Height));
                Dispatcher.UIThread.RunJobs();
                var selector = CurrentSelector(workspace);
                Assert.Equal("No binding detected", selector.PlaceholderText);
                Assert.True(selector.Bounds.Width > 0 && selector.Bounds.Height > 0,
                    "The standard dropdown control should remain laid out in a narrow inspector.");
                Assert.Equal(2, selector.ItemsSource!.Cast<string>().Count());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(EnergyUnitFamily.Joules, 4.184, "µJ")]
    [InlineData(EnergyUnitFamily.Calories, 1, "µcal")]
    public void SavedNullRmsdUsesTheIntegratedHeatUnitScale(EnergyUnitFamily family, double expectedValue, string unit)
    {
        var criteria = FitInformationCriteria.Restore(5, 1, 2,
            GaussianLikelihoodMode.EstimatedCommonVariance, 1, 2, 3,
            true, true, string.Empty, string.Empty,
            0, 4.184, 0, 0);
        var comparison = new NullModelComparison
        {
            NullFitSucceeded = true,
            NullInformationCriteria = criteria,
        };

        var expected = expectedValue.ToString("G4", CultureInfo.CurrentCulture);
        var displayed = NullModelComparisonPresentation.NullRmsd(comparison, family);
        Assert.Equal(expected, displayed);
        Assert.DoesNotContain(unit, displayed);
    }

    static AnalysisResult CreateResult(NullModelComparison? comparison = null)
    {
        var experiment = new ExperimentData("binding-assessment-ui.itc")
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
        };
        experiment.SetID("binding-assessment-ui");
        experiment.Injections.Add(new InjectionData(experiment, volume: 1e-6)
        {
            IsIntegrated = true,
            Ratio = 1,
        });
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        model.Solution.NullComparison = comparison;
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
    }

    static NullModelComparison CreateComparison(double delta)
    {
        static FitInformationCriteria Criteria(double aicc) => FitInformationCriteria.Restore(5, 1, 2,
            GaussianLikelihoodMode.EstimatedCommonVariance, aicc - 2, aicc, aicc,
            true, true, string.Empty, string.Empty,
            0, 4.184, 0, 0);
        var binding = Criteria(5);
        var baseline = Criteria(5 + delta);
        return new NullModelComparison
        {
            BindingFitSucceeded = true,
            NullFitSucceeded = true,
            BindingInformationCriteria = binding,
            NullInformationCriteria = baseline,
            DeltaAicc = delta,
        };
    }

    static ComboBox CurrentSelector(AnalysisResultWorkspaceControl workspace)
        => Assert.Single(NullAssessmentSection(workspace).GetLogicalDescendants()
            .OfType<ComboBox>().Where(combo => combo.ItemsSource is System.Collections.IEnumerable));

    static void SelectChoice(ComboBox selector, int index) => selector.SelectedIndex = index;

    static StackPanel NullAssessmentSection(AnalysisResultWorkspaceControl workspace)
        => NullAssessmentBorder(workspace)
            .Child as StackPanel ?? throw new Xunit.Sdk.XunitException("Null assessment section does not contain a stack panel.");

    static Border NullAssessmentBorder(AnalysisResultWorkspaceControl workspace)
        => Assert.Single(workspace.SummaryPanelForTesting.GetLogicalDescendants()
            .OfType<Border>().Where(border => border.Child is StackPanel panel
                && panel.Children.OfType<TextBlock>().Any(block => block.Text == "Null hypothesis test")));

    static int IndexOf(string[] values, string item) => System.Array.IndexOf(values, item);

    static void AssertAssessmentHandler(AnalysisResult result, object target, bool expected)
    {
        var field = typeof(AnalysisResult).GetField("BindingAssessmentChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var handlers = field.GetValue(result) as System.Delegate;
        var isSubscribed = handlers?.GetInvocationList().Any(handler => ReferenceEquals(handler.Target, target)) == true;
        Assert.Equal(expected, isSubscribed);
    }

    static string[] TextFrom(Control root)
    {
        var text = root.GetLogicalDescendants().OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty);
        var outcomes = root.GetLogicalDescendants().OfType<ComboBox>()
            .Select(combo => combo.SelectedItem?.ToString() ?? combo.PlaceholderText ?? string.Empty);
        return text.Concat(outcomes).ToArray();
    }
}
