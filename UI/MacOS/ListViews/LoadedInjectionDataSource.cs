using System;
using System.Collections.Generic;
using System.Linq;
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
    public class LoadedInjectionDataSource : NSTableViewDataSource
    {
        public ExperimentData Data { get; private set; }
        public ExperimentOverviewTable Table { get; private set; } = ExperimentOverviewTable.Build(null);
        public IReadOnlyList<ExperimentOverviewColumn> Columns => Table.Columns.Where(column => column.IsVisible).ToList();
        public IReadOnlyList<ExperimentOverviewRow> Rows => Table.Rows;

        public LoadedInjectionDataSource(ExperimentData data)
        {
            SetData(data);
        }

        public void SetData(ExperimentData data)
        {
            Data = data;
            Table = ExperimentOverviewTable.Build(data);
        }

        public override nint GetRowCount(NSTableView tableView) => Rows.Count;
    }

    public class LoadedInjectionTableDelegate : NSTableViewDelegate
    {
        const string CellIdentifier = "LoadedInjectionCell";

        readonly LoadedInjectionDataSource DataSource;

        public LoadedInjectionTableDelegate(LoadedInjectionDataSource dataSource)
        {
            DataSource = dataSource;
        }

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var view = tableView.MakeView(CellIdentifier, this) as NSTextField;
            if (view == null)
            {
                view = new NSTextField
                {
                    Identifier = CellIdentifier,
                    BackgroundColor = NSColor.Clear,
                    Bordered = false,
                    Selectable = false,
                    Editable = false,
                    FocusRingType = NSFocusRingType.None,
                    LineBreakMode = NSLineBreakMode.TruncatingTail,
                };
            }

            if (row < 0 || row >= DataSource.Rows.Count)
            {
                view.StringValue = "";
                return view;
            }

            var rowData = DataSource.Rows[(int)row];
            var column = DataSource.Columns.FirstOrDefault(item => item.Id == tableColumn.Identifier);
            view.Alignment = AlignmentFor(column?.Alignment ?? ExperimentOverviewColumnAlignment.Right);
            view.TextColor = rowData.IsIncluded ? NSColor.Label : NSColor.SecondaryLabel;
            view.StringValue = rowData[tableColumn.Identifier];

            return view;
        }

        [Export("tableView:heightOfRow:")]
        public override nfloat GetRowHeight(NSTableView tableView, nint row) => 22;

        static NSTextAlignment AlignmentFor(ExperimentOverviewColumnAlignment alignment)
        {
            switch (alignment)
            {
                case ExperimentOverviewColumnAlignment.Left: return NSTextAlignment.Left;
                case ExperimentOverviewColumnAlignment.Center: return NSTextAlignment.Center;
                default: return NSTextAlignment.Right;
            }
        }
    }
}
