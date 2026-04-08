using System;
using System.Linq;
using AppKit;

namespace AnalysisITC.GUI.MacOS
{
    public static class StoichiometryPopupBuilder
    {
        public static void Populate(NSPopUpButton popup)
        {
            popup.RemoveAllItems();
            popup.AddItems(StoichiometryOptions.Presets.Select(x => x.Title).ToArray());
        }

        public static void Select(NSPopUpButton popup, double factor, double tolerance = 1e-9)
        {
            var selected = StoichiometryOptions.GetClosest(factor, tolerance);
            var index = StoichiometryOptions.Presets
                .ToList()
                .FindIndex(x => x.Preset == selected.Preset);

            if (index >= 0)
                popup.SelectItem(index);
        }

        public static StoichiometryOption GetSelected(NSPopUpButton popup)
        {
            var idx = (int)popup.IndexOfSelectedItem;

            if (idx < 0 || idx >= StoichiometryOptions.Presets.Count)
                return StoichiometryOptions.Default;

            return StoichiometryOptions.Presets[idx];
        }
    }
}