using Avalonia.Controls;
using Avalonia.Threading;

using Xunit;

using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisWorkspaceControlTests
{
    public AnalysisWorkspaceControlTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnlockParametersControlInitializesFromFittingOptions(bool enabled)
    {
        var previous = FittingOptionsController.UnlockBootstrapParameters;
        try
        {
            FittingOptionsController.UnlockBootstrapParameters = enabled;

            Dispatcher.UIThread.Invoke(() =>
            {
                var workspace = new AnalysisWorkspaceControl();

                Assert.Equal(enabled, workspace.UnlockParametersCheck.IsChecked == true);
                Assert.Equal("Unlock parameters", workspace.UnlockParametersCheck.Content);
                Assert.Equal(
                    "Unlock locked parameters during the error estimation pass.",
                    ToolTip.GetTip(workspace.UnlockParametersCheck));
            });
        }
        finally
        {
            FittingOptionsController.UnlockBootstrapParameters = previous;
        }
    }

    [Fact]
    public void ChangingUnlockParametersUpdatesModelCloneDefaults()
    {
        var previous = FittingOptionsController.UnlockBootstrapParameters;
        try
        {
            FittingOptionsController.UnlockBootstrapParameters = false;

            Dispatcher.UIThread.Invoke(() =>
            {
                var workspace = new AnalysisWorkspaceControl();
                workspace.UnlockParametersCheck.IsChecked = true;

                Assert.True(FittingOptionsController.UnlockBootstrapParameters);
                Assert.True(ModelCloneOptions.DefaultOptions.UnlockBootstrapParameters);
            });
        }
        finally
        {
            FittingOptionsController.UnlockBootstrapParameters = previous;
        }
    }
}
