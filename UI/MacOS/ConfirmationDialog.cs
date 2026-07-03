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
    public static class ConfirmationDialog
    {
        public static bool ConfirmRemoveOrDelete(string messageText, string informativeText, string destructiveButtonText)
        {
            if (!AppSettings.ConfirmRemoveDelete)
            {
                return true;
            }

            using var alert = new NSAlert
            {
                MessageText = messageText,
                InformativeText = informativeText,
                AlertStyle = NSAlertStyle.Warning
            };

            alert.AddButton("Cancel");
            alert.AddButton(destructiveButtonText);
            alert.Buttons[1].HasDestructiveAction = true;
            alert.ShowsSuppressionButton = true;
            alert.SuppressionButton.Title = "Do not show this again";

            var confirmed = alert.RunModal() == 1001;
            if (confirmed && alert.SuppressionButton.State == NSCellStateValue.On)
            {
                AppSettings.ConfirmRemoveDelete = false;
                AppSettings.Save();
            }

            return confirmed;
        }
    }
}
