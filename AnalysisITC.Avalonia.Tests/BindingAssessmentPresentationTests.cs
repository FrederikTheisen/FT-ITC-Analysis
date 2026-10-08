using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    public void SummaryHeaderMenuSupportsAutomaticAndManualChoices()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(CreateComparison(delta: 0));
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

            var headerMenuButton = CurrentAssessmentMenuButton(workspace);
            Assert.Equal("Modify Assessment", AutomationProperties.GetName(headerMenuButton));
            Assert.Equal("Modify Assessment", ToolTip.GetTip(headerMenuButton)?.ToString());
            var headerGrid = Assert.IsType<Grid>(headerMenuButton.Parent);
            Assert.Contains(headerGrid.Children.OfType<TextBlock>(), block => block.Text == "Null hypothesis test");
            Assert.Contains(headerGrid.Children, child => ReferenceEquals(child, headerMenuButton));
            Assert.Empty(NullAssessmentSection(workspace).GetLogicalDescendants().OfType<ComboBox>());
            var menu = CurrentAssessmentMenu(workspace);
            Assert.Equal(new[] { "Use Automatic Assessment", "Mark Binding Detected", "Mark No Binding Detected" },
                menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray());
            Assert.Equal("No binding detected", ConclusionValue(workspace).Text);

            // Marking the currently displayed automatic conclusion still records an explicit manual choice.
            Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, result.BindingAssessment.AutomaticOutcome);
            Assert.Null(result.BindingAssessment.ManualOverride);
            ClickChoice(menu, 2);
            Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, result.BindingAssessment.ManualOverride);
            Assert.Equal("No binding detected", ConclusionValue(workspace).Text);

            ClickChoice(CurrentAssessmentMenu(workspace), 1);
            Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.BindingAssessment.ManualOverride);
            Assert.Equal("Binding detected", ConclusionValue(workspace).Text);
            Assert.Equal(3, CurrentAssessmentMenu(workspace).Items.OfType<MenuItem>().Count());
            ClickChoice(CurrentAssessmentMenu(workspace), 0);
            Assert.Null(result.BindingAssessment.ManualOverride);
            Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, result.BindingAssessment.EffectiveOutcome);
            Assert.Equal("No binding detected", ConclusionValue(workspace).Text);

            // Changing the selected result detaches the previous assessment event.
            AssertAssessmentHandler(result, workspace, expected: true);
            workspace.Result = CreateResult();
            AssertAssessmentHandler(result, workspace, expected: false);
            var newlySelectedResult = workspace.Result!;
            ClickChoice(menu, 0);
            Assert.Null(newlySelectedResult.BindingAssessment.ManualOverride);
            var selectedResultSection = NullAssessmentSection(workspace);
            result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
            Assert.Same(selectedResultSection, NullAssessmentSection(workspace));
            workspace.Refresh();
            Assert.Equal("Not assessed", ConclusionValue(workspace).Text);
        });
    }

    [Theory]
    [InlineData(null, "Not assessed")]
    [InlineData(8d, "Inconclusive")]
    public void AutomaticOutcomesRemainReadOnlyWithoutChangingSavedAssessment(double? delta, string expected)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(delta.HasValue ? CreateComparison(delta.Value) : null);
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            Assert.Equal(expected, ConclusionValue(workspace).Text);
            Assert.Empty(NullAssessmentSection(workspace).GetLogicalDescendants().OfType<ComboBox>());
            Assert.Null(result.BindingAssessment.ManualOverride);
        });
    }

    [Fact]
    public void InconclusiveStatusDisplaysSharedHealthReasonAndManualOverrideClearsIt()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(CreateComparison(delta: 5));
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            Assert.Contains(AnalysisResultHealthReasonFormatter.InconclusiveMessage,
                TextFrom(workspace.SummaryPanelForTesting));
            Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
            result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
            workspace.Refresh();
            Assert.DoesNotContain(AnalysisResultHealthReasonFormatter.InconclusiveMessage,
                TextFrom(workspace.SummaryPanelForTesting));
        });
    }

    [Fact]
    public void ResultListStatusTooltipIncludesInconclusiveReasonAndRefreshesAfterOverride()
    {
        var result = CreateResult(CreateComparison(delta: 5));
        var entry = AnalysisITC.Avalonia.MainWindow.DataListEntry.From(result);
        Assert.Equal("Warning", entry.ValidityLabel);
        Assert.Contains(AnalysisResultHealthReasonFormatter.InconclusiveMessage, entry.ValidityTooltip);
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        entry.RefreshState(result);
        Assert.Equal("Valid", entry.ValidityLabel);
        Assert.DoesNotContain(AnalysisResultHealthReasonFormatter.InconclusiveMessage, entry.ValidityTooltip);
    }

    [Fact]
    public void SavedManualAssessmentInitializesAndSurvivesSummaryRefresh()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(CreateComparison(delta: 8));
            result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
            var workspace = new AnalysisResultWorkspaceControl { Result = result };

            Assert.Equal("Binding detected", ConclusionValue(workspace).Text);
            Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.BindingAssessment.ManualOverride);
            workspace.Refresh();
            Assert.Equal("Binding detected", ConclusionValue(workspace).Text);
            Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.BindingAssessment.ManualOverride);
        });
    }

    [Fact]
    public void IndependentMemberMenusNumberDuplicateNamesAndShowEffectiveMode()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateIndependentResult(out var members);
            result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.BindingDetected);
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            var items = CurrentAssessmentMenu(workspace).Items.OfType<MenuItem>().ToList();

            Assert.Equal("Use Automatic Assessment", items[0].Header?.ToString());
            var first = items[1];
            var second = items[2];
            Assert.Equal("1 — Duplicate", first.Header?.ToString());
            Assert.Equal("2 — Duplicate", second.Header?.ToString());
            var firstItems = first.Items.OfType<MenuItem>().ToList();
            Assert.False(firstItems[0].IsEnabled);
            Assert.Contains("Binding detected (manual)", firstItems[0].Header?.ToString());
            Assert.Equal(new[] { "Mark Binding Detected", "Mark No Binding Detected" },
                firstItems.Skip(1).Select(item => item.Header?.ToString()).ToArray());
            Assert.Contains("Not assessed", second.Items.OfType<MenuItem>().First().Header?.ToString());
            Assert.Contains("automatic", second.Items.OfType<MenuItem>().First().Header?.ToString());
            var summaryText = TextFrom(workspace.SummaryPanelForTesting);
            Assert.Contains("Mixed assessments", summaryText);
            Assert.Equal("Member assessments", CollectionAssessmentLabel(workspace).Text);
            Assert.Equal("Mixed assessments", CollectionAssessmentValue(workspace).Text);
            Assert.Contains("Binding detected: 1", ToolTip.GetTip(CollectionAssessmentValue(workspace))?.ToString());
            Assert.Contains("Not assessed: 1", ToolTip.GetTip(CollectionAssessmentValue(workspace))?.ToString());
        });
    }

    [Fact]
    public void IndependentCollectionSummaryShowsUniformAssessmentAndMixedEffectiveOverrides()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateIndependentResult(out var members);
            var workspace = new AnalysisResultWorkspaceControl { Result = result };

            result.SetMemberBindingAssessmentOverride(members[1].Guid, BindingAssessmentOutcome.BindingDetected);
            workspace.Refresh();
            Assert.Equal("Binding detected (2 experiments)", CollectionAssessmentValue(workspace).Text);

            result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.NoBindingDetected);
            workspace.Refresh();
            Assert.Equal("Mixed assessments", CollectionAssessmentValue(workspace).Text);

            result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.BindingDetected);
            workspace.Refresh();
            Assert.Equal("Binding detected (2 experiments)", CollectionAssessmentValue(workspace).Text);

            result.UseAutomaticBindingAssessments();
            workspace.Refresh();
            Assert.Equal("Mixed assessments", CollectionAssessmentValue(workspace).Text);
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

            var conclusion = ConclusionValue(workspace);
            Assert.Equal("No binding detected", conclusion.Text);
            var tooltip = ToolTip.GetTip(conclusion)?.ToString();
            Assert.Equal("Manual override. Automatic: Binding detected.", tooltip);
            var evidenceRow = Assert.Single(workspace.SummaryPanelForTesting.GetLogicalDescendants()
                .OfType<Border>().Where(border => border.Child is Grid grid
                    && grid.Children.OfType<TextBlock>().Any(block => block.Text == "RMSD / ΔAICc")));
            Assert.Contains("Null AICc: 15", ToolTip.GetTip(evidenceRow)?.ToString());
        });
    }

    [Fact]
    public void HeaderMenuFitsAtNarrowInspectorWidth()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(CreateComparison(delta: 0));
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
                var menuButton = CurrentAssessmentMenuButton(workspace);
            Assert.Equal("Modify Assessment", menuButton.Content);
                Assert.True(menuButton.Bounds.Width > 0 && menuButton.Bounds.Height > 0,
                    "The standard dropdown control should remain laid out in a narrow inspector.");
                var header = Assert.IsType<Grid>(menuButton.Parent);
                Assert.True(menuButton.Bounds.Right <= header.Bounds.Width + 1,
                    "The header action should remain within the section title row.");
                var headerTitle = Assert.Single(header.Children.OfType<TextBlock>());
                Assert.Equal(global::Avalonia.Media.TextTrimming.None, headerTitle.TextTrimming);
                Assert.True(headerTitle.Bounds.Width > 0);
                headerTitle.Measure(new Size(headerTitle.Bounds.Width, double.PositiveInfinity));
                Assert.True(headerTitle.Bounds.Height + 1 >= headerTitle.DesiredSize.Height,
                    "The wrapped section title should have enough height to remain fully visible beside the assessment action.");
                menuButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(menuButton.Bounds.Width + 1 >= menuButton.DesiredSize.Width,
                    "The action label should not be clipped in the narrow inspector.");
                Assert.Equal(3, CurrentAssessmentMenu(workspace).Items.OfType<MenuItem>().Count());
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

    static AnalysisResult CreateIndependentResult(out List<SolutionInterface> members)
    {
        static Model CreateMember(string name, string id)
        {
            var experiment = new ExperimentData(id + ".itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                Name = name,
            };
            experiment.SetID(id);
            experiment.Injections.Add(new InjectionData(experiment, volume: 1e-6)
            {
                IsIntegrated = true,
                Ratio = 1,
            });
            var model = new OneSetOfSites(experiment);
            model.InitializeParameters(experiment);
            model.Solution = SolutionInterface.FromModel(model,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            return model;
        }

        var first = CreateMember("Duplicate", "binding-assessment-ui-first");
        var second = CreateMember("Duplicate", "binding-assessment-ui-second");
        first.Solution.NullComparison = CreateComparison(12);
        var globalModel = new GlobalModel(new List<Model> { first, second })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        globalModel.Parameters.AddIndivdualParameter(first.Parameters);
        globalModel.Parameters.AddIndivdualParameter(second.Parameters);
        members = new List<SolutionInterface> { first.Solution, second.Solution };
        var solution = new GlobalSolution(new GlobalSolver { Model = globalModel }, members,
            first.Solution.Convergence, reconstructBootstrap: false);
        globalModel.Solution = solution;
        return new AnalysisResult(solution);
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

    static Button CurrentAssessmentMenuButton(AnalysisResultWorkspaceControl workspace)
        => Assert.Single(NullAssessmentSection(workspace).GetLogicalDescendants()
            .OfType<Button>().Where(button => button.Flyout is MenuFlyout));

    static MenuFlyout CurrentAssessmentMenu(AnalysisResultWorkspaceControl workspace)
        => Assert.IsType<MenuFlyout>(CurrentAssessmentMenuButton(workspace).Flyout);

    static void ClickChoice(MenuFlyout menu, int index)
    {
        var choice = menu.Items.OfType<MenuItem>().ElementAt(index);
        choice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, choice));
    }

    static TextBlock ConclusionValue(AnalysisResultWorkspaceControl workspace)
    {
        var border = Assert.Single(NullAssessmentSection(workspace).GetLogicalDescendants()
            .OfType<Border>().Where(border => border.Child is Grid grid
                && grid.Children.OfType<TextBlock>().Any(block => block.Text == "Conclusion")));
        return Assert.IsType<Grid>(border.Child).Children.OfType<TextBlock>().Single(block => block.Text != "Conclusion");
    }

    static TextBlock CollectionAssessmentLabel(AnalysisResultWorkspaceControl workspace)
        => Assert.Single(NullAssessmentSection(workspace).GetLogicalDescendants()
            .OfType<TextBlock>().Where(block => block.Text == "Member assessments"));

    static TextBlock CollectionAssessmentValue(AnalysisResultWorkspaceControl workspace)
    {
        var label = CollectionAssessmentLabel(workspace);
        var row = Assert.IsType<Border>(label.Parent?.Parent);
        return Assert.IsType<Grid>(row.Child).Children.OfType<TextBlock>().Single(block => !ReferenceEquals(block, label));
    }

    static StackPanel NullAssessmentSection(AnalysisResultWorkspaceControl workspace)
        => NullAssessmentBorder(workspace)
            .Child as StackPanel ?? throw new Xunit.Sdk.XunitException("Null assessment section does not contain a stack panel.");

    static Border NullAssessmentBorder(AnalysisResultWorkspaceControl workspace)
        => Assert.Single(workspace.SummaryPanelForTesting.GetLogicalDescendants()
            .OfType<Border>().Where(border => border.Child is StackPanel panel
                && panel.GetLogicalDescendants().OfType<TextBlock>().Any(block => block.Text == "Null hypothesis test")));

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
        => root.GetLogicalDescendants().OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty).ToArray();
}
