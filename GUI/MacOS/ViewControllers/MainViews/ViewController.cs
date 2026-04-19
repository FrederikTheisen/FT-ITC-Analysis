using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppKit;
using Foundation;
using CoreGraphics;
using MathNet.Numerics.LinearAlgebra.Solvers;
using MathNet.Numerics.LinearAlgebra.Double;

namespace AnalysisITC
{
    public partial class ViewController : NSViewController
    {
        public static event EventHandler UpdateTable;
        public static event EventHandler<int> WillRemoveData;
        public static event EventHandler<int> RemoveData;

        public static void NotifyWillRemoveData(object sender, int index)
        {
            WillRemoveData?.Invoke(sender, index);
        }

        ExperimentData Data => DataManager.Current;

        NSScrollView LoadedInjectionScrollView;
        NSTableView LoadedInjectionTableView;
        LoadedInjectionDataSource LoadedInjectionSource;
        LoadedInjectionTableDelegate LoadedInjectionDelegate;

        public ViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            DataManager.Init();
            DocumentDirtyTracker.Initialize();
            DocumentDirtyTracker.MarkClean();

            DataManager.DataDidChange += OnDataChanged;
            DataManager.SelectionDidChange += OnSelectionChanged;
            StateManager.UpdateStateDependentUI += StateManager_UpdateStateDependentUI;
            AppDelegate.StartPrintOperation += AppDelegate_StartPrintOperation;
            BindExperimentMenuActions(ExperimentMenuButton.Menu);
            SetupLoadedInjectionTable();

            ShowLoadDataPrompt();
        }

        public override void ViewDidAppear()
        {
             base.ViewDidAppear();

            UpdateGraph();
        }

        public override void ViewDidLayout()
        {
            base.ViewDidLayout();

            ResizeLoadedInjectionColumns();
        }

        void BindExperimentMenuActions(NSMenu menu)
        {
            if (menu == null) return;

            foreach (var item in menu.Items)
            {
                item.Activated -= OnExperimentMenuItemActivated;
                item.Activated += OnExperimentMenuItemActivated;

                if (item.Submenu != null)
                    BindExperimentMenuActions(item.Submenu);
            }
        }

