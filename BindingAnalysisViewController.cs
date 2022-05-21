// This file has been autogenerated from a class added in the UI designer.

using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;

namespace AnalysisITC
{
    public partial class ResultViewDataSource : NSTableViewDataSource
    {
        public List<Solution> Data { get; private set; }

        public double KdMag { get; set; } = -1;
        public EnergyUnit EnergyUnit { get; set; } = EnergyUnit.KiloJoule;
        public bool UseKelvin { get; set; } = false;

        public ResultViewDataSource(AnalysisResult result)
        {
            Data = result.Solution.Solutions;
        }

        public override nint GetRowCount(NSTableView tableView)
        {
            return Data.Count;
        }
    }

    public class ResultViewDelegate : NSTableViewDelegate
    {
        const string CellIdentifier = "Cell";

        public double KdMag => DataSource.KdMag;
        public EnergyUnit EnergyUnit => DataSource.EnergyUnit;
        public bool UseKelvin => DataSource.UseKelvin;

        private ResultViewDataSource DataSource;

        public ResultViewDelegate(ResultViewDataSource datasource)
        {
            this.DataSource = datasource;
        }

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            // This pattern allows you reuse existing views when they are no-longer in use.
            // If the returned view is null, you instance up a new view
            // If a non-null view is returned, you modify it enough to reflect the new data
            NSTextField view = (NSTextField)tableView.MakeView(CellIdentifier, this);
            if (view == null)
            {
                view = new NSTextField();
                view.Identifier = CellIdentifier;
                view.BackgroundColor = NSColor.Clear;
                view.Bordered = false;
                view.Selectable = false;
                view.Editable = false;
            }

            // Setup view based on the column selected
            switch (tableColumn.Identifier)
            {
                case "T": view.StringValue = (DataSource.Data[(int)row].T + (UseKelvin ? 273.15 : 0)).ToString("F2"); break;
                case "N": view.StringValue = DataSource.Data[(int)row].N.ToString("F2"); view.Alignment = NSTextAlignment.Center; break;
                case "K": view.StringValue = DataSource.Data[(int)row].Kd.AsDissociationConstant(KdMag, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "H": view.StringValue = DataSource.Data[(int)row].Enthalpy.ToString(EnergyUnit, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "S": view.StringValue = DataSource.Data[(int)row].TdS.ToString(EnergyUnit, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "G": view.StringValue = DataSource.Data[(int)row].GibbsFreeEnergy.ToString(EnergyUnit, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "L": view.StringValue = DataSource.Data[(int)row].Loss.ToString("G3"); view.Alignment = NSTextAlignment.Center; break;
            }

            return view;
        }
    }

	public partial class BindingAnalysisViewController : NSViewController
	{
        public static AnalysisResult AnalysisResult { get; set; }

        EnergyUnit EnergyUnit => (int)EnergyUnitControl.SelectedSegment switch { 0 => EnergyUnit.Joule, 1 => EnergyUnit.KiloJoule, 2 => EnergyUnit.Cal, 3 => EnergyUnit.KCal, _ => EnergyUnit.KiloJoule, };
        public bool UseKelvin => TemperatureUnitControl.SelectedSegment == 1;
        double Mag = -1;

        public BindingAnalysisViewController (IntPtr handle) : base (handle)
		{
            
		}

        public override void ViewDidAppear()
        {
            Graph.Initialize(AnalysisResult);

            Setup();
        }

        void Setup()
        {
            string values = "";

            values += AnalysisResult.Solution.Model.ToString() + Environment.NewLine;
            values += AnalysisResult.Solution.Model.Options.EnthalpyStyle.ToString() + Environment.NewLine;
            values += AnalysisResult.Solution.Model.Options.AffinityStyle.ToString() + Environment.NewLine;
            values += AnalysisResult.Solution.Model.MeanTemperature.ToString("G3") + Environment.NewLine;

            ValueLabel.StringValue = values;
        }

        partial void CopyToClipboard(NSObject sender)
        {
            NSPasteboard.GeneralPasteboard.ClearContents();

            string paste = "";

            foreach (var data in AnalysisResult.Solution.Solutions)
            {
                paste += (data.T + (UseKelvin ? 273.15 : 0)).ToString("F2") + " ";
                paste += data.N.ToString("F2") + " ";
                paste += data.Kd.AsDissociationConstant(Mag, withunit: false) + " ";
                paste += data.Enthalpy.ToString(EnergyUnit, withunit: false) + " ";
                paste += data.TdS.ToString(EnergyUnit, withunit: false) + " ";
                paste += data.GibbsFreeEnergy.ToString(EnergyUnit, withunit: false);
                paste += Environment.NewLine;
            }

            paste = paste.Replace('±', ' ');

            NSPasteboard.GeneralPasteboard.SetStringForType(paste,  "NSStringPboardType");

            StatusBarManager.SetStatus("Results copied to clipboard", 3333);
        }

        partial void CloseButtonClicked(NSObject sender)
        {
            this.DismissViewController(this);
        }
    }
}
