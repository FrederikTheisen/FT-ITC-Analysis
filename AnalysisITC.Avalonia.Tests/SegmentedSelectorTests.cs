using System.Linq;

using AnalysisITC.Avalonia.Controls;
using AnalysisITC.Avalonia.Workspace;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SegmentedSelectorTests
{
    public SegmentedSelectorTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void SelectorMaintainsOneSelectionAndRaisesChangesOnlyWhenItChanges()
    {
        var selector = new SegmentedSelector(new[] { "Sparse", "Normal", "Dense" }, 1);
        var changes = 0;
        selector.SelectionChanged += (_, _) => changes++;

        Assert.Equal(1, selector.SelectedIndex);
        Assert.Equal(new bool?[] { false, true, false }, selector.ChoiceButtons.Select(choice => choice.IsChecked));
        Assert.Equal(new[] { false, true, false }, selector.ChoiceButtons.Select(choice => choice.IsTabStop));

        selector.SelectedIndex = 2;
        selector.SelectedIndex = 2;
        selector.ChoiceButtons[0].IsChecked = true;

        Assert.Equal(0, selector.SelectedIndex);
        Assert.Equal(new bool?[] { true, false, false }, selector.ChoiceButtons.Select(choice => choice.IsChecked));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void SelectorExposesAccessibleLabelsForItsChoices()
    {
        var selector = new SegmentedSelector(new[] { "Sparse", "Normal", "Dense" });

        Assert.Equal(new[] { "Sparse", "Normal", "Dense" }, selector.Options);
        Assert.Equal(
            new[] { "Sparse", "Normal", "Dense" },
            selector.ChoiceButtons.Select(AutomationProperties.GetName));
    }

    [Fact]
    public void SelectorSupportsArrowAndHomeKeyboardNavigation()
    {
        var selector = new SegmentedSelector(new[] { "Sparse", "Normal", "Dense" }, 1);
        var right = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Right
        };

        selector.ChoiceButtons[1].RaiseEvent(right);

        Assert.True(right.Handled);
        Assert.Equal(2, selector.SelectedIndex);

        var home = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Home
        };
        selector.ChoiceButtons[2].RaiseEvent(home);

        Assert.True(home.Handled);
        Assert.Equal(0, selector.SelectedIndex);
    }

    [Fact]
    public void WorkspaceSelectorUsesTheStandardFieldHeight()
    {
        var selector = WorkspaceControlBuilder.Segmented(new[] { "Sparse", "Normal", "Dense" });
        var frame = selector.FindControl<Border>("Frame");
        var host = selector.FindControl<Grid>("Host");
        var window = new Window
        {
            Width = 214,
            Height = 64,
            Content = selector
        };

        window.Show();
        try
        {
            Assert.Equal(32, selector.Height);
            Assert.Equal(32, selector.MinHeight);
            Assert.NotNull(frame);
            Assert.NotNull(host);
            Assert.Equal(32, frame.Bounds.Height);
            Assert.False(frame.IsHitTestVisible);
            Assert.Same(frame, host.Children[^1]);
        }
        finally
        {
            window.Close();
        }
    }
}
