using System.Reflection;

using Avalonia.Controls;

using AnalysisITC.Avalonia;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class StartupInteractionGuardTests
{
    public StartupInteractionGuardTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void WelcomeAndMenuOpenStayDisabledUntilStartupReady()
    {
        var window = new MainWindow();
        var open = ButtonField(window, "WelcomeOpenButton");
        var reload = ButtonField(window, "WelcomeReloadButton");

        Assert.False(window.CanOpenFiles());
        Assert.False(open.IsEnabled);
        Assert.False(reload.IsEnabled);
        Assert.True(window.OpenFilesFromMenuAsync().IsCompletedSuccessfully);

        window.SetStartupReady();

        Assert.True(window.CanOpenFiles());
        Assert.True(open.IsEnabled);
        window.Close();
    }

    static Button ButtonField(MainWindow window, string name) =>
        (Button)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
}
