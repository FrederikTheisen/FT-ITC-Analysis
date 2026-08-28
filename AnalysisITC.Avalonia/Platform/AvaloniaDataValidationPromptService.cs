using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia;

public sealed class AvaloniaDataValidationPromptService : IDataValidationPromptService
{
    public DataValidationPromptResult AskValidationIssue(
        string title,
        string message,
        bool canFix,
        bool requiresInput,
        bool allowKeep = true)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() =>
                AskValidationIssue(title, message, canFix, requiresInput, allowKeep));

        var owner = GetMainWindow();
        if (owner == null)
            return new DataValidationPromptResult(DataValidationPromptAction.Discard);

        var dialog = new ValidationPromptWindow(title, message, canFix, requiresInput, allowKeep);
        var task = dialog.ShowDialog<ValidationPromptResult?>(owner);
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
        Dispatcher.UIThread.PushFrame(frame);

        var result = task.IsCompletedSuccessfully ? task.Result : null;
        return result?.ToCoreResult() ?? new DataValidationPromptResult(DataValidationPromptAction.Discard);
    }

    static Window? GetMainWindow()
    {
        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }

    readonly struct ValidationPromptResult
    {
        public ValidationPromptResult(DataValidationPromptAction action, string? input)
        {
            Action = action;
            Input = input;
        }

        public DataValidationPromptAction Action { get; }
        public string? Input { get; }

        public DataValidationPromptResult ToCoreResult() => new(Action, Input);
    }

    internal sealed class ValidationPromptWindow : Window
    {
        readonly TextBox? input;
        readonly List<Button> actionButtons = new();

        public ValidationPromptWindow(
            string title,
            string message,
            bool canFix,
            bool requiresInput,
            bool allowKeep = true)
        {
            Title = title;
            Width = 500;
            Height = requiresInput ? 330 : 270;
            MinWidth = 420;
            MinHeight = requiresInput ? 300 : 240;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold
            };
            AppTheme.Bind(titleText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

            var messageText = new TextBlock
            {
                Text = message ?? "",
                TextWrapping = TextWrapping.Wrap
            };
            AppTheme.Bind(messageText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

            var body = new StackPanel
            {
                Spacing = 12,
                Children = { titleText, messageText }
            };

            if (requiresInput)
            {
                input = new TextBox
                {
                    PlaceholderText = "Updated syringe concentration",
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                body.Children.Add(input);
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            if (allowKeep)
            {
                AddButton(buttons, "Keep", DataValidationPromptAction.Keep);
                AddButton(buttons, "Discard", DataValidationPromptAction.Discard);
                if (canFix) AddButton(buttons, "Attempt Fix", DataValidationPromptAction.AttemptFix);
            }
            else
            {
                AddButton(buttons, "Cancel", DataValidationPromptAction.Discard);
                if (canFix) AddButton(buttons, "Import", DataValidationPromptAction.AttemptFix);
            }

            var layout = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                RowSpacing = 16
            };
            Grid.SetRow(body, 0);
            Grid.SetRow(buttons, 1);
            layout.Children.Add(body);
            layout.Children.Add(buttons);

            var border = new Border
            {
                Padding = new Thickness(18),
                Child = layout
            };
            AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
            Content = border;
        }

        internal IReadOnlyList<Button> ActionButtons => actionButtons;

        void AddButton(StackPanel buttons, string text, DataValidationPromptAction action)
        {
            var button = new Button { Content = text, MinWidth = 82 };
            button.Click += (_, _) => Close(new ValidationPromptResult(action, input?.Text));
            buttons.Children.Add(button);
            actionButtons.Add(button);
        }
    }
}
