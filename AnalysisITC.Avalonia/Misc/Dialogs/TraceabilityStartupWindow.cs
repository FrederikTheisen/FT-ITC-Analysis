using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Application;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class TraceabilityStartupWindow : Window
{
    readonly TextBox nameBox;
    readonly TextBlock errorText;

    TraceabilityStartupWindow()
    {
        Title = "Traceability Mode";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        MinWidth = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        nameBox = new TextBox
        {
            Text = AppSettings.UserName,
            PlaceholderText = "Operator name",
            MinWidth = 280
        };
        errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        AppTheme.Bind(errorText, TextBlock.ForegroundProperty, AppTheme.StatusError);

        var continueButton = new Button { Content = "Continue", MinWidth = 90, IsDefault = true };
        continueButton.Click += (_, _) => Continue();
        var quitButton = new Button { Content = "Quit", MinWidth = 90, IsCancel = true };
        quitButton.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { quitButton, continueButton }
        };
        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Confirm the operator name before opening data. This confirmation appears once each time FT-ITC starts.",
                    TextWrapping = TextWrapping.Wrap
                },
                nameBox,
                errorText,
                buttons
            }
        };
        Content = content;
        AppTheme.Bind(this, BackgroundProperty, AppTheme.WorkspaceBackground);
    }

    public static async Task<bool> ShowAsync(Window owner)
    {
        var dialog = new TraceabilityStartupWindow();
        return await dialog.ShowDialog<bool>(owner);
    }

    void Continue()
    {
        if (!PreferencesState.TryValidateTraceability(true, nameBox.Text ?? "", out var error))
        {
            errorText.Text = error;
            errorText.IsVisible = true;
            nameBox.Focus();
            return;
        }

        AppSettings.UserName = (nameBox.Text ?? "").Trim();
        AppSettings.Save();
        Close(true);
    }
}
