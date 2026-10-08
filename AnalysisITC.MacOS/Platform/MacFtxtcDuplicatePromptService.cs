using AppKit;
using Foundation;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;

namespace AnalysisITC.UI.MacOS
{
    public sealed class MacFtxtcDuplicatePromptService : IFtxtcDuplicatePromptService
    {
        public FtxtcDuplicateAction ChooseAction(FtxtcDuplicateSummary summary)
        {
            using var alert = CreateAlert(summary);
            var window = alert.Window;
            var closing = NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.WillCloseNotification,
                _ => NSApplication.SharedApplication.AbortModal(), window);
            // AppKit normally assigns Escape to the second alert button. This
            // warning always dismisses to Skip, regardless of button focus.
            var monitor = NSEvent.AddLocalMonitorForEventsMatchingMask(NSEventMask.KeyDown, input =>
            {
                if (input.WindowNumber != window.WindowNumber
                    || (input.KeyCode != 36 && input.KeyCode != 76 && input.KeyCode != 53)) return input;
                NSApplication.SharedApplication.StopModalWithCode(1000);
                return null;
            });
            try
            {
                return alert.RunModal() == 1001 ? FtxtcDuplicateAction.ImportCopies : FtxtcDuplicateAction.SkipDuplicates;
            }
            finally
            {
                NSEvent.RemoveMonitor(monitor);
                monitor.Dispose();
                NSNotificationCenter.DefaultCenter.RemoveObserver(closing);
                closing.Dispose();
            }
        }

        internal static NSAlert CreateAlert(FtxtcDuplicateSummary summary)
        {
            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Warning,
                MessageText = FtxtcDuplicatePresentation.Title,
                InformativeText = FtxtcDuplicatePresentation.Message(summary),
            };
            alert.AddButton(FtxtcDuplicatePresentation.SkipLabel);
            alert.AddButton(FtxtcDuplicatePresentation.CopyLabel);
            // AppKit assigns key equivalents during layout; apply our default afterwards.
            alert.Layout();
            alert.Buttons[0].ToolTip = FtxtcDuplicatePresentation.SkipToolTip;
            alert.Buttons[1].ToolTip = FtxtcDuplicatePresentation.CopyToolTip;
            alert.Window.DefaultButtonCell = (NSButtonCell)alert.Buttons[0].Cell;
            alert.Window.StyleMask |= NSWindowStyle.Closable;
            return alert;
        }
    }
}
