using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Core.Export;
using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaExportPromptService : IExportPromptService
    {
        public async Task<string> ChooseExportFolderAsync(ExportAccessoryViewSettings settings)
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return "";

            if (desktop.MainWindow == null || !await ExportDataWindow.ConfigureAsync(desktop.MainWindow, settings))
                return "";

            var storage = desktop.MainWindow?.StorageProvider;
            if (storage == null) return "";

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose Export Folder",
                AllowMultiple = false
            });

            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path);

            return path ?? "";
        }

        public bool ConfirmOverwrite(IEnumerable<string> outputPaths)
        {
            var existing = outputPaths?
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            if (existing.Length == 0) return true;

            bool Confirm()
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                    desktop.MainWindow == null)
                    return false;

                var message = existing.Length == 1
                    ? $"This export will overwrite:\n{Path.GetFileName(existing[0])}"
                    : $"This export will overwrite {existing.Length} files.";

                return ConfirmationDialogWindow.ConfirmModal(
                    desktop.MainWindow,
                    "File already exists",
                    message,
                    "Cancel",
                    "Overwrite");
            }

            return Dispatcher.UIThread.CheckAccess()
                ? Confirm()
                : Dispatcher.UIThread.Invoke(Confirm);
        }
    }
}
