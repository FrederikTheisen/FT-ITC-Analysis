using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Platform;
using AppKit;
using Foundation;

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
    public sealed class MacExportPromptService : IExportPromptService
    {
        public Task<string> ChooseExportFolderAsync(ExportAccessoryViewSettings settings)
        {
            var tcs = new TaskCompletionSource<string>();
            var parent = NSApplication.SharedApplication.MainWindow;
            var presenter = parent?.ContentViewController;
            if (parent == null || presenter == null)
                return Task.FromResult<string>(null);

            ExportSheetViewController sheet = null;
            sheet = new ExportSheetViewController(settings, accepted =>
            {
                presenter.DismissViewController(sheet);
                if (!accepted)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                NSApplication.SharedApplication.BeginInvokeOnMainThread(() => ChooseFolder(parent, tcs));
            });
            presenter.PresentViewControllerAsSheet(sheet);

            return tcs.Task;
        }

        static void ChooseFolder(NSWindow parent, TaskCompletionSource<string> completion)
        {
            var panel = NSOpenPanel.OpenPanel;
            panel.Title = "Choose Export Folder";
            panel.CanChooseDirectories = true;
            panel.CanChooseFiles = false;
            panel.AllowsMultipleSelection = false;
            panel.CanCreateDirectories = true;
            panel.Prompt = "Export";
            panel.BeginSheet(parent, result =>
                completion.TrySetResult(result == (int)NSModalResponse.OK ? panel.Url?.Path : null));
        }

        public bool ConfirmOverwrite(IEnumerable<string> outputPaths)
        {
            var existing = outputPaths.Where(File.Exists).Distinct().ToList();
            if (existing.Count == 0) return true;

            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Warning,
                MessageText = "File already exists.",
                InformativeText = existing.Count == 1
                    ? $"This export will overwrite:\n{Path.GetFileName(existing[0])}"
                    : $"This export will overwrite {existing.Count} files."
            };

            alert.AddButton("Overwrite");
            alert.AddButton("Cancel");

            var parent = NSApplication.SharedApplication.MainWindow;
            var response = parent != null ? alert.RunSheetModal(parent) : alert.RunModal();
            return response == (int)NSAlertButtonReturn.First;
        }
    }
}
