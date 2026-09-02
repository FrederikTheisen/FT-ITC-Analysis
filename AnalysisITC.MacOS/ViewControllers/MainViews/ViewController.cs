using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppKit;
using Foundation;
using CoreGraphics;
using MathNet.Numerics.LinearAlgebra.Solvers;
using MathNet.Numerics.LinearAlgebra.Double;
using AnalysisITC.UI.MacOS;

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
    public partial class ViewController : NSViewController
    {
        ExperimentData Data => DataManager.Current;

        NSScrollView LoadedInjectionScrollView;
        NSTableView LoadedInjectionTableView;
        LoadedInjectionDataSource LoadedInjectionSource;
        LoadedInjectionTableDelegate LoadedInjectionDelegate;
        NSScrollView OverviewInfoScrollView;
        NSStackView OverviewInfoStackView;
        NSLayoutConstraint OverviewInfoPreferredHeightConstraint;
        NSFont OverviewInfoFont;
        bool isLoadingRecentData;
        static readonly nfloat OverviewInfoMinimumHeight = 60;
        static readonly nfloat OverviewInfoMaximumHeight = 220;
        public static bool OverviewShowsInjections { get; private set; }
        public static event EventHandler OverviewDisplayModeDidChange;

        public static void SetOverviewDisplayMode(bool showInjections)
        {
            if (OverviewShowsInjections == showInjections) return;

            OverviewShowsInjections = showInjections;
            OverviewDisplayModeDidChange?.Invoke(null, EventArgs.Empty);
        }

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
            OverviewDisplayModeDidChange += OnOverviewDisplayModeDidChange;
            StateManager.UpdateStateDependentUI += StateManager_UpdateStateDependentUI;
            AppDelegate.StartPrintOperation += AppDelegate_StartPrintOperation;
            BindExperimentMenuActions(ExperimentMenuButton.Menu);
            SetupLoadedInjectionTable();
            SetupOverviewInfoTable();

            ShowLoadDataPrompt();
        }

        public override void ViewDidAppear()
        {
             base.ViewDidAppear();

            UpdateGraph();
            ShowLoadDataPrompt();
        }

        public override void ViewDidLayout()
        {
            base.ViewDidLayout();

            ResizeLoadedInjectionColumns();
            UpdateOverviewInfoScrollHeight();
        }

        void OnOverviewDisplayModeDidChange(object sender, EventArgs e) => UpdateGraph();

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
                FocusRingType = NSFocusRingType.None,
                TranslatesAutoresizingMaskIntoConstraints = true,
                //BackgroundColor = NSColor.WindowBackground,
            };
            LoadedInjectionTableView.Frame = GVC.Frame;
            LoadedInjectionTableView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;

            LoadedInjectionSource = new LoadedInjectionDataSource(null);
            LoadedInjectionDelegate = new LoadedInjectionTableDelegate(LoadedInjectionSource);
            LoadedInjectionTableView.DataSource = LoadedInjectionSource;
            LoadedInjectionTableView.Delegate = LoadedInjectionDelegate;
            RebuildLoadedInjectionColumns();

            LoadedInjectionScrollView = new NSScrollView
            {
                DocumentView = LoadedInjectionTableView,
                HasVerticalScroller = true,
                HasHorizontalScroller = true,
                HorizontalScrollElasticity = NSScrollElasticity.None,
                AutohidesScrollers = true,
                BorderType = NSBorderType.NoBorder,
                DrawsBackground = true,
                BackgroundColor = NSColor.WindowBackground,
                FocusRingType = NSFocusRingType.None,
                TranslatesAutoresizingMaskIntoConstraints = false,
                Hidden = true,
            };

            var container = GVC.Superview;
            container.AddSubview(LoadedInjectionScrollView);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                // Start below the header separator so it remains visible in table mode.
                LoadedInjectionScrollView.TopAnchor.ConstraintEqualToAnchor(GVC.TopAnchor),
                LoadedInjectionScrollView.BottomAnchor.ConstraintEqualToAnchor(container.BottomAnchor),
                LoadedInjectionScrollView.LeadingAnchor.ConstraintEqualToAnchor(container.LeadingAnchor),
                LoadedInjectionScrollView.TrailingAnchor.ConstraintEqualToAnchor(container.TrailingAnchor),
            });
        }

        void SetupOverviewInfoTable()
        {
            OverviewInfoScrollView = InfoLabel?.EnclosingScrollView;
            OverviewInfoStackView = InfoLabel?.Superview as NSStackView;
            if (OverviewInfoStackView == null) return;

            OverviewInfoFont = InfoLabel.Font ?? NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
            OverviewInfoStackView.RemoveArrangedSubview(InfoLabel);
            InfoLabel.RemoveFromSuperview();

            if (OverviewInfoScrollView != null)
            {
                OverviewInfoPreferredHeightConstraint = OverviewInfoScrollView.HeightAnchor.ConstraintEqualToConstant(OverviewInfoMinimumHeight);
                OverviewInfoPreferredHeightConstraint.Priority = 750;
                OverviewInfoPreferredHeightConstraint.Active = true;
            }
        }

        void UpdateOverviewInfoScrollHeight(bool resetScrollPosition = false)
        {
            if (OverviewInfoScrollView == null
                || OverviewInfoStackView == null
                || OverviewInfoPreferredHeightConstraint == null) return;

            OverviewInfoStackView.LayoutSubtreeIfNeeded();

            var contentHeight = OverviewInfoStackView.FittingSize.Height;
            var preferredHeight = contentHeight;
            if (preferredHeight < OverviewInfoMinimumHeight)
                preferredHeight = OverviewInfoMinimumHeight;
            else if (preferredHeight > OverviewInfoMaximumHeight)
                preferredHeight = OverviewInfoMaximumHeight;

            if (Math.Abs(OverviewInfoPreferredHeightConstraint.Constant - preferredHeight) > 0.5)
                OverviewInfoPreferredHeightConstraint.Constant = preferredHeight;

            if (!resetScrollPosition) return;

            OverviewInfoScrollView.ContentView.ScrollToPoint(CGPoint.Empty);
            OverviewInfoScrollView.ReflectScrolledClipView(OverviewInfoScrollView.ContentView);
        }

        void RebuildLoadedInjectionColumns()
        {
            if (LoadedInjectionTableView == null || LoadedInjectionSource == null) return;

            foreach (var column in LoadedInjectionTableView.TableColumns())
                LoadedInjectionTableView.RemoveColumn(column);

            foreach (var column in LoadedInjectionSource.Columns)
                AddColumn(column);
        }

        void AddColumn(ExperimentOverviewColumn overviewColumn)
        {
            var column = new NSTableColumn(overviewColumn.Id)
            {
                Identifier = overviewColumn.Id,
                Width = (nfloat)overviewColumn.PreferredWidth,
                MinWidth = (nfloat)Math.Min(overviewColumn.PreferredWidth, 60),
                ResizingMask = NSTableColumnResizing.UserResizingMask,
                Editable = false,
                HeaderCell = new NSTableHeaderCell(overviewColumn.Title)
                {
                    Alignment = HeaderAlignmentFor(overviewColumn.Alignment),
                },
            };

            LoadedInjectionTableView.AddColumn(column);
        }

        void ResizeLoadedInjectionColumns()
        {
            if (LoadedInjectionTableView == null || LoadedInjectionTableView.TableColumns() == null) return;

            var scrollWidth = LoadedInjectionScrollView?.ContentSize.Width ?? 0;
            var graphWidth = GVC?.Frame.Width ?? 0;
            var width = Math.Max(scrollWidth > 0 ? scrollWidth : graphWidth, 0);
            if (width <= 0) return;

            var columns = LoadedInjectionTableView.TableColumns();
            if (columns.Length == 0) return;

            var intercellWidth = LoadedInjectionTableView.IntercellSpacing.Width * Math.Max(columns.Length - 1, 0);
            var usableWidth = Math.Max(width - intercellWidth, 240);
            var preferredWidth = LoadedInjectionSource.Columns.Sum(column => column.PreferredWidth);
            var widthFactor = preferredWidth > 0 ? Math.Max(1, usableWidth / preferredWidth) : 1;

            foreach (var column in columns)
            {
                var overviewColumn = LoadedInjectionSource.Columns.FirstOrDefault(item => item.Id == column.Identifier);
                var columnWidth = (overviewColumn?.PreferredWidth ?? 80) * widthFactor;

                column.Width = (nfloat)columnWidth;
                column.MinWidth = (nfloat)Math.Min(columnWidth, 60);
            }

            var headerHeight = LoadedInjectionTableView.HeaderView?.Frame.Height ?? 0;
            var rowHeight = LoadedInjectionDelegate?.GetRowHeight(LoadedInjectionTableView, 0) ?? 22;
            var tableHeight = Math.Max(LoadedInjectionScrollView?.ContentSize.Height ?? 0, headerHeight + rowHeight * LoadedInjectionSource.Rows.Count);
            LoadedInjectionTableView.Frame = new CGRect(0, 0, width, tableHeight);
        }

        static NSTextAlignment HeaderAlignmentFor(ExperimentOverviewColumnAlignment alignment)
        {
            switch (alignment)
            {
                case ExperimentOverviewColumnAlignment.Left: return NSTextAlignment.Left;
                case ExperimentOverviewColumnAlignment.Center: return NSTextAlignment.Center;
                default: return NSTextAlignment.Right;
            }
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
                case "context-copyattributes":
                    DataManager.CopySelectedAttributesToAll();
                    break;
                case "toggleinclude":
                    ToggleInclusionAction(null);
                    break;
                case "duplicate":
                    DataManager.DuplicateSelectedData(Data);
                    break;
                case "export":
                case "context-export":
                    Exporter.Export(null, ExportDataSelection.SelectedData);
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
            var dataName = string.IsNullOrWhiteSpace(DataManager.Current.Name) ? DataManager.Current.FileName : DataManager.Current.Name;
            if (!ConfirmationDialog.ConfirmRemoveOrDelete(
                "Confirm Delete Data",
                $"Are you sure you wish to delete {dataName}?",
                "Delete Data")) return;

            DataManager.RemoveSourceItemAt(DataManager.SelectedContentIndex);
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

        async partial void LoadLastFile(NSObject sender)
        {
            if (isLoadingRecentData) return;

            isLoadingRecentData = true;
            LoadLastButton.Enabled = false;

            try
            {
                var recentPaths = AppSettings.LastDocumentPaths;
                if (recentPaths != null && recentPaths.Length > 0)
                {
                    await AnalysisITC.UI.MacOS.MacDataReader.ReadAsync(recentPaths.Select(path => NSUrl.CreateFileUrl(path, null)));
                }
                else if (!string.IsNullOrWhiteSpace(AppSettings.LastDocumentPath))
                {
                    await AnalysisITC.UI.MacOS.MacDataReader.ReadAsync(new[] { NSUrl.CreateFileUrl(AppSettings.LastDocumentPath, null) });
                }
            }
            finally
            {
                isLoadingRecentData = false;
                ShowLoadDataPrompt();
            }
        }

        private void OnSelectionChanged(object sender, ExperimentData e) => UpdateGraph();
        private void OnDataChanged(object sender, ExperimentData e)
        {
            UpdateGraph();
            ShowLoadDataPrompt();
        }

        private void UpdateGraph()
        {
            var showLoadedInjectionTable = DataManager.Current != null && OverviewShowsInjections;

            //GVC.Hidden = DataManager.Current == null;
            GVC.AlphaValue = showLoadedInjectionTable ? 0 : 1;
            LoadedInjectionScrollView.Hidden = !showLoadedInjectionTable;
            LoadedInjectionTableView.Hidden = !showLoadedInjectionTable;

            GVC.Initialize(DataManager.Current);

            if (showLoadedInjectionTable)
            {
                LoadedInjectionSource.SetData(DataManager.Current);
                RebuildLoadedInjectionColumns();
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
            if (OverviewInfoStackView == null)
            {
                InfoLabel.StringValue = Data == null
                    ? ""
                    : string.Join(Environment.NewLine, Data.GetInfoString());
                return;
            }

            foreach (var view in OverviewInfoStackView.ArrangedSubviews.ToArray())
            {
                OverviewInfoStackView.RemoveArrangedSubview(view);
                view.RemoveFromSuperview();
                view.Dispose();
            }

            if (Data == null)
            {
                UpdateOverviewInfoScrollHeight(resetScrollPosition: true);
                return;
            }

            var lines = Data.GetInfoString()
                .Select(OverviewPlainText)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            var rows = lines
                .Select(OverviewInfoRow)
                .ToArray();
            if (rows.Length == 0)
            {
                UpdateOverviewInfoScrollHeight(resetScrollPosition: true);
                return;
            }

            var grid = NSGridView.Create(rows);
            grid.ColumnSpacing = 8;
            grid.RowSpacing = 2;
            grid.RowAlignment = NSGridRowAlignment.FirstBaseline;
            grid.X = NSGridCellPlacement.Fill;
            grid.Y = NSGridCellPlacement.Center;
            grid.GetColumn(0).Width = 135;
            grid.GetColumn(0).X = NSGridCellPlacement.Leading;
            grid.GetColumn(1).X = NSGridCellPlacement.Fill;
            for (var rowIndex = 0; rowIndex < lines.Length; rowIndex++)
            {
                if (!lines[rowIndex].Contains(Environment.NewLine)
                    && !lines[rowIndex].Contains("\n"))
                    continue;

                // Baseline alignment adds space above a wrapping NSTextField.
                // Top-align multiline overview rows so their first line starts
                // alongside the row label.
                grid.GetRow(rowIndex).RowAlignment = NSGridRowAlignment.None;
                grid.GetCell(0, rowIndex).Y = NSGridCellPlacement.Top;
                grid.GetCell(1, rowIndex).Y = NSGridCellPlacement.Top;
            }
            grid.TranslatesAutoresizingMaskIntoConstraints = false;
            OverviewInfoStackView.AddArrangedSubview(grid);
            OverviewInfoStackView.AddConstraint(NSLayoutConstraint.Create(
                grid,
                NSLayoutAttribute.Width,
                NSLayoutRelation.Equal,
                OverviewInfoStackView,
                NSLayoutAttribute.Width,
                1,
                -10));

            OverviewInfoStackView.LayoutSubtreeIfNeeded();
            UpdateOverviewInfoScrollHeight(resetScrollPosition: true);
        }

        NSView[] OverviewInfoRow(string line)
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && separator < 28)
            {
                return new NSView[]
                {
                    CreateOverviewInfoLabel(line.Substring(0, separator).TrimEnd(), true),
                    CreateOverviewInfoLabel(line.Substring(separator + 1).Trim(), false),
                };
            }

            return new NSView[]
            {
                CreateOverviewInfoLabel("", true),
                CreateOverviewInfoLabel(line, false),
            };
        }

        NSTextField CreateOverviewInfoLabel(string text, bool isHeader)
        {
            var label = new NSTextField
            {
                StringValue = text ?? "",
                Bordered = false,
                Editable = false,
                Selectable = false,
                DrawsBackground = false,
                FocusRingType = NSFocusRingType.None,
                Font = OverviewInfoFont ?? NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                TextColor = isHeader ? NSColor.SecondaryLabel : NSColor.Label,
                Alignment = NSTextAlignment.Left,
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
                MaximumNumberOfLines = 0,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            label.Cell.Wraps = true;
            label.Cell.UsesSingleLineMode = false;
            label.SetContentHuggingPriorityForOrientation(
                isHeader ? 750 : 249,
                NSLayoutConstraintOrientation.Horizontal);
            label.SetContentCompressionResistancePriority(
                isHeader ? 1000 : 250,
                NSLayoutConstraintOrientation.Horizontal);
            return label;
        }

        static string OverviewPlainText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            return string.Concat(MarkdownProcessor.GetSegments(text).Select(segment => segment.Text))
                .Replace("∆", "Δ")
                .TrimEnd();
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
        }

        void RemoveSolution()
        {
            if (Data == null) return;
            var dataName = string.IsNullOrWhiteSpace(Data.Name) ? Data.FileName : Data.Name;
            if (!ConfirmationDialog.ConfirmRemoveOrDelete(
                "Confirm Clear Solution",
                $"Are you sure you wish to clear the fitted solution for {dataName}?",
                "Clear Solution")) return;

            Data.RemoveModel();
        }

        partial void DuplicateDataAction(NSObject sender)
        {
            DataManager.DuplicateSelectedData(Data);
        }

        void ShowLoadDataPrompt()
        {
            LoadLastButton.Enabled = false;

            var lastDocumentPath = AppSettings.LastDocumentPath;
            if (!string.IsNullOrWhiteSpace(lastDocumentPath))
            {
                var format = AnalysisITC.Core.DataReaders.DataReader.GetFormat(lastDocumentPath);
                var fileName = Path.GetFileName(lastDocumentPath);

                if (format != AnalysisITC.Core.DataReaders.ITCDataFormat.Unknown && !isLoadingRecentData) LoadLastButton.Enabled = true;

                LastOpenedFileLabel.StringValue = $"Last opened file: {fileName}";
                LastOpenedFileLabel.ToolTip = lastDocumentPath;
                LoadLastButton.ToolTip = $"Reload the last file ({fileName}) [SPACE] ";
            }
            else
            {
                LastOpenedFileLabel.StringValue = "No recently opened file";
                LastOpenedFileLabel.ToolTip = null;
            }

            LoadDataPrompt.Hidden = DataManager.DataIsLoaded;
        }
    }

    [Register("OverviewInfoDocumentView")]
    internal sealed class OverviewInfoDocumentView : NSView
    {
        public OverviewInfoDocumentView(IntPtr handle) : base(handle) { }

        public override bool IsFlipped => true;
    }
}
