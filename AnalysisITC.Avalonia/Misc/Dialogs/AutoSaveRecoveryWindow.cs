using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Support;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Application;

namespace AnalysisITC.Avalonia.Dialogs;

internal enum AutoSaveRecoveryAction
{
    Recover,
    Discard,
    NotNow
}

internal sealed class AutoSaveRecoveryWindow : Window
{
    public AutoSaveRecoveryWindow(AutoSaveEntry entry, string? errorMessage = null)
    {
        Title = "Recover Autosaved Project";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12
        };

        root.Children.Add(new TextBlock
        {
            Text = "FT-ITC Analysis found an autosave from an interrupted session.",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(errorMessage)
                ? "Recover it as an unsaved project, discard it, or leave it for the next launch."
                : errorMessage,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Project: {entry.SourceProjectName}\nAutosaved: {entry.LastWrittenUtc.ToLocalTime():g}\nFile: {Path.GetFileName(entry.FilePath)}",
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var openFolder = new Button { Content = "Open Folder" };
        openFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(AutoSaveManager.Shared.AutoSaveDirectory);
            ExternalLinkLauncher.TryOpen(AutoSaveManager.Shared.AutoSaveDirectory);
        };
        buttons.Children.Add(openFolder);

        var discard = new Button { Content = "Discard" };
        discard.Click += (_, _) => Close(AutoSaveRecoveryAction.Discard);
        buttons.Children.Add(discard);

        var notNow = new Button { Content = "Not Now" };
        notNow.Click += (_, _) => Close(AutoSaveRecoveryAction.NotNow);
        buttons.Children.Add(notNow);

        var recover = new Button { Content = "Recover" };
        recover.Click += (_, _) => Close(AutoSaveRecoveryAction.Recover);
        buttons.Children.Add(recover);

        root.Children.Add(buttons);
        Content = root;
        AppTheme.Bind(this, BackgroundProperty, AppTheme.WorkspaceBackground);
    }
}
