using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaImportPromptService : IImportPromptService
    {
        static readonly List<EnergyUnit> Units = EnergyUnitAttribute.GetSelectableUnits();
        static int selection;

        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
        {
            var owner = GetMainWindow();
            if (owner == null)
                return new EnergyUnitPromptResult(AppSettings.EnergyUnit, false, false);

            if (Dispatcher.UIThread.CheckAccess())
                return ShowPrompt(owner, fileName, encounteredValue, allowQueueReuse);

            return Dispatcher.UIThread.Invoke(() => ShowPrompt(owner, fileName, encounteredValue, allowQueueReuse));
        }

        static EnergyUnitPromptResult ShowPrompt(Window owner, string fileName, string encounteredValue, bool allowQueueReuse)
        {
            var dialog = new EnergyUnitPromptWindow(Units, selection, fileName, encounteredValue, allowQueueReuse);
            var task = dialog.ShowDialog<EnergyUnitPromptWindow.PromptResult?>(owner);
            var frame = new DispatcherFrame();

            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
            Dispatcher.UIThread.PushFrame(frame);

            var result = task.IsCompletedSuccessfully ? task.Result : null;
            if (result == null || result.Value.IsCancelled)
                return new EnergyUnitPromptResult(null, false, true);

            selection = result.Value.SelectedIndex;
            var unit = selection >= 0 && selection < Units.Count ? Units[selection] : (EnergyUnit?)null;
            return new EnergyUnitPromptResult(unit, result.Value.UseForRemainingFilesInQueue, false);
        }

        static Window? GetMainWindow()
        {
            return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        }

        sealed class EnergyUnitPromptWindow : Window
        {
            readonly ComboBox unitCombo;
            readonly CheckBox? queueCheckbox;

            public readonly struct PromptResult
            {
                public int SelectedIndex { get; }
                public bool UseForRemainingFilesInQueue { get; }
                public bool IsCancelled { get; }

                public PromptResult(int selectedIndex, bool useForRemainingFilesInQueue, bool isCancelled)
                {
                    SelectedIndex = selectedIndex;
                    UseForRemainingFilesInQueue = useForRemainingFilesInQueue;
                    IsCancelled = isCancelled;
                }
            }

            public EnergyUnitPromptWindow(
                IReadOnlyList<EnergyUnit> units,
                int selectedIndex,
                string fileName,
                string encounteredValue,
                bool allowQueueReuse)
            {
                Title = "Select Energy Unit";
                Width = 460;
                Height = allowQueueReuse ? 295 : 250;
                MinWidth = 420;
                MinHeight = allowQueueReuse ? 270 : 230;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                CanResize = false;

                var titleText = new TextBlock
                {
                    Text = "Select Energy Unit",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                AppTheme.Bind(titleText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

                var messageText = new TextBlock
                {
                    Text = BuildMessage(fileName, encounteredValue),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                AppTheme.Bind(messageText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

                unitCombo = new ComboBox
                {
                    ItemsSource = units.Select(unit => unit.GetProperties().LongName).ToArray(),
                    SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(units.Count - 1, 0)),
                    MinWidth = 220,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var unitRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("110,*"),
                    ColumnSpacing = 12,
                    Margin = new Thickness(0, 0, 0, allowQueueReuse ? 12 : 0)
                };

                var unitLabel = new TextBlock
                {
                    Text = "Energy unit",
                    VerticalAlignment = VerticalAlignment.Center
                };
                AppTheme.Bind(unitLabel, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
                Grid.SetColumn(unitLabel, 0);
                Grid.SetColumn(unitCombo, 1);
                unitRow.Children.Add(unitLabel);
                unitRow.Children.Add(unitCombo);

                queueCheckbox = allowQueueReuse
                    ? new CheckBox
                    {
                        Content = "Use selected action for remaining files",
                        HorizontalAlignment = HorizontalAlignment.Left
                    }
                    : null;

                var import = DialogButton("Import");
                import.Click += (_, _) => Close(new PromptResult(
                    unitCombo.SelectedIndex,
                    queueCheckbox?.IsChecked == true,
                    false));

                var cancel = DialogButton("Cancel");
                cancel.Click += (_, _) => Close(new PromptResult(-1, false, true));

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, import }
                };

                var body = new StackPanel
                {
                    Spacing = 0,
                    Children = { titleText, messageText, unitRow }
                };

                if (queueCheckbox != null)
                    body.Children.Add(queueCheckbox);

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

            static string BuildMessage(string fileName, string encounteredValue)
            {
                var file = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName);
                var message = file == null
                    ? "The imported file does not specify the energy unit. Choose the unit used in the file."
                    : $"The imported file \"{file}\" does not specify the energy unit. Choose the unit used in the file.";

                if (!string.IsNullOrWhiteSpace(encounteredValue))
                    message += $"{Environment.NewLine}{Environment.NewLine}Max Absolute Value: {encounteredValue}";

                return message;
            }

            static Button DialogButton(string text) => new()
            {
                Content = text,
                MinWidth = 82,
                HorizontalAlignment = HorizontalAlignment.Right
            };
        }
    }
}
