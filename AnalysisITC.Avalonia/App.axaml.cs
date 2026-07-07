using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace AnalysisITC.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            CoreStartup.Initialize();
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            WireNativeApplicationMenu(mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
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
