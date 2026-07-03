using AnalysisITC.Platform;
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
    public sealed class MacConfirmationPromptService : IConfirmationPromptService
    {
        public bool ConfirmDestructiveAction(string message, string cancelButton = "Keep", string confirmButton = "Overwrite")
        {
            var alert = new NSAlert
            {
                MessageText = message,
                AlertStyle = NSAlertStyle.Critical
            };

            alert.AddButton(cancelButton);
            alert.AddButton(confirmButton);
            alert.Buttons[1].HasDestructiveAction = true;

            return alert.RunModal() == 1001;
        }
    }
}
