using AnalysisITC.Core.Application;
using AnalysisITC.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Diagnostics;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaAppNotificationService : IAppNotificationService
    {
        public void ShowInfoAlert(string title, string message, bool useLeftAlignedAccessory = false, string? actionUrl = null)
        {
            AppEventHandler.PrintAndLog($"{title}: {message}");
            Dispatcher.UIThread.Post(async () =>
            {
                var owner = GetMainWindow();
                if (owner == null) return;

                var dialog = new InfoAlertWindow(title, message, actionUrl);
                await dialog.ShowDialog(owner);
            });
        }

        static Window? GetMainWindow()
        {
            return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        }

        sealed class InfoAlertWindow : Window
        {
            readonly string? actionUrl;

            public InfoAlertWindow(string title, string message, string? actionUrl)
            {
                this.actionUrl = actionUrl;

                Title = title;
                Width = 500;
                Height = 420;
                MinWidth = 380;
                MinHeight = 260;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                CanResize = false;

                var titleText = new TextBlock
                {
                    Text = title,
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush("#202832"),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var messageText = new TextBlock
                {
                    Text = message ?? "",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("#202832")
                };
                var messageScroll = new ScrollViewer
                {
                    Content = messageText,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };

                var ok = DialogButton("OK");
                ok.Click += (_, _) => Close();

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8
                };

                if (!string.IsNullOrWhiteSpace(actionUrl))
                {
                    var open = DialogButton("Open Releases");
                    open.Click += (_, _) =>
                    {
                        OpenUrl();
                        Close();
                    };
                    buttons.Children.Add(open);
                }

                buttons.Children.Add(ok);

                var layout = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                    RowSpacing = 14
                };
                Grid.SetRow(titleText, 0);
                Grid.SetRow(messageScroll, 1);
                Grid.SetRow(buttons, 2);
                layout.Children.Add(titleText);
                layout.Children.Add(messageScroll);
                layout.Children.Add(buttons);

                Content = new Border
                {
                    Background = Brushes.White,
                    Padding = new Thickness(18),
                    Child = layout
                };
            }

            void OpenUrl()
            {
                if (string.IsNullOrWhiteSpace(actionUrl)) return;

                try
                {
                    Process.Start(new ProcessStartInfo(actionUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    AppEventHandler.AddLog(ex);
                }
            }

            static Button DialogButton(string text) => new()
            {
                Content = text,
                MinWidth = 82,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
        }
    }
}
