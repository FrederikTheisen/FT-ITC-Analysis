using System;
using AppKit;
using Foundation;

namespace AnalysisITC
{
    [Register("SettingsTabViewController")]
    public class SettingsTabViewController : NSTabViewController
    {
        const int DefaultTabIndex = 0;
        const int LastSettingsTabIndex = 2;

        static int selectedTabIndex = DefaultTabIndex;

        bool isRestoringSelection;
        bool hasAppeared;

        public SettingsTabViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            RestoreSelectedTab();
        }

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();

            hasAppeared = false;
            RestoreSelectedTab();
        }

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();

            RestoreSelectedTab();
            hasAppeared = true;
        }

        public override void ViewWillDisappear()
        {
            SaveSelectedTab(TabView.Selected);

            hasAppeared = false;
            base.ViewWillDisappear();
        }

        [Export("tabView:didSelectTabViewItem:")]
        public void DidSelect(NSTabView tabView, NSTabViewItem item)
        {
            if (isRestoringSelection || !hasAppeared)
            {
                return;
            }

            SaveSelectedTab(item);
        }

        private void SaveSelectedTab(NSTabViewItem item)
        {
            if (item == null)
            {
                return;
            }

            var selectedIndex = (int)TabView.IndexOf(item);
            if (selectedIndex < DefaultTabIndex || selectedIndex > LastSettingsTabIndex)
            {
                return;
            }

            selectedTabIndex = selectedIndex;
        }

        private void RestoreSelectedTab()
        {
            var restoredTabIndex = Math.Max(DefaultTabIndex, Math.Min(LastSettingsTabIndex, selectedTabIndex));

            try
            {
                isRestoringSelection = true;
                TabView.SelectAt(restoredTabIndex);
            }
            finally
            {
                isRestoringSelection = false;
            }
        }
    }
}
