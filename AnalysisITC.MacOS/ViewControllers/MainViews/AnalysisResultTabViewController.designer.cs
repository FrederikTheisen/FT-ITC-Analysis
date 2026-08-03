// WARNING
//
// This file has been generated automatically by Visual Studio to store outlets and
// actions made in the UI designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;

namespace AnalysisITC
{
    [Register("AnalysisResultTabViewController")]
    partial class AnalysisResultTabViewController
    {
        [Outlet]
        AnalysisITC.ResultGraphView Graph { get; set; }

        [Outlet]
        AppKit.NSView ResultAnalysisPage { get; set; }

        [Outlet]
        AppKit.NSStackView ResultAnalysisStack { get; set; }

        [Outlet]
        AppKit.NSView ResultExperimentsPage { get; set; }

        [Outlet]
        AppKit.NSStackView ResultExperimentsStack { get; set; }

        [Outlet]
        AppKit.NSSegmentedControl ResultInspectorTabControl { get; set; }

        [Outlet]
        AppKit.NSTabView ResultInspectorTabView { get; set; }

        [Outlet]
        AppKit.NSView ResultModelPage { get; set; }

        [Outlet]
        AppKit.NSStackView ResultModelStack { get; set; }

        [Outlet]
        AppKit.NSView ResultSummaryPage { get; set; }

        [Outlet]
        AppKit.NSStackView ResultSummaryStack { get; set; }

        [Outlet]
        AppKit.NSTableView ResultsTableView { get; set; }

        [Action("ResultInspectorTabChanged:")]
        partial void ResultInspectorTabChanged(Foundation.NSObject sender);

        void ReleaseDesignerOutlets()
        {
            Graph?.Dispose();
            Graph = null;
            ResultAnalysisPage?.Dispose();
            ResultAnalysisPage = null;
            ResultAnalysisStack?.Dispose();
            ResultAnalysisStack = null;
            ResultExperimentsPage?.Dispose();
            ResultExperimentsPage = null;
            ResultExperimentsStack?.Dispose();
            ResultExperimentsStack = null;
            ResultInspectorTabControl?.Dispose();
            ResultInspectorTabControl = null;
            ResultInspectorTabView?.Dispose();
            ResultInspectorTabView = null;
            ResultModelPage?.Dispose();
            ResultModelPage = null;
            ResultModelStack?.Dispose();
            ResultModelStack = null;
            ResultSummaryPage?.Dispose();
            ResultSummaryPage = null;
            ResultSummaryStack?.Dispose();
            ResultSummaryStack = null;
            ResultsTableView?.Dispose();
            ResultsTableView = null;
        }
    }
}
