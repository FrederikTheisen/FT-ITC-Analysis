using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using Xunit;

using AnalysisITC.Avalonia.Styling;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class UiTypographyTests
{
    public UiTypographyTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BodyWeightIsMediumOnlyOnWindows(bool isWindows)
    {
        var expected = isWindows ? FontWeight.Medium : FontWeight.Normal;

        Assert.Equal(expected, AppTheme.BodyFontWeightFor(isWindows));
    }

    [Fact]
    public void TypographyResourceUsesWindowsBodyWeight()
    {
        var resources = new ResourceDictionary();

        AppTheme.RegisterUiTypography(resources, isWindows: true);

        Assert.Equal(FontWeight.Medium, resources[AppTheme.UiBodyFontWeight]);
    }

    [Fact]
    public void BodyWeightIsInheritedWithoutReplacingExplicitEmphasis()
    {
        var body = new TextBlock { Text = "Body" };
        var heading = new TextBlock
        {
            Text = "Heading",
            FontWeight = FontWeight.SemiBold
        };
        var content = new StackPanel
        {
            Children = { body, heading }
        };
        var window = new Window
        {
            FontWeight = AppTheme.BodyFontWeightFor(isWindows: true),
            Content = content
        };

        window.Show();
        try
        {
            Assert.Equal(FontWeight.Medium, body.FontWeight);
            Assert.Equal(FontWeight.SemiBold, heading.FontWeight);
        }
        finally
        {
            window.Close();
        }
    }
}
