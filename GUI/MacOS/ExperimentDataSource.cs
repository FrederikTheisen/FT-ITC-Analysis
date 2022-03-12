using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;
using Foundation;
using DataReaders;

namespace AnalysisITC
{
    public class ExperimentDataSource : NSTableViewDataSource
    {
        public int SelectedIndex { get; set; } = 0;

        public List<ExperimentData> Data { get; private set; }

        #region Constructors

        public ExperimentDataSource()
        {
            Data = new List<ExperimentData>();
        }

        #endregion

        #region Override Methods
        public override nint GetRowCount(NSTableView tableView)
        {
            return DataManager.Count;
        }
        #endregion
    }

    public class ExperimentDataDelegate : NSTableViewDelegate
    {
        public event EventHandler ExperimentDataViewClicked;

        public ExperimentDataSource Source { get; }
        public EventHandler<int> RemoveRow;

        public ExperimentDataDelegate(ExperimentDataSource source)
        {
            Source = source;
        }

        private const string CellIdentifier = "ExperimentDataViewCell";

        

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            // This pattern allows you reuse existing views when they are no-longer in use.
            // If the returned view is null, you instance up a new view
            // If a non-null view is returned, you modify it enough to reflect the new data
            ExperimentData series = DataManager.Data[(int)row];

            var view = tableView.MakeView(series.FileName, this);

            if (view == null)
            {
                view = tableView.MakeView(CellIdentifier, this);
                view.SetIdentifier(series.FileName);
                (view as ExperimentDataViewCell).RemoveData += OnRemoveDataButtonClick;
            }

            (view as ExperimentDataViewCell).Setup(Source, series, (int)row);
            

            return view;
        }

        private void OnRemoveDataButtonClick(object sender, int e)
        {
            Console.WriteLine("OnRemoveClick " + e);

            RemoveRow?.Invoke(this, e);
        }

        [Export("tableViewSelectionDidChange:")]
        public override void SelectionDidChange(NSNotification notification)
        {
            ExperimentDataViewClicked.Invoke(this, null);
        }

        [Export("tableView:heightOfRow:")]
        public override nfloat GetRowHeight(NSTableView tableView, nint row)
        {
            return 60;
        }

    }
}
