using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Avalonia.Help;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AppAssetTests
{
    [AvaloniaFact]
    public void AboutDialogCanBeCreatedWithBundledIcon()
    {
        var dialog = new AboutDialogWindow();

        Assert.Equal("About FT-ITC Analysis", dialog.Title);
    }

    [AvaloniaFact]
    public void UnifiedHelpResourceIsBundledAndLoadable()
    {
        var text = AvaloniaHelpResourceLoader.LoadText("HelpTextResource.txt");

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("##", text);
        Assert.Single(typeof(MainWindow).Assembly.GetManifestResourceNames(),
            name => name.EndsWith("HelpTextResource.txt") || name.EndsWith("ScienceHelpResource.txt"));
    }
}
