using System;
using System.Collections.Generic;
using System.IO;
using AppKit;
using CoreGraphics;

namespace AnalysisITC.GUI.MacOS
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

            public PromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled)
            {
                Unit = unit;
                UseForRemainingFilesInQueue = useForRemainingFilesInQueue;
                IsCancelled = isCancelled;
            }
        }

        public static PromptResult AskForEnergyUnit(
            NSWindow parentWindow = null,
            string fileName = null,
            string encounteredvalue = null,
            bool allowQueueReuse = false)
        {
            var popup = new NSPopUpButton(new CGRect(0, 0, 220, 26), false);

            foreach (var unit in Units)
            {
                popup.AddItem(unit.GetProperties().LongName);
            }

            popup.SelectItem(selection);

            NSButton queueCheckbox = null;
            NSView accessoryView = popup;

            if (allowQueueReuse)
            {
                var container = new NSView(new CGRect(0, 0, 320, 52));
                popup.Frame = new CGRect(50, 26, 220, 26);
                container.AddSubview(popup);

                queueCheckbox = new NSButton(new CGRect(0, 0, 260, 18))
                {
                    Title = "Use selected action for remaining files",
                    State = NSCellStateValue.Off,
                    ControlSize = NSControlSize.Small,
                    Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)
                };
                queueCheckbox.SetButtonType(NSButtonType.Switch);
                queueCheckbox.SizeToFit();
                queueCheckbox.Frame = new CGRect((container.Frame.Width - queueCheckbox.Frame.Width) / 2.0, 0, queueCheckbox.Frame.Width, queueCheckbox.Frame.Height);
                container.AddSubview(queueCheckbox);

                accessoryView = container;
            }

            string informativeText = fileName == null
                ? "The imported file does not specify the heat unit. Choose the unit used in the file."
                : $"The imported file \"{Path.GetFileName(fileName)}\" does not specify the heat unit. Choose the unit used in the file.";

            if (encounteredvalue != null)
            {
                informativeText += $"\n\nMax Absolute Value: {encounteredvalue}";
            }

            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = "Select Energy Unit",
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

            return new PromptResult(selectedUnit, useForQueue, false);
        }
    }
}
