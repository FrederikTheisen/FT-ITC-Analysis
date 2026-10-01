using System;
using System.Collections.Generic;
using System.Globalization;
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
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaImportPromptService : IIntegratedHeatImportPromptService
    {
        static readonly List<EnergyUnit> Units = EnergyUnitAttribute.GetSelectableUnits();
        static int selection;

        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
            => AskForEnergyUnit(fileName, encounteredValue, allowQueueReuse, false, AppSettings.ReprocessIntegratedHeatDataOnLoad, null, false, AppSettings.ReferenceTemperature);

        public EnergyUnitPromptResult AskForEnergyUnit(
            string fileName,
            string encounteredValue,
            bool allowQueueReuse,
            bool showReprocessChoice,
            bool defaultReprocess,
            EnergyUnit? reusedUnit,
            bool showTemperatureInput,
            double defaultTemperature)
        {
            var owner = GetMainWindow();
            if (owner == null)
                return new EnergyUnitPromptResult(
                    EnergyUnitResolver.DefaultUnit(AppSettings.EnergyUnitFamily),
                    false,
                    false,
                    showReprocessChoice ? defaultReprocess : null,
                    showTemperatureInput ? defaultTemperature : null);

            if (Dispatcher.UIThread.CheckAccess())
                return ShowPrompt(owner, fileName, encounteredValue, allowQueueReuse, showReprocessChoice, defaultReprocess, reusedUnit, showTemperatureInput, defaultTemperature);

            return Dispatcher.UIThread.Invoke(() => ShowPrompt(owner, fileName, encounteredValue, allowQueueReuse, showReprocessChoice, defaultReprocess, reusedUnit, showTemperatureInput, defaultTemperature));
        }

        static EnergyUnitPromptResult ShowPrompt(Window owner, string fileName, string encounteredValue, bool allowQueueReuse, bool showReprocessChoice, bool defaultReprocess, EnergyUnit? reusedUnit, bool showTemperatureInput, double defaultTemperature)
        {
            var dialog = new EnergyUnitPromptWindow(Units, selection, fileName, encounteredValue, allowQueueReuse, showReprocessChoice, defaultReprocess, reusedUnit, showTemperatureInput, defaultTemperature);
            var task = dialog.ShowDialog<EnergyUnitPromptWindow.PromptResult?>(owner);
            var frame = new DispatcherFrame();

            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
            Dispatcher.UIThread.PushFrame(frame);

            var result = task.IsCompletedSuccessfully ? task.Result : null;
            if (result == null || result.Value.IsCancelled)
                return new EnergyUnitPromptResult(null, false, true, null);

            selection = result.Value.SelectedIndex;
            var unit = selection >= 0 && selection < Units.Count ? Units[selection] : (EnergyUnit?)null;
            return new EnergyUnitPromptResult(unit, result.Value.UseForRemainingFilesInQueue, false, result.Value.ReprocessIntegratedHeatData, result.Value.Temperature);
        }

        static Window? GetMainWindow()
        {
            return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        }

        internal sealed class EnergyUnitPromptWindow : Window
        {
            readonly ComboBox unitCombo;
            readonly CheckBox? queueCheckbox;
            readonly CheckBox? reprocessCheckbox;
            readonly TextBox? temperatureBox;
            readonly TextBlock? temperatureMessage;
            readonly Button importButton;

            internal TextBox? TemperatureBox => temperatureBox;
            internal TextBlock? TemperatureMessage => temperatureMessage;
            internal Button ImportButton => importButton;

            public readonly struct PromptResult
            {
                public int SelectedIndex { get; }
                public bool UseForRemainingFilesInQueue { get; }
                public bool IsCancelled { get; }
                public bool? ReprocessIntegratedHeatData { get; }
                public double? Temperature { get; }

                public PromptResult(int selectedIndex, bool useForRemainingFilesInQueue, bool isCancelled, bool? reprocessIntegratedHeatData, double? temperature)
                {
                    SelectedIndex = selectedIndex;
                    UseForRemainingFilesInQueue = useForRemainingFilesInQueue;
                    IsCancelled = isCancelled;
                    ReprocessIntegratedHeatData = reprocessIntegratedHeatData;
                    Temperature = temperature;
                }
            }

            public EnergyUnitPromptWindow(
                IReadOnlyList<EnergyUnit> units,
                int selectedIndex,
                string fileName,
                string encounteredValue,
                bool allowQueueReuse,
                bool showReprocessChoice,
                bool defaultReprocess,
                EnergyUnit? reusedUnit,
                bool showTemperatureInput,
                double defaultTemperature)
            {
                var title = showTemperatureInput ? "Import Integrated Heats" : "Select Energy Unit";
                Title = title;
                Width = 460;
                Height = (allowQueueReuse ? 295 : 250) + (showReprocessChoice ? 38 : 0) + (showTemperatureInput ? 84 : 0);
                MinWidth = 420;
                MinHeight = (allowQueueReuse ? 270 : 230) + (showReprocessChoice ? 38 : 0) + (showTemperatureInput ? 84 : 0);
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                CanResize = false;

                var titleText = new TextBlock
                {
                    Text = title,
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                AppTheme.Bind(titleText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

                var messageText = new TextBlock
                {
                    Text = BuildMessage(fileName, encounteredValue, showTemperatureInput),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                AppTheme.Bind(messageText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

                unitCombo = new ComboBox
                {
                    ItemsSource = units.Select(unit => unit.GetProperties().LongName).ToArray(),
                    SelectedIndex = reusedUnit.HasValue
                        ? units.ToList().IndexOf(reusedUnit.Value)
                        : Math.Clamp(selectedIndex, 0, Math.Max(units.Count - 1, 0)),
                    IsEnabled = !reusedUnit.HasValue,
                    MinWidth = 220,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var unitRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("110,*"),
                    ColumnSpacing = 12,
                    Margin = new Thickness(0, 0, 0, allowQueueReuse || showTemperatureInput ? 12 : 0)
                };

                var unitLabel = new TextBlock
                {
                    Text = reusedUnit.HasValue ? "Energy unit (reused)" : "Energy unit",
                    VerticalAlignment = VerticalAlignment.Center
                };
                AppTheme.Bind(unitLabel, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
                Grid.SetColumn(unitLabel, 0);
                Grid.SetColumn(unitCombo, 1);
                unitRow.Children.Add(unitLabel);
                unitRow.Children.Add(unitCombo);

                Grid? temperatureRow = null;
                if (showTemperatureInput)
                {
                    temperatureBox = new TextBox
                    {
                        Text = defaultTemperature.ToString("G6", CultureInfo.CurrentCulture),
                        MinWidth = 220,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    var temperatureLabel = new TextBlock
                    {
                        Text = "Temperature (°C)",
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    AppTheme.Bind(temperatureLabel, TextBlock.ForegroundProperty, AppTheme.SecondaryText);

                    temperatureMessage = new TextBlock
                    {
                        Text = TemperatureRangeMessage,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        IsVisible = false,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    AppTheme.Bind(temperatureMessage, TextBlock.ForegroundProperty, AppTheme.StatusError);

                    temperatureRow = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("110,*"),
                        RowDefinitions = new RowDefinitions("Auto,Auto"),
                        ColumnSpacing = 12,
                        Margin = new Thickness(0, 0, 0, allowQueueReuse ? 12 : 0)
                    };
                    Grid.SetColumn(temperatureLabel, 0);
                    Grid.SetColumn(temperatureBox, 1);
                    Grid.SetColumn(temperatureMessage, 1);
                    Grid.SetRow(temperatureMessage, 1);
                    temperatureRow.Children.Add(temperatureLabel);
                    temperatureRow.Children.Add(temperatureBox);
                    temperatureRow.Children.Add(temperatureMessage);
                }

                queueCheckbox = allowQueueReuse
                    ? new CheckBox
                    {
                        Content = "Use selected action for remaining files",
                        HorizontalAlignment = HorizontalAlignment.Left
                    }
                    : null;

                importButton = DialogButton("Import");
                importButton.Click += (_, _) =>
                {
                    double? temperature = null;
                    if (temperatureBox != null)
                    {
                        if (!IntegratedHeatReader.TryParseImportTemperature(temperatureBox.Text, out var celsius))
                        {
                            UpdateTemperatureValidity();
                            return;
                        }
                        temperature = celsius;
                    }

                    Close(new PromptResult(
                        unitCombo.SelectedIndex,
                        queueCheckbox?.IsChecked == true,
                        false,
                        reprocessCheckbox == null ? null : reprocessCheckbox.IsChecked == true,
                        temperature));
                };

                var cancel = DialogButton("Cancel");
                cancel.Click += (_, _) => Close(new PromptResult(-1, false, true, null, null));

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, importButton }
                };

                var body = new StackPanel
                {
                    Spacing = 0,
                    Children = { titleText, messageText, unitRow }
                };

                if (temperatureRow != null)
                    body.Children.Add(temperatureRow);

                if (queueCheckbox != null)
                    body.Children.Add(queueCheckbox);

                reprocessCheckbox = showReprocessChoice
                    ? new CheckBox
                    {
                        Content = "Recalculate concentrations and ratios",
                        IsChecked = defaultReprocess,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, allowQueueReuse ? 4 : 10, 0, 0)
                    }
                    : null;
                if (reprocessCheckbox != null)
                    body.Children.Add(reprocessCheckbox);

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

                if (temperatureBox != null)
                {
                    temperatureBox.TextChanged += (_, _) => UpdateTemperatureValidity();
                    UpdateTemperatureValidity();
                }
            }

            static string TemperatureRangeMessage =>
                $"Enter the temperature in °C, between {IntegratedHeatReader.MinimumImportTemperature.ToString(CultureInfo.CurrentCulture)} and {IntegratedHeatReader.MaximumImportTemperature.ToString(CultureInfo.CurrentCulture)}.";

            void UpdateTemperatureValidity()
            {
                if (temperatureBox == null) return;

                var valid = IntegratedHeatReader.TryParseImportTemperature(temperatureBox.Text, out _);
                importButton.IsEnabled = valid;
                if (temperatureMessage != null)
                    temperatureMessage.IsVisible = !valid;
            }

            static string BuildMessage(string fileName, string encounteredValue, bool showTemperatureInput)
            {
                var file = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName);
                var missing = showTemperatureInput
                    ? "the energy unit or the experiment temperature"
                    : "the energy unit";
                var action = showTemperatureInput
                    ? "Choose the unit used in the file and enter the temperature of the experiment."
                    : "Choose the unit used in the file.";
                var message = file == null
                    ? $"The imported file does not specify {missing}. {action}"
                    : $"The imported file \"{file}\" does not specify {missing}. {action}";

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
