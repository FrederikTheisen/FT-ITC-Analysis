using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;
using Foundation;
using DataReaders;

namespace AnalysisITC
{
    public class ITCDataViewContainer
    {
        public readonly string UniqueID = Guid.NewGuid().ToString();
        public string FileName { get; set; } = "";
        public DateTime Date { get; internal set; }
    }

    public class AnalysisResult : ITCDataViewContainer
    {
        public GlobalSolution Solution { get; set; }

        public AnalysisResult(GlobalSolution solution)
        {
            Solution = solution;

            FileName = solution.Model.Models[0].ToString().Substring("AnalysisITC.".Length);
            Date = DateTime.Now;
        }

        public string GetResultString()
        {
            string s = "Fit of " + Solution.Solutions.Count.ToString() + " experiments";

            s += Environment.NewLine;
            s += "Enthalpy:" + Solution.Model.Options.EnthalpyStyle.ToString();
            s += Environment.NewLine;
            s += "Affinity:" + Solution.Model.Options.AffinityStyle.ToString();
            s += Environment.NewLine;
            s += "∆H @ 25 °C = " + Solution.EnthalpyRef.ToString(EnergyUnit.KiloJoule) + "/mol";
            s += Environment.NewLine;
            s += "∆Cp = " + Solution.HeatCapacity.ToString(EnergyUnit.Joule, "F0") + "/molK";

            return s;
        }
    }

    public class AnalysisITCDataSource : NSTableViewDataSource
    {
        public int SelectedIndex => DataManager.SelectedContentIndex;

        //public List<ExperimentData> Data { get; private set; }
        public List<ITCDataViewContainer> Content { get; private set; } = new List<ITCDataViewContainer>();

        #region Constructors

        public AnalysisITCDataSource()
        {
            //Data = new List<ExperimentData>();
            Content = new List<ITCDataViewContainer>();
        }

        #endregion

        #region Override Methods
        public override nint GetRowCount(NSTableView tableView)
        {
            return Content.Count;
        }
        #endregion
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

        private string GetCellIdentifier(ITCDataViewContainer content) => content is ExperimentData ? DataCellIdentifier : AnalysisCellIdentifier;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            // This pattern allows you reuse existing views when they are no-longer in use.
            // If the returned view is null, you instance up a new view
            // If a non-null view is returned, you modify it enough to reflect the new data
            ITCDataViewContainer content = DataManager.DataSourceContent[(int)row];

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
