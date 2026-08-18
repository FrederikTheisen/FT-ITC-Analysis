using Avalonia.Automation;
using Avalonia.Controls;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class StatusAccessibilityTests
{
    public StatusAccessibilityTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void PrimaryStatusUsesPoliteLiveAnnouncements()
    {
        var window = new MainWindow();
        window.Show();

        try
        {
            var status = window.FindControl<TextBlock>("StatusText");

            Assert.NotNull(status);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        }
        finally
        {
            window.Close();
        }
    }
}
