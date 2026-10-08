using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Platform.Avalonia;

public sealed class AvaloniaFtxtcDuplicatePromptService : IFtxtcDuplicatePromptService
{
    public FtxtcDuplicateAction ChooseAction(FtxtcDuplicateSummary summary)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => ChooseAction(summary));
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
        {
            AppEventHandler.PrintAndLog(FtxtcDuplicatePresentation.Title + ": " + FtxtcDuplicatePresentation.Message(summary));
            return FtxtcDuplicateAction.SkipDuplicates;
        }

        var task = new DuplicatePromptWindow(summary).ShowDialog<FtxtcDuplicateAction>(desktop.MainWindow);
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
        Dispatcher.UIThread.PushFrame(frame);
        return task.IsCompletedSuccessfully ? task.Result : FtxtcDuplicateAction.SkipDuplicates;
    }

    internal sealed class DuplicatePromptWindow : Window
    {
        internal Button SkipButton { get; }
        internal Button CopyButton { get; }

        internal DuplicatePromptWindow(FtxtcDuplicateSummary summary)
        {
            Title = FtxtcDuplicatePresentation.Title;
            Width = 540;
            MinWidth = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var message = new TextBlock
            {
                Text = FtxtcDuplicatePresentation.Message(summary),
                TextWrapping = TextWrapping.Wrap,
            };
            AppTheme.Bind(message, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            SkipButton = new Button { Content = FtxtcDuplicatePresentation.SkipLabel, IsDefault = true, IsCancel = true };
            CopyButton = new Button { Content = FtxtcDuplicatePresentation.CopyLabel };
            ToolTip.SetTip(SkipButton, FtxtcDuplicatePresentation.SkipToolTip);
            ToolTip.SetTip(CopyButton, FtxtcDuplicatePresentation.CopyToolTip);
            SkipButton.Click += (_, _) => Close(FtxtcDuplicateAction.SkipDuplicates);
            CopyButton.Click += (_, _) => Close(FtxtcDuplicateAction.ImportCopies);
            AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args.Key != Key.Enter && args.Key != Key.Escape) return;
                args.Handled = true;
                Close(FtxtcDuplicateAction.SkipDuplicates);
            }, RoutingStrategies.Tunnel);
            var layout = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    new ScrollViewer { Content = message, MaxHeight = 400 },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { SkipButton, CopyButton },
                    },
                },
            };
            var border = new Border { Padding = new Thickness(18), Child = layout };
            AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
            Content = border;
        }
    }
}
