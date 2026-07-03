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
            var storyboard = NSStoryboard.FromName("Main", null);
            var viewController = (ExportAccessoryViewController)storyboard.InstantiateControllerWithIdentifier("ExportAccessoryViewController");
            viewController.Setup(settings);

            var dlg = NSOpenPanel.OpenPanel;
            dlg.Title = "Export";
            dlg.AccessoryView = viewController.View;
            dlg.RespondsToSelector(new ObjCRuntime.Selector("setAccessoryViewDisclosed:"));
            dlg.PerformSelector(new ObjCRuntime.Selector("setAccessoryViewDisclosed:"), NSNumber.FromBoolean(true), 0);
            dlg.CanChooseDirectories = true;
            dlg.CanChooseFiles = false;
            dlg.AllowsMultipleSelection = false;
            dlg.CanCreateDirectories = true;
            dlg.Prompt = "Export";

            dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, result =>
            {
                tcs.TrySetResult(result == (int)NSModalResponse.OK ? dlg.Url?.Path : null);
            });

            return tcs.Task;
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
