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

        public static EnergyUnit? AskForEnergyUnit(NSWindow parentWindow = null, string fileName = null, string encounteredvalue = null)
        {
            var popup = new NSPopUpButton(new CGRect(0, 0, 220, 26), false);

            foreach (var unit in Units) popup.AddItem(unit.GetProperties().LongName);

            popup.SelectItem(selection);

            string informativetext = fileName == null
                    ? "The imported file does not specify the heat unit. Choose the unit used in the file."
                    : $"The imported file \"{Path.GetFileName(fileName)}\" does not specify the heat unit. Choose the unit used in the file.";

            if (encounteredvalue != null) informativetext += $"\n\nMax Absolute Value: {encounteredvalue}";

            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = "Select Energy Unit",
                InformativeText = informativetext,
                AccessoryView = popup
            };

            alert.AddButton("Import");
            alert.AddButton("Cancel");
            alert.Layout();

            // Synchronous prompt: easiest when the reader needs an answer immediately.
            var result = alert.RunModal();

            // First added button is the default button.
            if ((int)result != 1000)
                return null;

            selection = (int)popup.IndexOfSelectedItem;

            if (selection < Units.Count) return Units[selection];
            else return null;
        }
    }
}

