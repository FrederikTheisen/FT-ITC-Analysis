using System.Linq;

using AnalysisITC.Platform.Avalonia;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class DataValidationPromptServiceTests
{
    [AvaloniaFact]
    public void IncompleteResponseOffersKeepAndDiscardWithoutRepair()
    {
        var window = new AvaloniaDataValidationPromptService.ValidationPromptWindow(
            "Incomplete response", "The recorded response may be incomplete.",
            canFix: false, requiresInput: false);

        Assert.Equal(new[] { "Keep", "Discard" },
            window.ActionButtons.Select(button => button.Content?.ToString()).ToArray());
    }

    [AvaloniaFact]
    public void RejectedRawImportOffersOnlyCancel()
    {
        var window = new AvaloniaDataValidationPromptService.ValidationPromptWindow(
            "Incomplete thermogram", "A recorded injection starts outside the thermogram.",
            canFix: false, requiresInput: false, allowKeep: false);

        Assert.Equal(new[] { "Cancel" },
            window.ActionButtons.Select(button => button.Content?.ToString()).ToArray());
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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
