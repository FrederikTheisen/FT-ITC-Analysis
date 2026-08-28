using System.Linq;

using AnalysisITC.Platform.Avalonia;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class DataValidationPromptServiceTests
{
    public DataValidationPromptServiceTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void RequiredInputPromptOffersImportAndCancelWithoutKeep()
    {
        var window = new AvaloniaDataValidationPromptService.ValidationPromptWindow(
            "Missing concentration",
            "Provide a concentration.",
            canFix: true,
            requiresInput: true,
            allowKeep: false);

        Assert.Equal(
            new[] { "Cancel", "Import" },
            window.ActionButtons.Select(button => button.Content?.ToString()).ToArray());
    }

    [Fact]
    public void OrdinaryValidationPromptRetainsExistingActions()
    {
        var window = new AvaloniaDataValidationPromptService.ValidationPromptWindow(
            "Potential error",
            "Review this import.",
            canFix: true,
            requiresInput: true);

        Assert.Equal(
            new[] { "Keep", "Discard", "Attempt Fix" },
            window.ActionButtons.Select(button => button.Content?.ToString()).ToArray());
    }
}
