using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

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
            return true;
        }
    }
}
