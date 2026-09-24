using System;
using System.Collections.Generic;
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

            public PromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled, bool? reprocessIntegratedHeatData = null)
            {
                Unit = unit;
                UseForRemainingFilesInQueue = useForRemainingFilesInQueue;
                IsCancelled = isCancelled;
                ReprocessIntegratedHeatData = reprocessIntegratedHeatData;
            }
        }

        public static PromptResult AskForEnergyUnit(
            NSWindow parentWindow = null,
            string fileName = null,
            string encounteredvalue = null,
            bool allowQueueReuse = false)
            => AskForEnergyUnit(parentWindow, fileName, encounteredvalue, allowQueueReuse, false, false, null);

        public static PromptResult AskForEnergyUnit(
            NSWindow parentWindow,
            string fileName,
            string encounteredvalue,
            bool allowQueueReuse,
            bool showReprocessChoice,
            bool defaultReprocess,
            EnergyUnit? reusedUnit)
        {
            var popup = new NSPopUpButton(new CGRect(0, 0, 220, 26), false);

            foreach (var unit in Units)
            {
                popup.AddItem(unit.GetProperties().LongName);
            }

            popup.SelectItem(reusedUnit.HasValue ? Units.IndexOf(reusedUnit.Value) : selection);
            popup.Enabled = !reusedUnit.HasValue;

            NSButton queueCheckbox = null;
            var accessoryView = new NSView(new CGRect(0, 0, 340, (allowQueueReuse ? 54 : 28) + (showReprocessChoice ? 26 : 0)));
            popup.Frame = new CGRect(60, accessoryView.Frame.Height - 28, 220, 26);
            accessoryView.AddSubview(popup);

            if (allowQueueReuse)
            {
                queueCheckbox = new NSButton(new CGRect(30, 2 + (showReprocessChoice ? 24 : 0), 280, 18))
                {
                    Title = "Use selected action for remaining files",
                    State = NSCellStateValue.Off,
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
                    State = defaultReprocess ? NSCellStateValue.On : NSCellStateValue.Off,
                    ControlSize = NSControlSize.Small,
                    Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)
                };
                reprocessCheckbox.SetButtonType(NSButtonType.Switch);
                accessoryView.AddSubview(reprocessCheckbox);
            }

            string informativeText = fileName == null
                ? "The imported file does not specify the energy unit. Choose the unit used in the file."
                : $"The imported file \"{Path.GetFileName(fileName)}\" does not specify the energy unit. Choose the unit used in the file.";
            if (reusedUnit.HasValue)
                informativeText = $"Energy unit {reusedUnit.Value.GetProperties().LongName} is reused from an earlier file. Choose whether to recalculate concentrations and ratios for this table.";

            if (encounteredvalue != null)
            {
                informativeText += $"\n\nMax Absolute Value: {encounteredvalue}";
            }

            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = reusedUnit.HasValue ? "Import Integrated Heats" : "Select Energy Unit",
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

            selection = (int)popup.IndexOfSelectedItem;
            EnergyUnit? selectedUnit = selection < Units.Count ? Units[selection] : (EnergyUnit?)null;
            var useForQueue = queueCheckbox != null && queueCheckbox.State == NSCellStateValue.On;

            return new PromptResult(selectedUnit, useForQueue, false,
                reprocessCheckbox == null ? null : reprocessCheckbox.State == NSCellStateValue.On);
        }
    }
}
