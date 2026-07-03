using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Platform;
using AppKit;

namespace AnalysisITC.UI.MacOS
{
    public sealed class MacFileSavePromptService : IFileSavePromptService
    {
        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes)
        {
            var tcs = new TaskCompletionSource<string>();
            var dlg = new NSSavePanel
            {
                Title = title,
                AllowedFileTypes = allowedFileTypes?.ToArray()
            };

            dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, result =>
            {
                tcs.TrySetResult(result == (int)NSModalResponse.OK ? dlg.Filename : null);
            });

            return tcs.Task;
        }
    }
}
