// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
    public partial class SideBarViewController : NSViewController
    {
        private ExperimentData SelectedData() => DataManager.Current;
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
        }

        private void DataManager_SelectionDidChange(object sender, ExperimentData e)
        {
            TableView.SelectRow(DataManager.DataSource.SelectedIndex, false);
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            TableView.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly;

            DataNotProcessedImage = NSPlayImage.Image;
            DataEnabledImage = NSPlayFillImage.Image;
            DataDisabledImage = NSPlaySlashedFIllImage.Image;
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
            Console.WriteLine("RemoveRow " + e);

            if (e >= TableView.RowCount)
            {

            }

            TableView.RemoveRows(new NSIndexSet(e), NSTableViewAnimation.SlideLeft);
        }

        private void OnDataViewClicked(object sender, EventArgs e)
        {
            DataManager.SelectIndex((int)TableView.SelectedRow);
        }

        

        #endregion
    }
}
