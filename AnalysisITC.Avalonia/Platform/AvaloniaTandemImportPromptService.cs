using System;
using System.Globalization;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Processing;
using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaTandemImportPromptService : ITandemImportPromptService
    {
        public TandemConcatenation.BackMixingSettings AskBackMixingSettings(
            string fileName,
            int segmentCount,
            TandemConcatenation.BackMixingSettings defaults)
        {
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));

            var owner = GetMainWindow();
            if (owner == null) return defaults;

            if (Dispatcher.UIThread.CheckAccess())
                return ShowPrompt(owner, fileName, segmentCount, defaults);

            return Dispatcher.UIThread.Invoke(() => ShowPrompt(owner, fileName, segmentCount, defaults));
        }

        static TandemConcatenation.BackMixingSettings ShowPrompt(
            Window owner,
            string fileName,
            int segmentCount,
            TandemConcatenation.BackMixingSettings defaults)
        {
            var dialog = new TandemImportPromptWindow(fileName, segmentCount, defaults);
            var task = dialog.ShowDialog<TandemConcatenation.BackMixingSettings?>(owner);
            var frame = new DispatcherFrame();

            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
            Dispatcher.UIThread.PushFrame(frame);

            return task.IsCompletedSuccessfully
                ? task.Result ?? defaults
                : defaults;
        }

        static Window? GetMainWindow()
        {
            return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        }

        sealed class TandemImportPromptWindow : Window
        {
            readonly TextBox deadVolumeBox;
            readonly TextBox mixingFractionBox;
            readonly CheckBox removeOverflowCheck;
            readonly TextBlock errorText;
            readonly TandemConcatenation.BackMixingSettings defaults;

            public TandemImportPromptWindow(
                string fileName,
                int segmentCount,
                TandemConcatenation.BackMixingSettings defaults)
            {
                this.defaults = defaults;

                Title = "Tandem ITC File Detected";
                Width = 520;
                Height = 360;
                MinWidth = 460;
                MinHeight = 330;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                CanResize = false;

                var titleText = new TextBlock
                {
                    Text = "Tandem ITC File Detected",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                AppTheme.Bind(titleText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

                var messageText = new TextBlock
                {
                    Text = BuildMessage(fileName, segmentCount),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                AppTheme.Bind(messageText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

                deadVolumeBox = new TextBox
                {
                    Text = (defaults.DeadVolume * 1e6).ToString("G4", CultureInfo.InvariantCulture),
                    MinWidth = 120
                };
                mixingFractionBox = new TextBox
                {
                    Text = "20",
                    MinWidth = 120
                };
                removeOverflowCheck = new CheckBox
                {
                    Content = "Remove overflow between segments",
                    IsChecked = defaults.DidRemoveOverflow,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                errorText = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    IsVisible = false,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                AppTheme.Bind(errorText, TextBlock.ForegroundProperty, AppTheme.StatusError);

                var fields = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        FieldRow("Dead volume (uL)", deadVolumeBox),
                        FieldRow("Mixing fraction (%)", mixingFractionBox),
                        removeOverflowCheck,
                        errorText
                    }
                };

                var microCal = DialogButton("Use MicroCal Concat");
                microCal.Click += (_, _) => Close(defaults);

                var backMixing = DialogButton("Use Back-Mixing Compensation");
                backMixing.Click += (_, _) => TryUseBackMixing();

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { microCal, backMixing }
                };

                var body = new StackPanel
                {
                    Spacing = 0,
                    Children = { titleText, messageText, fields }
                };

                var layout = new Grid
                {
                    RowDefinitions = new RowDefinitions("*,Auto"),
                    RowSpacing = 18
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

            void TryUseBackMixing()
            {
                if (!TryParseDouble(deadVolumeBox.Text, out var deadVolumeMicroliters)
                    || deadVolumeMicroliters <= 0)
                {
                    ShowError("Dead volume must be a positive number in microliters.");
                    return;
                }

                if (!TryParseDouble(mixingFractionBox.Text, out var mixingFractionPercent)
                    || mixingFractionPercent < 0
                    || mixingFractionPercent > 100)
                {
                    ShowError("Mixing fraction must be a number from 0 to 100 percent.");
                    return;
                }

                Close(new TandemConcatenation.BackMixingSettings
                {
                    UseBackMixingMethod = true,
                    DeadVolume = deadVolumeMicroliters * 1e-6,
                    MixingFraction = mixingFractionPercent / 100.0,
                    DidRemoveOverflow = removeOverflowCheck.IsChecked == true,
                    RemoveOverflowVolume = defaults.RemoveOverflowVolume
                });
            }

            void ShowError(string message)
            {
                errorText.Text = message;
                errorText.IsVisible = true;
            }

            static Border FieldRow(string label, Control field)
            {
                var labelText = new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center
                };
                AppTheme.Bind(labelText, TextBlock.ForegroundProperty, AppTheme.SecondaryText);

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("170,*"),
                    ColumnSpacing = 12,
                    Children = { labelText, field }
                };
                Grid.SetColumn(field, 1);

                return new Border
                {
                    Child = row
                };
            }

            static Button DialogButton(string text) => new()
            {
                Content = text,
                MinWidth = 82,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            static string BuildMessage(string fileName, int segmentCount)
            {
                var file = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName);
                var name = file == null ? "The imported file" : $"The file \"{file}\"";
                return $"{name} contains {segmentCount} tandem segments. Choose how to process segment-to-segment concentrations.";
            }

            static bool TryParseDouble(string? text, out double value)
            {
                return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                    || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }
    }
}
