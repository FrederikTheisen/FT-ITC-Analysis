using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Tools
{
    public sealed class AnalysisResultExporterWindow : Window
    {
        readonly ListBox resultList = new ListBox { SelectionMode = SelectionMode.Multiple };
        readonly ComboBox rowModeCombo = Combo(new[] { "Summary rows", "All replicate rows" });
        readonly ComboBox errorStyleCombo = Combo(new[] { "Value with error", "Separate columns" });
        readonly ComboBox uncertaintyCombo = Combo(new[] { "SD", "CI", "SD + CI" });
        readonly ComboBox formatCombo = Combo(new[] { "CSV", "TSV" });
        readonly ComboBox temperatureCombo = Combo(new[] { "Celsius", "Kelvin" });
        readonly TextBlock statusText = Text();

        public AnalysisResultExporterWindow()
        {
            Title = "Analysis Result Exporter";
            Width = 740;
            Height = 560;
            MinWidth = 620;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = WorkspaceBackgroundBrush;

            BuildLayout();
            PopulateResults();
        }

        void BuildLayout()
        {
            var copyButton = Button("Copy", 82);
            copyButton.Click += (_, _) => CopyToClipboard();

            var exportButton = Button("Export...", 96);
            exportButton.Click += async (_, _) => await ExportToFileAsync();

            resultList.ItemTemplate = new FuncDataTemplate<AnalysisResult>((result, _) => ResultCell(result));

            var optionsPanel = InspectorPanel();
            optionsPanel.Children.Add(Section("Rows", Labeled("Mode", rowModeCombo)));
            optionsPanel.Children.Add(Section("Uncertainty",
                Labeled("Errors", errorStyleCombo),
                Labeled("Style", uncertaintyCombo)));
            optionsPanel.Children.Add(Section("Format",
                Labeled("File", formatCombo),
                Labeled("Temperature", temperatureCombo)));

            Content = WorkspaceControlBuilder.Workspace(
                ContentBorder(resultList),
                Scroll(optionsPanel),
                InspectorFooter(Section("Export",
                    Row(copyButton, exportButton),
                    statusText)),
                useOuterMargin: true);
        }

        void PopulateResults()
        {
            var results = DataManager.Results;
            resultList.ItemsSource = results;
            var selectedItems = resultList.SelectedItems;
            if (selectedItems == null) return;

            var selected = DataManager.SelectedResult;
            if (selected != null && results.Contains(selected))
            {
                selectedItems.Add(selected);
            }
            else
            {
                foreach (var result in results)
                    selectedItems.Add(result);
            }
        }

        Control ResultCell(AnalysisResult? result)
        {
            if (result == null) return Text();

            var count = result.Solution?.Solutions?.Count ?? 0;
            var model = result.Model?.ModelType.GetProperties().Name ?? "";
            var details = $"{result.Date:g} | {model} | {count} experiment" + (count == 1 ? "" : "s");

            var panel = new StackPanel { Spacing = 1, Margin = new Thickness(8, 5) };
            panel.Children.Add(new TextBlock
            {
                Text = result.Name,
                FontWeight = FontWeight.SemiBold,
                Foreground = SectionHeaderBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            panel.Children.Add(new TextBlock
            {
                Text = details,
                FontSize = 12,
                Foreground = LabelBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            panel.Children.Add(new TextBlock
            {
                Text = result.GetListDescriptionString(),
                FontSize = 12,
                Foreground = TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            return panel;
        }

        List<AnalysisResult> SelectedResults()
        {
            return resultList.SelectedItems?
                .OfType<AnalysisResult>()
                .ToList() ?? new List<AnalysisResult>();
        }

        AnalysisResultExportOptions CurrentOptions()
        {
            return new AnalysisResultExportOptions
            {
                RowMode = rowModeCombo.SelectedIndex == 1 ? AnalysisResultExportRowMode.AllRows : AnalysisResultExportRowMode.Summary,
                ErrorStyle = errorStyleCombo.SelectedIndex == 1 ? AnalysisResultExportErrorStyle.SeparateColumns : AnalysisResultExportErrorStyle.ValueWithError,
                UncertaintyDisplayStyle = uncertaintyCombo.SelectedIndex switch
                {
                    1 => UncertaintyDisplayStyle.ConfidenceInterval,
                    2 => UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
                    _ => UncertaintyDisplayStyle.StandardDeviation
                },
                FileFormat = formatCombo.SelectedIndex == 1 ? AnalysisResultExportFileFormat.TSV : AnalysisResultExportFileFormat.CSV,
                EnergyUnit = AppSettings.EnergyUnit,
                UseKelvin = temperatureCombo.SelectedIndex == 1
            };
        }

        string BuildOutput()
        {
            var selected = SelectedResults();
            if (selected.Count == 0)
            {
                SetStatus("Select one or more analysis results.");
                return "";
            }

            return AnalysisResultTableExporter.Build(selected, CurrentOptions());
        }

        void CopyToClipboard()
        {
            var output = BuildOutput();
            if (string.IsNullOrWhiteSpace(output)) return;

            PlatformServices.ClipboardService.SetString(output);
            SetStatus("Analysis result table copied.");
        }

        async Task ExportToFileAsync()
        {
            var output = BuildOutput();
            if (string.IsNullOrWhiteSpace(output)) return;

            var options = CurrentOptions();
            var storage = StorageProvider;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Analysis Results",
                SuggestedFileName = "analysis_results." + options.FileExtension,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(options.FileExtension.ToUpperInvariant())
                    {
                        Patterns = new[] { "*." + options.FileExtension }
                    },
                    FilePickerFileTypes.All
                }
            });

            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            if (string.IsNullOrWhiteSpace(Path.GetExtension(path)))
                path += "." + options.FileExtension;

            await File.WriteAllTextAsync(path, output);
            SetStatus("Analysis result table exported.");
        }

        void SetStatus(string message)
        {
            statusText.Text = message ?? "";
            StatusBarManager.SetStatus(message ?? "", 3000);
        }
    }
}
