using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using AppKit;
using CoreGraphics;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;

namespace AnalysisITC.UI.MacOS
{
    public sealed class MacAnalysisResultUpdatePromptService : IAnalysisResultUpdatePromptService
    {
        public Task<AnalysisResultUpdateOptions> ChooseOptionsAsync(AnalysisResult result)
        {
            var current = AnalysisResultUpdater.GetEffectiveBootstrapIterations(result);
            var values = new int?[] { null }
                .Concat(AnalysisResultUpdater.GetLargerBootstrapIterationPresets(result).Select(value => (int?)value))
                .ToList();

            var popup = new NSPopUpButton(new CGRect(0, 0, 280, 26), pullsDown: false);
            popup.AddItem($"Stored setting ({current.ToString("N0", CultureInfo.CurrentCulture)})");
            foreach (var value in values.Skip(1))
                popup.AddItem(value.Value.ToString("N0", CultureInfo.CurrentCulture));
            popup.SelectItem(0);

            var informativeText =
                "Rerun the complete stored fit using the current experiment data.\n\n" +
                $"Error method: {result.Solution.ErrorEstimationMethod.Description()}\n" +
                $"Retained refits: {result.Solution.BootstrapIterations.ToString("N0", CultureInfo.CurrentCulture)}\n\n" +
                "Choose the requested bootstrap iterations. Reruns use fresh random streams, so uncertainty values may differ.";

            if (values.Count == 1)
                informativeText += "\n\nNo larger supported bootstrap preset is available.";

            using var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = "Update Analysis Result",
                InformativeText = informativeText,
                AccessoryView = popup,
            };
            alert.AddButton("Update Result");
            alert.AddButton("Cancel");
            alert.Layout();

            var parent = NSApplication.SharedApplication.MainWindow;
            var response = parent != null ? alert.RunSheetModal(parent) : alert.RunModal();
            if (response != (int)NSAlertButtonReturn.First)
                return Task.FromResult<AnalysisResultUpdateOptions>(null);

            var selectedIndex = (int)popup.IndexOfSelectedItem;
            var selected = selectedIndex >= 0 && selectedIndex < values.Count
                ? values[selectedIndex]
                : null;
            return Task.FromResult(new AnalysisResultUpdateOptions(selected));
        }
    }
}
