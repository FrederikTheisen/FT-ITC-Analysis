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

    [AvaloniaTheory]
    [InlineData("HelpTextResource.txt")]
    [InlineData("ScienceHelpResource.txt")]
    public void HelpDocumentsAreBundledAndLoadable(string resourceName)
    {
        var text = AvaloniaHelpResourceLoader.LoadText(resourceName);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("##", text);
    }
}
