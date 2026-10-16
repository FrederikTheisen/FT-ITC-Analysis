using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class DecisionDialogCancellationTests
{
    [AvaloniaTheory]
    [InlineData("SaveChangesDialogWindow", "CloseWindow")]
    [InlineData("SaveChangesDialogWindow", "QuitApplication")]
    [InlineData("SaveChangesDialogWindow", "ClearAllData")]
    [InlineData("ProjectLoadDialogWindow", null)]
    public Task ClosingWithoutResultCancels(string dialogTypeName, string? saveReason) =>
        WithPromptAsync(dialogTypeName, saveReason, dialog => dialog.Close(), "Cancel");

    [AvaloniaTheory]
    [InlineData("SaveChangesDialogWindow", "CloseWindow")]
    [InlineData("SaveChangesDialogWindow", "QuitApplication")]
    [InlineData("SaveChangesDialogWindow", "ClearAllData")]
    [InlineData("ProjectLoadDialogWindow", null)]
    public Task EscapeCancelsWithAnotherButtonFocused(string dialogTypeName, string? saveReason) =>
        WithPromptAsync(dialogTypeName, saveReason, dialog =>
        {
            var otherButton = dialog.GetLogicalDescendants().OfType<Button>()
                .First(button => !Equals(button.Content, "Cancel"));
            Assert.True(otherButton.Focus());
            Assert.True(otherButton.IsFocused);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        }, "Cancel", useTheme: true);

    [AvaloniaTheory]
    [InlineData("SaveChangesDialogWindow", "CloseWindow", "Save", "Save")]
    [InlineData("SaveChangesDialogWindow", "CloseWindow", "Cancel", "Cancel")]
    [InlineData("SaveChangesDialogWindow", "CloseWindow", "Don't Save", "Discard")]
    [InlineData("SaveChangesDialogWindow", "QuitApplication", "Save", "Save")]
    [InlineData("SaveChangesDialogWindow", "QuitApplication", "Cancel", "Cancel")]
    [InlineData("SaveChangesDialogWindow", "QuitApplication", "Don't Save", "Discard")]
    [InlineData("SaveChangesDialogWindow", "ClearAllData", "Save", "Save")]
    [InlineData("SaveChangesDialogWindow", "ClearAllData", "Cancel", "Cancel")]
    [InlineData("SaveChangesDialogWindow", "ClearAllData", "Clear All", "Discard")]
    [InlineData("ProjectLoadDialogWindow", null, "Replace Data", "Replace")]
    [InlineData("ProjectLoadDialogWindow", null, "Append", "Append")]
    [InlineData("ProjectLoadDialogWindow", null, "Cancel", "Cancel")]
    public Task ExplicitButtonsReturnTheirActions(
        string dialogTypeName, string? saveReason, string buttonText, string expectedAction) =>
        WithPromptAsync(dialogTypeName, saveReason, dialog =>
        {
            var button = Assert.Single(dialog.GetLogicalDescendants().OfType<Button>(),
                candidate => Equals(candidate.Content, buttonText));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, expectedAction);

    static async Task WithPromptAsync(
        string dialogTypeName,
        string? saveReason,
        Action<Window> interact,
        string expectedAction,
        bool useTheme = false)
    {
        var application = Application.Current!;
        var theme = useTheme ? new FluentTheme() : null;
        var owner = new Window();
        if (theme != null) application.Styles.Add(theme);

        try
        {
            owner.Show();
            var dialogType = typeof(MainWindow).GetNestedType(dialogTypeName, BindingFlags.NonPublic)!;
            var promptMethod = dialogType.GetMethod("PromptAsync", BindingFlags.Public | BindingFlags.Static)!;
            var arguments = saveReason == null
                ? new object[] { owner }
                : new[]
                {
                    owner,
                    Enum.Parse(typeof(MainWindow).GetNestedType("SavePromptReason", BindingFlags.NonPublic)!, saveReason)
                };
            var prompt = (Task)promptMethod.Invoke(null, arguments)!;
            var dialog = Assert.Single(owner.OwnedWindows);
            Dispatcher.UIThread.RunJobs();
            Assert.False(prompt.IsCompleted);

            interact(dialog);
            Dispatcher.UIThread.RunJobs();
            await prompt.WaitAsync(TimeSpan.FromSeconds(2));

            var actionType = promptMethod.ReturnType.GenericTypeArguments.Single();
            var result = promptMethod.ReturnType.GetProperty("Result")!.GetValue(prompt);
            Assert.Equal(Enum.Parse(actionType, expectedAction), result);
            Assert.False(dialog.IsVisible);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close();
            owner.Close();
            if (theme != null) application.Styles.Remove(theme);
        }
    }
}
