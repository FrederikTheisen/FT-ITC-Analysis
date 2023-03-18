using System;
using System.Collections.Generic;
using AppKit;
using Foundation;
using System.Linq;
using DataReaders;

namespace AnalysisITC
{
    public class ITCDataContainer
    {
        public string UniqueID { get; private set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = "";
        public DateTime Date { get; internal set; }

        public void SetID(string id) => UniqueID = id;
    }

    public class AnalysisITCDataSource : NSTableViewDataSource
    {
        public static event EventHandler SourceWasSorted;

        public int SelectedIndex => DataManager.SelectedContentIndex;

        public List<ITCDataContainer> Content { get; private set; } = new List<ITCDataContainer>();

        #region Constructors

        public AnalysisITCDataSource()
        {
            //Data = new List<ExperimentData>();
            Content = new List<ITCDataContainer>();
        }

        #endregion

        #region Override Methods
        public override nint GetRowCount(NSTableView tableView)
        {
            return Content.Count;
        }
        #endregion

        public void SortByName()
        {
            var curr = Content[SelectedIndex];

            Content = Content.OrderBy(o => o.FileName).OrderBy(OrderOnType).ToList();

            HandleSorted(curr);
        }

        public void SortByTemperature()
        {
            var curr = Content[SelectedIndex];

            Content = Content.OrderBy(OrderOnTemperature).ToList();

            HandleSorted(curr);
        }

        public void SortByType()
        {
            var curr = Content[SelectedIndex];

            Content = Content.OrderBy(OrderOnType).ToList();

            HandleSorted(curr);
        }

        public void SetAllIncludeState(bool includeall)
        {
            var curr = Content[SelectedIndex];

            Content.Where(o => o is ExperimentData).Select(o => o as ExperimentData).ToList().ForEach(d => d.Include = includeall);

            HandleSorted(curr);
        }

        private void HandleSorted(ITCDataContainer prev)
        {
            DataManager.InvokeDataDidChange();

            SourceWasSorted?.Invoke(this, null);

            int idx = Content.IndexOf(prev);

            DataManager.SelectIndex(idx);
        }

        private static double OrderOnTemperature(ITCDataContainer item)
        {
            if (item is ExperimentData) return ((ExperimentData)item).MeasuredTemperature;
            else return double.MaxValue;
        }

        private static int OrderOnType(ITCDataContainer item)
        {
            if (item is ExperimentData)
                return 0;
            if (item is AnalysisResult)
                return 1;
            return 2;
        }
    }

    public class ExperimentDataDelegate : NSTableViewDelegate
    {
        public event EventHandler ExperimentDataViewClicked;

        public AnalysisITCDataSource Source { get; }
        public EventHandler<int> RemoveRow;

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
            ITCDataContainer content = DataManager.DataSourceContent[(int)row];

            var view = tableView.MakeView(content.UniqueID, this);

            if (view == null)
            {
                view = tableView.MakeView(GetCellIdentifier(content), this);
                view.SetIdentifier(content.UniqueID);

                switch (content)
                {
                    case ExperimentData: (view as ExperimentDataViewCell).RemoveData += OnRemoveDataButtonClick; break;
                    case AnalysisResult: (view as AnalysisResultView).RemoveData += OnRemoveDataButtonClick; break;
                    default: break;
                }
            }

            if (content is ExperimentData) (view as ExperimentDataViewCell).Setup(Source, content as ExperimentData, (int)row);
            else if (content is AnalysisResult) (view as AnalysisResultView).Setup(Source, content as AnalysisResult, (int)row);

            return view;
        }

        private void OnRemoveDataButtonClick(object sender, int e) => RemoveRow?.Invoke(this, e);

        [Export("tableViewSelectionDidChange:")]
        public override void SelectionDidChange(NSNotification notification) => ExperimentDataViewClicked.Invoke(this, null);

        [Export("tableView:heightOfRow:")]
        public override nfloat GetRowHeight(NSTableView tableView, nint row) => 48;
    }
}
