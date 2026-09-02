using System;
using System.Collections.Generic;
using System.Linq;

using AppKit;
using Foundation;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

namespace AnalysisITC
{
    public sealed class ResultViewDataSource : NSTableViewDataSource
    {
        public ResultViewDataSource(
            AnalysisResult result,
            EnergyUnitFamily energyUnitFamily,
            bool useKelvin)
        {
            Presentation = AnalysisResultOverviewTable.Build(
                result,
                energyUnitFamily,
                useKelvin);
            Data = Presentation.Rows
                .Select(row => row.Solution)
                .ToList();
        }

        [Obsolete("Use the family-based constructor so automatic J/kJ or cal/kcal selection is preserved.")]
        public ResultViewDataSource(
            AnalysisResult result,
            EnergyUnit energyUnit,
            bool useKelvin)
        {
            Presentation = AnalysisResultOverviewTable.Build(
                result,
                energyUnit.GetFamily(),
                energyUnit,
                useKelvin);
            Data = Presentation.Rows
                .Select(row => row.Solution)
                .ToList();
        }

        public AnalysisResultOverviewTable Presentation { get; }
        public List<SolutionInterface> Data { get; }

        public string GetCellValue(string columnIdentifier, int row)
        {
            if (row < 0 || row >= Presentation.Rows.Count)
                return string.Empty;

            return Presentation.Rows[row][columnIdentifier];
        }

        public override nint GetRowCount(NSTableView tableView) =>
            Presentation.Rows.Count;
    }

    public sealed class ResultViewDelegate : NSTableViewDelegate
    {
        const string CellIdentifierPrefix = "ResultCell-";
        static readonly NSString ResizedColumnKey = new NSString("NSTableColumn");
        readonly ResultViewDataSource dataSource;
        readonly Action<string, double> columnWidthChanged;

        public ResultViewDelegate(
            ResultViewDataSource dataSource,
            Action<string, double> columnWidthChanged = null)
        {
            this.dataSource = dataSource;
            this.columnWidthChanged = columnWidthChanged;
        }

        public override void ColumnDidResize(NSNotification notification)
        {
            var column = notification.UserInfo?[ResizedColumnKey] as NSTableColumn;
            if (column == null) return;
            columnWidthChanged?.Invoke(column.Identifier, column.Width);
        }

        public override NSView GetViewForItem(
            NSTableView tableView,
            NSTableColumn tableColumn,
            nint row)
        {
            var identifier = CellIdentifierPrefix + tableColumn.Identifier;
            var view = tableView.MakeView(identifier, this) as NSTextField;
            if (view == null)
            {
                view = new NSTextField
                {
                    Identifier = identifier,
                    BackgroundColor = NSColor.Clear,
                    Bordered = false,
                    Selectable = false,
                    Editable = false,
                    LineBreakMode = NSLineBreakMode.TruncatingTail,
                    UsesSingleLineMode = true,
                };
                view.Cell.Wraps = false;
                view.Cell.Scrollable = false;
                view.Cell.UsesSingleLineMode = true;
                view.Cell.LineBreakMode =
                    NSLineBreakMode.TruncatingTail;
                view.Cell.TruncatesLastVisibleLine = true;
                view.WantsLayer = true;
                view.Layer.MasksToBounds = true;
                view.SetContentCompressionResistancePriority(
                    250,
                    NSLayoutConstraintOrientation.Horizontal);
            }

            view.StringValue = dataSource.GetCellValue(
                tableColumn.Identifier,
                (int)row);
            view.ToolTip = view.StringValue;

            var column = dataSource.Presentation.Columns.FirstOrDefault(
                candidate => candidate.Id == tableColumn.Identifier);
            view.Alignment = column?.Alignment switch
            {
                AnalysisResultColumnAlignment.Left => NSTextAlignment.Left,
                AnalysisResultColumnAlignment.Center => NSTextAlignment.Center,
                _ => NSTextAlignment.Right,
            };

            return view;
        }

        public override void SelectionDidChange(NSNotification notification)
        {
            if (notification.Object is not NSTableView tableView)
                return;

            var row = (int)tableView.SelectedRow;
            if (row < 0 || row >= dataSource.Data.Count)
            {
                DataManager.ClearResultSolutionSelection();
                return;
            }

            DataManager.SelectResultSolution(dataSource.Data[row]);
        }
    }
}
