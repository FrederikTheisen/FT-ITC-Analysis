using System;
using System.Globalization;
using AppKit;
using CoreGraphics;
using Foundation;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.UI.MacOS.CustomViews;

namespace AnalysisITC
{
    public class AnalysisITCDataSource : NSTableViewDataSource
    {
        public const string RowPasteboardType = "com.ftitcanalysis.sidebar-row";
        int draggedRow = -1;

        #region Constructors

        public AnalysisITCDataSource() { }

        #endregion

        #region Override Methods
        public override nint GetRowCount(NSTableView tableView)
        {
            return DataManager.SourceItems.Count;
        }

        public override bool WriteRows(NSTableView tableView, NSIndexSet rowIndexes, NSPasteboard pboard)
        {
            if (rowIndexes == null || rowIndexes.Count != 1) return false;

            draggedRow = (int)rowIndexes.FirstIndex;
            pboard.DeclareTypes(new[] { RowPasteboardType }, tableView);
            return pboard.SetStringForType(
                draggedRow.ToString(CultureInfo.InvariantCulture),
                RowPasteboardType);
        }

        public override NSDragOperation ValidateDrop(
            NSTableView tableView,
            NSDraggingInfo info,
            nint row,
            NSTableViewDropOperation dropOperation)
        {
            if (draggedRow < 0
                || info.DraggingPasteboard.GetStringForType(RowPasteboardType) == null
                || dropOperation != NSTableViewDropOperation.Above)
                return NSDragOperation.None;

            tableView.SetDropRowDropOperation(row, NSTableViewDropOperation.Above);
            return NSDragOperation.Move;
        }

        public override bool AcceptDrop(
            NSTableView tableView,
            NSDraggingInfo info,
            nint row,
            NSTableViewDropOperation dropOperation)
        {
            if (draggedRow < 0
                || info.DraggingPasteboard.GetStringForType(RowPasteboardType) == null
                || dropOperation != NSTableViewDropOperation.Above)
                return false;

            var source = draggedRow;
            draggedRow = -1;
            DataManager.MoveSourceItem(source, (int)row);
            return true;
        }
        #endregion
    }

    public class ExperimentDataDelegate : NSTableViewDelegate
    {
        public event EventHandler ExperimentDataViewClicked;
        public event EventHandler<ITCDataContainer> RemoveItemRequested;

        public AnalysisITCDataSource Source { get; }

        public ExperimentDataDelegate(AnalysisITCDataSource source)
        {
            Source = source;
        }

        private const string DataCellIdentifier = "ExperimentDataViewCell";
        private const string AnalysisCellIdentifier = "AnalysisResultView";

        private string GetCellIdentifier(ITCDataContainer content) => content is ExperimentData ? DataCellIdentifier : AnalysisCellIdentifier;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            // This pattern allows you reuse existing views when they are no-longer in use.
            // If the returned view is null, you instance up a new view
            // If a non-null view is returned, you modify it enough to reflect the new data
            ITCDataContainer content = DataManager.SourceItems[(int)row];

            var view = tableView.MakeView(GetCellIdentifier(content), this);

            if (content is ExperimentData experimentData)
            {
                (view as ExperimentDataViewCell).Setup(experimentData, OnRemoveItemRequested);
            }
            else if (content is AnalysisResult analysisResult)
            {
                (view as AnalysisResultView).Setup(analysisResult, OnRemoveItemRequested);
            }

            return view;
        }

        void OnRemoveItemRequested(ITCDataContainer item) => RemoveItemRequested?.Invoke(this, item);

        [Export("tableViewSelectionDidChange:")]
        public override void SelectionDidChange(NSNotification notification) => ExperimentDataViewClicked?.Invoke(this, null);

        [Export("tableView:heightOfRow:")]
        public override nfloat GetRowHeight(NSTableView tableView, nint row) => 48;

        [Export("tableView:rowViewForRow:")]
        public override NSTableRowView CoreGetRowView(NSTableView tableView, nint row)
        {
            return new SourceListHoverRowView();
        }
    }

    /// <summary>
    /// Keeps the source list's native selection appearance while providing a
    /// quiet indication that an unselected row is under the pointer.
    /// </summary>
    sealed class SourceListHoverRowView : NSTableRowView
    {
        readonly ScrollAwareHoverTracker hoverTracker;

        public SourceListHoverRowView()
        {
            hoverTracker = new ScrollAwareHoverTracker(this, () => NeedsDisplay = true);
        }

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();
            hoverTracker?.UpdateTrackingArea();
        }

        public override void ViewDidMoveToWindow()
        {
            base.ViewDidMoveToWindow();
            hoverTracker?.UpdateTrackingArea();
        }

        public override void ViewDidMoveToSuperview()
        {
            base.ViewDidMoveToSuperview();
            hoverTracker?.UpdateTrackingArea();
        }

        public override void ViewDidHide()
        {
            base.ViewDidHide();
            hoverTracker?.Reconcile();
        }

        public override void ViewDidUnhide()
        {
            base.ViewDidUnhide();
            hoverTracker?.Reconcile();
        }

        public override void MouseEntered(NSEvent theEvent)
        {
            base.MouseEntered(theEvent);
            hoverTracker.Reconcile();
        }

        public override void MouseExited(NSEvent theEvent)
        {
            base.MouseExited(theEvent);
            hoverTracker.Reconcile();
        }

        public override void DrawBackground(CGRect dirtyRect)
        {
            base.DrawBackground(dirtyRect);

            if (!hoverTracker.IsHovered || Selected) return;

            NSColor.Label.ColorWithAlphaComponent(0.045f).SetFill();
            var hoverBounds = Bounds;
            hoverBounds.Inflate(-10, -2);
            NSBezierPath.FromRoundedRect(hoverBounds, 5, 5).Fill();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) hoverTracker?.Dispose();

            base.Dispose(disposing);
        }
    }
}
