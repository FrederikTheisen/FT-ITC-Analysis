using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaFileSavePromptService : IFileSavePromptService
    {
        public async Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes)
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return "";

            var storage = desktop.MainWindow?.StorageProvider;
            if (storage == null) return "";

            var extensions = allowedFileTypes?
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Select(extension => extension.Trim().TrimStart('.'))
                .Distinct()
                .ToList() ?? new List<string>();

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                FileTypeChoices = extensions.Count == 0
                    ? new[] { FilePickerFileTypes.All }
                    : new[]
                    {
                        new FilePickerFileType(string.Join("/", extensions.Select(ext => "." + ext)))
                        {
                            Patterns = extensions.Select(ext => "*." + ext).ToArray()
                        },
                        FilePickerFileTypes.All
                    }
            });

            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return "";

            if (extensions.Count > 0 && string.IsNullOrWhiteSpace(Path.GetExtension(path)))
                path += "." + extensions[0];

            return path;
        }
    }
}
