using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using AnalysisITC.Platform.Avalonia;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class FtxtcDuplicatePromptTests
{
    public FtxtcDuplicatePromptTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    public async Task KeyboardDefaultsSkipDuplicates(Key key)
    {
        var owner = new Window();
        owner.Show();
        var dialog = Create();
        var result = dialog.ShowDialog<FtxtcDuplicateAction>(owner);
        dialog.CopyButton.Focus();
        dialog.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(result.IsCompletedSuccessfully);
        Assert.Equal(FtxtcDuplicateAction.SkipDuplicates, await result);
        owner.Close();
    }

    [Fact]
    public async Task ClosingSkipsAndExplicitCopyImportsCopies()
    {
        var owner = new Window();
        owner.Show();
        var closed = Create();
        var closedResult = closed.ShowDialog<FtxtcDuplicateAction>(owner);
        closed.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(closedResult.IsCompletedSuccessfully);
        Assert.Equal(FtxtcDuplicateAction.SkipDuplicates, await closedResult);
        var copy = Create();
        var copyResult = copy.ShowDialog<FtxtcDuplicateAction>(owner);
        copy.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(copyResult.IsCompletedSuccessfully);
        Assert.Equal(FtxtcDuplicateAction.ImportCopies, await copyResult);
        Assert.Equal(FtxtcDuplicatePresentation.SkipToolTip, ToolTip.GetTip(copy.SkipButton));
        Assert.Equal(FtxtcDuplicatePresentation.CopyToolTip, ToolTip.GetTip(copy.CopyButton));
        owner.Close();
    }

    static AvaloniaFtxtcDuplicatePromptService.DuplicatePromptWindow Create() => new(
        new FtxtcDuplicateSummary("overlapping.ftxtc", 2, 1, 1, 3, new[] { "Experiment A", "Analysis Result A" }));
}
