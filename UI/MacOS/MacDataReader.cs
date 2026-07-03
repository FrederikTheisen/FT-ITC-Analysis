using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppKit;
using Foundation;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.DataReaders;

namespace AnalysisITC.UI.MacOS
{
    public static class MacDataReader
    {
        public static async void Read(NSUrl url) => await ReadAsync(new[] { url });

        public static async void Read(IEnumerable<NSUrl> urls) => await ReadAsync(urls);

        public static async Task ReadAsync(IEnumerable<NSUrl> urls)
        {
            var urlList = urls?.Where(url => url != null).ToArray() ?? Array.Empty<NSUrl>();
            var paths = urlList
                .Select(url => url.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
            var containsProjectFile = paths.Any(path => DataReader.GetFormat(path) == ITCDataFormat.FTITC);

            if (containsProjectFile && DataManager.SourceItems != null && DataManager.SourceItems.Count > 0)
            {
                switch (AppDelegate.PromptProjectLoadAction())
                {
                    case AppDelegate.ProjectLoadAction.Replace:
                        if (!await AppDelegate.CloseAllDataAsync(DataClearMode.ResetSession)) return;
                        break;
                    case AppDelegate.ProjectLoadAction.Cancel:
                        return;
                    case AppDelegate.ProjectLoadAction.Append:
                        break;
                }
            }

            var urlsByPath = urlList
                .Where(url => !string.IsNullOrWhiteSpace(url.Path))
                .GroupBy(url => url.Path)
                .ToDictionary(group => group.Key, group => group.First());

            await DataReader.ReadPathsAsync(paths, path =>
            {
                if (urlsByPath.TryGetValue(path, out var url))
                {
                    NSDocumentController.SharedDocumentController.NoteNewRecentDocumentURL(url);
                }
            });
        }
    }
}
