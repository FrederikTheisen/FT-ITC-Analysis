using System;
using System.Linq;
using AppKit;

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