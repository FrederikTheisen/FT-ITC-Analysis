using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Platform.Avalonia;

public sealed class AvaloniaAnalysisResultUpdatePromptService : IAnalysisResultUpdatePromptService
{
    public async Task<AnalysisResultUpdateOptions> ChooseOptionsAsync(AnalysisResult result)
    {
        var owner = GetMainWindow();
        if (owner == null)
            return AnalysisResultUpdateOptions.StoredSettings;

        var dialog = new AnalysisResultUpdateDialogWindow(result);
        return (await dialog.ShowDialog<AnalysisResultUpdateOptions?>(owner))!;
    }

    static Window? GetMainWindow()
    {
        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
