// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
    public partial class SideBarViewController : NSViewController
    {
        private ExperimentData SelectedData() => DataManager.Current();

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
            
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            DataManager.DataDidChange += OnDataManagerUpdated;

            TableView.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly;
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
