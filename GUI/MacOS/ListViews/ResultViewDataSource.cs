﻿// This file has been autogenerated from a class added in the UI designer.

using System;
using System.Collections.Generic;
using AppKit;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.Analysis2.Models;

namespace AnalysisITC
{
    public partial class ResultViewDataSource : NSTableViewDataSource
    {
        public List<SolutionInterface> Data { get; private set; }

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
                case "Temp": view.StringValue = (DataSource.Data[(int)row].Temp + (UseKelvin ? 273.15 : 0)).ToString("F2"); break;
                case "N1": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.Nvalue1].ToString("F3"); view.Alignment = NSTextAlignment.Center; break;
                case "N2": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.Nvalue2].ToString("F3"); view.Alignment = NSTextAlignment.Center; break;
                case "Kd1": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.Affinity1].AsDissociationConstant(KdMag, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "Kd2": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.Affinity2].AsDissociationConstant(KdMag, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "∆H1": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.Enthalpy1].Energy.ToString(EnergyUnit, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "∆H2": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.Enthalpy2].Energy.ToString(EnergyUnit, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "-T∆S1": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.EntropyContribution1].Energy.ToString(EnergyUnit, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "-T∆S2": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.EntropyContribution2].Energy.ToString(EnergyUnit, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "∆G1": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.Gibbs1].Energy.ToString(EnergyUnit, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "∆G2": view.StringValue = DataSource.Data[(int)row].ReportParameters[ParameterTypes.Gibbs2].Energy.ToString(EnergyUnit, withunit: false); view.Alignment = NSTextAlignment.Center; break;
                case "Loss": view.StringValue = DataSource.Data[(int)row].Loss.ToString("G3"); view.Alignment = NSTextAlignment.Center; break;
            }

            return view;
        }
    }
}
