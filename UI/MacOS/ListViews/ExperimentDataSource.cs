using System;
using AppKit;
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

namespace AnalysisITC
{
    public class AnalysisITCDataSource : NSTableViewDataSource
    {
        #region Constructors

        public AnalysisITCDataSource() { }

        #endregion

        #region Override Methods
        public override nint GetRowCount(NSTableView tableView)
        {
            return DataManager.SourceItems.Count;
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
    }
}
