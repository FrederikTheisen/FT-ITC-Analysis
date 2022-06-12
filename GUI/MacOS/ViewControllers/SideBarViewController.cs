// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
    public partial class SideBarViewController : NSViewController
    {
        public static NSImage DataNotProcessedImage { get; private set; } = null;
        public static NSImage DataDisabledImage { get; private set; } = null;
        public static NSImage DataEnabledImage { get; private set; } = null;

        #region Constructors

        // Called when created from unmanaged code
        public SideBarViewController(IntPtr handle) : base(handle)
        {
            Initialize();
        }

        // Called when created directly from a XIB file
        [Export("initWithCoder:")]
        public SideBarViewController(NSCoder coder) : base(coder)
        {
            Initialize();
        }

        // Call to load from the XIB/NIB file
        public SideBarViewController() : base("DataSeriesSideBarView", NSBundle.MainBundle)
        {
            Initialize();
        }

        // Shared initialization code
        void Initialize()
        {
            DataManager.DataDidChange += OnDataManagerUpdated;
            DataManager.SelectionDidChange += DataManager_SelectionDidChange;
            DataManager.AnalysisResultSelected += DataManager_AnalysisResultSelected;

            ExperimentDataViewCell.ExpandDataButtonClicked += ExperimentDataViewCell_ExpandDataButtonClicked;
            AnalysisResultView.ExpandDataButtonClicked += AnalysisResultView_ExpandDataButtonClicked;

            ExperimentDetailsPopoverController.UpdateTable += ExperimentDetailsPopoverController_UpdateTable;
        }

        private void ExperimentDetailsPopoverController_UpdateTable(object sender, EventArgs e)
        {
            TableView.ReloadData();
        }

        private void ExperimentDataViewCell_ExpandDataButtonClicked(object sender, ExperimentData e)
        {
            ExperimentDetailsPopoverController.Data = e;

            PerformSegue("DetailsSegue", sender as NSObject);
        }

        private void AnalysisResultView_ExpandDataButtonClicked(object sender, AnalysisResult e)
        {
            BindingAnalysisViewController.AnalysisResult = e;

            PerformSegue("ShowAnalysisResultSegue", this);
        }

        private void DataManager_SelectionDidChange(object sender, ExperimentData e)
        {
            TableView.SelectRow(DataManager.DataSource.SelectedIndex, false);
        }

        private void DataManager_AnalysisResultSelected(object sender, AnalysisResult e)
        {
            //PerformSegue("ShowAnalysisResultSegue", this);
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            TableView.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly;

            DataNotProcessedImage = NotProcessedImage.Image;
            DataEnabledImage = IncludedImage.Image;
            DataDisabledImage = NotIncludedImage.Image;
        }

        private void OnDataManagerUpdated(object sender, ExperimentData data)
        {
            TableView.DataSource = DataManager.DataSource;

            var del = new ExperimentDataDelegate(DataManager.DataSource);
            del.ExperimentDataViewClicked += OnDataViewClicked;
            del.RemoveRow += OnRowRemoveEvent;

            TableView.Delegate = del;
        }

        private void OnRowRemoveEvent(object sender, int e)
        {
            //var item = (TableView.Delegate as ExperimentDataDelegate).Source.Content[e];
            //Console.WriteLine("TV REMOVE: " + item.UniqueID + " " + e);

            TableView.RemoveRows(new NSIndexSet(e), NSTableViewAnimation.SlideLeft);
        }

        private void OnDataViewClicked(object sender, EventArgs e)
        {
            DataManager.SelectIndex((int)TableView.SelectedRow);
        }

        #endregion
    }
}
