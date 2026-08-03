// WARNING
//
// This file has been generated automatically by Visual Studio to store outlets
// made in the UI designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;

namespace AnalysisITC
{
    [Register("StatusBarViewController")]
    partial class StatusBarViewController
    {
        [Outlet]
        AppKit.NSTextField DocumentStatusLabel { get; set; }

        [Outlet]
        AppKit.NSProgressIndicator ProgressIndicator { get; set; }

        [Outlet]
        AppKit.NSTextField StatusLabel { get; set; }

        void ReleaseDesignerOutlets()
        {
            if (DocumentStatusLabel != null)
            {
                DocumentStatusLabel.Dispose();
                DocumentStatusLabel = null;
            }

            if (ProgressIndicator != null)
            {
                ProgressIndicator.Dispose();
                ProgressIndicator = null;
            }

            if (StatusLabel != null)
            {
                StatusLabel.Dispose();
                StatusLabel = null;
            }
        }
    }
}
