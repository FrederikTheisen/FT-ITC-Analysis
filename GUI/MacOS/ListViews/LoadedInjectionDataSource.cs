using System;
using System.Collections.Generic;
using AppKit;
using Foundation;

namespace AnalysisITC
{
    public class LoadedInjectionDataSource : NSTableViewDataSource
    {
        public ExperimentData Data { get; private set; }
        public List<InjectionData> Injections => Data?.Injections ?? new List<InjectionData>();

        public LoadedInjectionDataSource(ExperimentData data)
        {
            Data = data;
        }

        public void SetData(ExperimentData data)
        {
            Data = data;
        }

        public override nint GetRowCount(NSTableView tableView) => Injections.Count;
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

            if (row < 0 || row >= DataSource.Injections.Count)
            {
                view.StringValue = "";
                return view;
            }

            var inj = DataSource.Injections[(int)row];
            view.Alignment = NSTextAlignment.Right;

            switch (tableColumn.Identifier)
            {
                case "ID":
                    view.StringValue = (inj.ID + 1).ToString();
                    view.Alignment = NSTextAlignment.Center;
                    break;
                case "Volume":
                    view.StringValue = (1000000 * inj.Volume).ToString("F1");
                    break;
                case "M":
                    view.StringValue = new FloatWithError(inj.ActualCellConcentration).AsConcentration(ConcentrationUnit.µM, withunit: false);
                    break;
                case "L":
                    view.StringValue = new FloatWithError(inj.ActualTitrantConcentration).AsConcentration(ConcentrationUnit.µM, withunit: false);
                    break;
                case "Ratio":
                    view.StringValue = inj.Ratio.ToString("G5");
                    break;
                case "NormHeat":
                    view.StringValue = inj.Enthalpy2.ToFormattedString(AppSettings.EnergyUnit, withunit: false, permole: true);
                    break;
                default:
                    view.StringValue = "";
                    break;
            }

            return view;
        }

        [Export("tableView:heightOfRow:")]
        public override nfloat GetRowHeight(NSTableView tableView, nint row) => 22;
    }
}
