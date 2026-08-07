using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Platform.Avalonia;

namespace AnalysisITC.Avalonia;

public partial class App : Application
{
    AnalysisProgressCoordinator? analysisProgressCoordinator;
    readonly List<string> pendingActivationPaths = new();
    MainWindow? mainWindow;
    bool mainWindowOpened;
    bool isOpeningActivationPaths;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            CoreStartup.Initialize();
            analysisProgressCoordinator = new AnalysisProgressCoordinator();
            desktop.Exit += (_, _) => analysisProgressCoordinator?.Dispose();
            MacDockIcon.Apply();
            mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            WireNativeApplicationMenu(mainWindow);
            WireFileActivation(desktop, mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    void WireFileActivation(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
        {
            activatableLifetime.Activated += (_, args) =>
            {
                if (args is not FileActivatedEventArgs fileArgs || args.Kind != ActivationKind.File) return;

                QueueActivationPaths(fileArgs.Files
                    .Select(GetLocalPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!));
            };
        }

        QueueActivationPaths(desktop.Args?.Where(File.Exists) ?? Array.Empty<string>());
        window.Opened += async (_, _) =>
        {
            mainWindowOpened = true;
            await window.InitializeAutoSaveAndRecoveryAsync();
            await FlushActivationPathsAsync();
        };
    }

    void QueueActivationPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if (!pendingActivationPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                pendingActivationPaths.Add(fullPath);
        }

        if (mainWindowOpened)
            _ = FlushActivationPathsAsync();
    }

    async Task FlushActivationPathsAsync()
    {
        if (!mainWindowOpened || mainWindow == null || isOpeningActivationPaths) return;

        isOpeningActivationPaths = true;
        try
        {
            while (pendingActivationPaths.Count > 0)
            {
                var paths = pendingActivationPaths.ToArray();
                pendingActivationPaths.Clear();
                await mainWindow.OpenExternalPathsAsync(paths);
            }
        }
        finally
        {
            isOpeningActivationPaths = false;
        }
    }

    static string? GetLocalPath(IStorageItem item)
    {
        var localPath = item.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath)) return localPath;

        return item.Path.IsFile ? item.Path.LocalPath : null;
    }

    void WireNativeApplicationMenu(MainWindow mainWindow)
    {
        var menu = NativeMenu.GetMenu(this);
        if (menu == null) return;

        foreach (var item in menu.Items)
        {
            if (item is not NativeMenuItem menuItem) continue;

            switch (menuItem.Header as string)
            {
                case "About FT-ITC Analysis":
                    menuItem.Click += async (_, _) => await mainWindow.ShowAboutAsync();
                    break;
                case "Preferences...":
                    menuItem.Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta);
                    menuItem.Click += async (_, _) => await mainWindow.OpenPreferencesAsync();
                    break;
                case "Quit":
                    menuItem.Header = "Quit FT-ITC Analysis";
                    menuItem.Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta);
                    menuItem.Click += async (_, _) => await mainWindow.QuitAsync();
                    break;
            }
        }
    }
}