        void SetupLoadedInjectionTable()
        {
            LoadedInjectionTableView = new NSTableView
            {
                HeaderView = new NSTableHeaderView(),
                UsesAlternatingRowBackgroundColors = true,
                GridStyleMask = NSTableViewGridStyle.SolidHorizontalLine,
                SelectionHighlightStyle = NSTableViewSelectionHighlightStyle.Regular,
                AllowsEmptySelection = true,
                AllowsMultipleSelection = false,
                ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.None,
                TranslatesAutoresizingMaskIntoConstraints = true,
                //BackgroundColor = NSColor.WindowBackground,
            };
            LoadedInjectionTableView.Frame = GVC.Frame;
            LoadedInjectionTableView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;

            AddColumn("ID", "#", 20, headerAlignment: NSTextAlignment.Center);
            AddColumn("Volume", "Vol. (µl)", 50);
            AddColumn("M", "[M] (µM)", 60);
            AddColumn("L", "[L] (µM)", 60);
            AddColumn("Ratio", "Ratio", 50);
            AddColumn("NormHeat", $"Norm. Heat ({AppSettings.EnergyUnit.GetUnit()}/mol)", 80);

            LoadedInjectionSource = new LoadedInjectionDataSource(null);
            LoadedInjectionDelegate = new LoadedInjectionTableDelegate(LoadedInjectionSource);
            LoadedInjectionTableView.DataSource = LoadedInjectionSource;
            LoadedInjectionTableView.Delegate = LoadedInjectionDelegate;

            LoadedInjectionScrollView = new NSScrollView
            {
                DocumentView = LoadedInjectionTableView,
                HasVerticalScroller = true,
                HasHorizontalScroller = false,
                HorizontalScrollElasticity = NSScrollElasticity.None,
                AutohidesScrollers = true,
                BorderType = NSBorderType.LineBorder,
                DrawsBackground = true,
                BackgroundColor = NSColor.WindowBackground,
                TranslatesAutoresizingMaskIntoConstraints = false,
                Hidden = true,
            };

            GVC.Superview.AddSubview(LoadedInjectionScrollView);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                LoadedInjectionScrollView.TopAnchor.ConstraintEqualToAnchor(GVC.TopAnchor),
                LoadedInjectionScrollView.BottomAnchor.ConstraintEqualToAnchor(GVC.BottomAnchor),
                LoadedInjectionScrollView.LeadingAnchor.ConstraintEqualToAnchor(GVC.LeadingAnchor),
                LoadedInjectionScrollView.TrailingAnchor.ConstraintEqualToAnchor(GVC.TrailingAnchor),
            });
        }

        void AddColumn(string identifier, string title, nfloat width, NSTextAlignment headerAlignment = NSTextAlignment.Right)
        {
            var column = new NSTableColumn(identifier)
            {
                Identifier = identifier,
                Width = width,
                MinWidth = width,
                ResizingMask = NSTableColumnResizing.UserResizingMask,
                Editable = false,
                HeaderCell = new NSTableHeaderCell(title)
                {
                    Alignment = headerAlignment,
                },
            };

            LoadedInjectionTableView.AddColumn(column);
        }

        void ResizeLoadedInjectionColumns()
        {
            if (LoadedInjectionTableView == null || LoadedInjectionTableView.TableColumns() == null) return;

            var scrollWidth = LoadedInjectionScrollView?.Frame.Width ?? 0;
            var graphWidth = GVC?.Frame.Width ?? 0;
            var width = Math.Max(Math.Min(scrollWidth > 0 ? scrollWidth : graphWidth, graphWidth > 0 ? graphWidth : scrollWidth), 0);
            if (width <= 0) return;

            var columns = LoadedInjectionTableView.TableColumns();
            if (columns.Length == 0) return;

            var fixedWidths = new Dictionary<string, nfloat>
            {
                { "ID", 42f },
                { "Volume", 72f },
                { "M", 92f },
                { "L", 92f },
                { "Ratio", 72f },
            };

            var intercellWidth = LoadedInjectionTableView.IntercellSpacing.Width * Math.Max(columns.Length - 1, 0);
            var usableWidth = Math.Max(width - intercellWidth - 2, 240);
            var fixedWidthSum = fixedWidths.Values.Sum(v => v);
            var remainingWidth = Math.Max(usableWidth - fixedWidthSum, 120) - 20;

            foreach (var column in columns)
            {
                var columnWidth = fixedWidths.TryGetValue(column.Identifier, out var value)
                    ? value
                    : remainingWidth;

                column.Width = (nfloat)columnWidth;
                column.MinWidth = (nfloat)Math.Min(columnWidth, 60);
            }

            var headerHeight = LoadedInjectionTableView.HeaderView?.Frame.Height ?? 0;
            var rowHeight = LoadedInjectionDelegate?.GetRowHeight(LoadedInjectionTableView, 0) ?? 22;
            var tableHeight = Math.Max(LoadedInjectionScrollView?.ContentSize.Height ?? 0, headerHeight + rowHeight * LoadedInjectionSource.Injections.Count);
            LoadedInjectionTableView.Frame = new CGRect(0, 0, width, tableHeight);
        }

        private void OnExperimentMenuItemActivated(object sender, EventArgs e)
        {
            var item = sender as NSMenuItem;
            if (item == null) return;

            var iden = item.Identifier;

            switch (iden)
            {
                case "openattributes":
                    EditAttributesAction(null);
                    break;
                case "copyatttoactive":
                    DataManager.CopySelectedAttributesToActive();
                    break;
                case "copyatttoall":
                case "copyattributes":
                    DataManager.CopySelectedAttributesToAll();
                    break;
                case "toggleinclude":
                    ToggleInclusionAction(null);
                    break;
                case "duplicate":
                    DataManager.DuplicateSelectedData(Data);
                    break;
                case "export": 
                    Exporter.Export(ExportType.Data, ExportDataSelection.SelectedData);
                    break;
                case "clearsolution":
                    RemoveSolution();
                    break;
                case "delete":
                    DeleteDataAction();
                    break;
                default: break;
            }
        }

        void DeleteDataAction()
        {
            var alert = new NSAlert
            {
                InformativeText = $"Are you sure you wish to delete {DataManager.Current.Name}?",
                MessageText = "Confirm Delete Data",
                AlertStyle = NSAlertStyle.Warning,
            };

            alert.AddButton("Cancel");
            alert.AddButton("Delete Data");

            alert.Buttons[1].HasDestructiveAction = true;

            if (alert.RunModal() == 1001)
            {
                int idx = DataManager.SelectedContentIndex;
                NotifyWillRemoveData(this, idx);
                DataManager.RemoveData2(idx);
                RemoveData?.Invoke(this, idx);
            }
        }

        private void AppDelegate_StartPrintOperation(object sender, EventArgs e)
        {
            if (StateManager.CurrentState != ProgramState.Load) return;

            GVC.Print();
        }

        private void StateManager_UpdateStateDependentUI(object sender, EventArgs e)
        {
            ShowLoadDataPrompt();
        }

        partial void LoadDataButtonClick(NSObject sender)
        {
            //LoadDataPrompt.Hidden = true;

            AppDelegate.LaunchOpenFileDialog();
        }

        partial void LoadLastFile(NSObject sender)
        {
            if (AppSettings.LastDocumentUrls != null)
            {
                DataReaders.DataReader.Read(AppSettings.LastDocumentUrls);
            }
            else DataReaders.DataReader.Read(AppSettings.LastDocumentUrl);
        }

        private void OnSelectionChanged(object sender, ExperimentData e) => UpdateGraph();
        private void OnDataChanged(object sender, ExperimentData e) => UpdateGraph();

        private void UpdateGraph()
        {
            var showLoadedInjectionTable = DataManager.Current != null && !DataManager.Current.HasThermogram;

            LoadedInjectionScrollView.Hidden = !showLoadedInjectionTable;
            LoadedInjectionTableView.Hidden = !showLoadedInjectionTable;

            GVC.Initialize(DataManager.Current);

            if (showLoadedInjectionTable)
            {
                LoadedInjectionSource.SetData(DataManager.Current);
                View.LayoutSubtreeIfNeeded();
                ResizeLoadedInjectionColumns();
                LoadedInjectionTableView.ReloadData();

                BeginInvokeOnMainThread(() =>
                {
                    View.LayoutSubtreeIfNeeded();
                    ResizeLoadedInjectionColumns();
                    LoadedInjectionTableView.ReloadData();
                });
            }

            TitleLabel.StringValue = DataManager.Current?.Name ?? "No Data Selected";
            TitleLabel.TextColor = DataManager.Current != null ? NSColor.Label : NSColor.DisabledControlText;

            ExperimentMenuButton.Enabled = DataManager.Current != null;

            UpdateLabel();
        }

        void UpdateLabel()
        {
            if (Data == null) InfoLabel.StringValue = "";
            else
            {
                InfoLabel.AttributedStringValue = Utilities.MacStrings.FromMarkDownString(string.Join(Environment.NewLine, DataManager.Current.GetInfoString()), InfoLabel.Font);
            }
        }

        partial void ClearButtonClick(NSObject sender)
        {
            AppDelegate.CloseAllData();
        }

        partial void ContinueClick(NSObject sender)
        {
            
        }

        partial void EditAttributesAction(NSObject sender)
        {
            ExperimentDetailsPopoverController.Data = DataManager.Current;

            PerformSegue("DetailsSegue", this);
        }

        partial void ToggleInclusionAction(NSObject sender)
        {
            if (Data == null) return;

            Data.ToggleInclude();

            DataManager.InvokeDataInclusionDidChange();
        }

        void RemoveSolution()
        {
            if (Data == null) return;

            Data.RemoveModel();
        }

        partial void DuplicateDataAction(NSObject sender)
        {
            DataManager.DuplicateSelectedData(Data);
        }

        async void ShowLoadDataPrompt()
        {
            LoadLastButton.Enabled = false;

            if (AppSettings.LastDocumentUrl != null)
            {
                var format = DataReaders.DataReader.GetFormat(AppSettings.LastDocumentUrl.Path);

                if (format != DataReaders.ITCDataFormat.Unknown) LoadLastButton.Enabled = true;

                LoadLastButton.ToolTip = $"Reload the last file ({Path.GetFileName(AppSettings.LastDocumentUrl.Path)}) [SPACE] ";
            }

            LoadDataPrompt.Hidden = DataManager.DataIsLoaded;
        }
    }
}
