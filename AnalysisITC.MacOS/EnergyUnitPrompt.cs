using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AppKit;
using CoreGraphics;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS
{
    public static class EnergyUnitPrompt
    {
        static int selection = 0;

        static List<EnergyUnit> Units { get; } = EnergyUnitAttribute.GetSelectableUnits();

        public readonly struct PromptResult
        {
            public EnergyUnit? Unit { get; }
            public bool UseForRemainingFilesInQueue { get; }
            public bool IsCancelled { get; }
            public bool? ReprocessIntegratedHeatData { get; }
            public double? Temperature { get; }

            public PromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled, bool? reprocessIntegratedHeatData = null, double? temperature = null)
            {
                Unit = unit;
                UseForRemainingFilesInQueue = useForRemainingFilesInQueue;
                IsCancelled = isCancelled;
                ReprocessIntegratedHeatData = reprocessIntegratedHeatData;
                Temperature = temperature;
            }
        }

        public static PromptResult AskForEnergyUnit(
            NSWindow parentWindow = null,
            string fileName = null,
            string encounteredvalue = null,
            bool allowQueueReuse = false)
            => AskForEnergyUnit(parentWindow, fileName, encounteredvalue, allowQueueReuse, false, false, null, false, AppSettings.ReferenceTemperature);

        public static PromptResult AskForEnergyUnit(
            NSWindow parentWindow,
            string fileName,
            string encounteredvalue,
            bool allowQueueReuse,
            bool showReprocessChoice,
            bool defaultReprocess,
            EnergyUnit? reusedUnit,
            bool showTemperatureInput,
            double defaultTemperature)
        {
            // State carried over when the prompt is shown again after an invalid temperature.
            var unitSelection = reusedUnit.HasValue ? Units.IndexOf(reusedUnit.Value) : selection;
            var useForQueue = false;
            var reprocess = defaultReprocess;
            var temperatureText = defaultTemperature.ToString("G6", CultureInfo.CurrentCulture);
            var invalidTemperature = false;

            while (true)
            {
                var popup = new NSPopUpButton(new CGRect(0, 0, 220, 26), false);

                foreach (var unit in Units)
                {
                    popup.AddItem(unit.GetProperties().LongName);
                }

                popup.SelectItem(unitSelection);
                popup.Enabled = !reusedUnit.HasValue;

                NSButton queueCheckbox = null;
                var temperatureRowHeight = showTemperatureInput ? 30 : 0;
                var accessoryView = new NSView(new CGRect(0, 0, 340, (allowQueueReuse ? 54 : 28) + (showReprocessChoice ? 26 : 0) + temperatureRowHeight));
                popup.Frame = new CGRect(60, accessoryView.Frame.Height - 28, 220, 26);
                accessoryView.AddSubview(popup);

                NSTextField temperatureField = null;
                if (showTemperatureInput)
                {
                    var rowY = accessoryView.Frame.Height - 58;
                    var temperatureLabel = NSTextField.CreateLabel("Temperature (°C)");
                    temperatureLabel.Frame = new CGRect(60, rowY + 4, 120, 17);
                    accessoryView.AddSubview(temperatureLabel);

                    temperatureField = new NSTextField(new CGRect(184, rowY, 96, 24))
                    {
                        StringValue = temperatureText,
                    };
                    accessoryView.AddSubview(temperatureField);
                }

                if (allowQueueReuse)
                {
                    queueCheckbox = new NSButton(new CGRect(30, 2 + (showReprocessChoice ? 24 : 0), 280, 18))
                    {
                        Title = "Use selected action for remaining files",
                        State = useForQueue ? NSCellStateValue.On : NSCellStateValue.Off,
                        ControlSize = NSControlSize.Small,
                        Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)
                    };
                    queueCheckbox.SetButtonType(NSButtonType.Switch);
                    queueCheckbox.SizeToFit();
                    accessoryView.AddSubview(queueCheckbox);
                }

                NSButton reprocessCheckbox = null;
                if (showReprocessChoice)
                {
                    reprocessCheckbox = new NSButton(new CGRect(30, 2, 290, 18))
                    {
                        Title = "Recalculate concentrations and ratios",
                        State = reprocess ? NSCellStateValue.On : NSCellStateValue.Off,
                        ControlSize = NSControlSize.Small,
                        Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)
                    };
                    reprocessCheckbox.SetButtonType(NSButtonType.Switch);
                    accessoryView.AddSubview(reprocessCheckbox);
                }

                var missing = showTemperatureInput ? "the energy unit or the experiment temperature" : "the energy unit";
                var action = showTemperatureInput
                    ? "Choose the unit used in the file and enter the temperature of the experiment."
                    : "Choose the unit used in the file.";
                string informativeText = fileName == null
                    ? $"The imported file does not specify {missing}. {action}"
                    : $"The imported file \"{Path.GetFileName(fileName)}\" does not specify {missing}. {action}";
                if (reusedUnit.HasValue)
                    informativeText = showTemperatureInput
                        ? $"Energy unit {reusedUnit.Value.GetProperties().LongName} is reused from an earlier file. Enter the temperature of the experiment and choose whether to recalculate concentrations and ratios for this table."
                        : $"Energy unit {reusedUnit.Value.GetProperties().LongName} is reused from an earlier file. Choose whether to recalculate concentrations and ratios for this table.";

                if (encounteredvalue != null)
                {
                    informativeText += $"\n\nMax Absolute Value: {encounteredvalue}";
                }

                if (invalidTemperature)
                {
                    informativeText = $"Enter the temperature in °C, between {IntegratedHeatReader.MinimumImportTemperature.ToString(CultureInfo.CurrentCulture)} and {IntegratedHeatReader.MaximumImportTemperature.ToString(CultureInfo.CurrentCulture)}.\n\n" + informativeText;
                }

                var alert = new NSAlert
                {
                    AlertStyle = invalidTemperature ? NSAlertStyle.Warning : NSAlertStyle.Informational,
                    MessageText = reusedUnit.HasValue || showTemperatureInput ? "Import Integrated Heats" : "Select Energy Unit",
                    InformativeText = informativeText,
                    AccessoryView = accessoryView
                };

                alert.AddButton("Import");
                alert.AddButton("Cancel");
                alert.Layout();

                // Synchronous prompt: easiest when the reader needs an answer immediately.
                var result = alert.RunModal();

                // First added button is the default button.
                if ((int)result != 1000)
                {
                    return new PromptResult(null, false, true);
                }

                unitSelection = (int)popup.IndexOfSelectedItem;
                useForQueue = queueCheckbox != null && queueCheckbox.State == NSCellStateValue.On;
                reprocess = reprocessCheckbox != null && reprocessCheckbox.State == NSCellStateValue.On;

                double? temperature = null;
                if (temperatureField != null)
                {
                    temperatureText = temperatureField.StringValue;
                    if (!IntegratedHeatReader.TryParseImportTemperature(temperatureText, out var celsius))
                    {
                        invalidTemperature = true;
                        continue;
                    }
                    temperature = celsius;
                }

                selection = unitSelection;
                EnergyUnit? selectedUnit = selection < Units.Count ? Units[selection] : (EnergyUnit?)null;

                return new PromptResult(selectedUnit, useForQueue, false,
                    reprocessCheckbox == null ? (bool?)null : reprocess,
                    temperature);
            }
        }
    }
}
