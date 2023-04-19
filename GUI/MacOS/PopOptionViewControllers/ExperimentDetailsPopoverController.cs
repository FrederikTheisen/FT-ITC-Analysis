// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using AnalysisITC.GUI.MacOS.CustomViews;
using CoreGraphics;
using AnalysisITC.AppClasses.AnalysisClasses;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public partial class ExperimentDetailsPopoverController : NSViewController
    {
        public static event EventHandler UpdateTable; 

        public static ExperimentData Data { get; set; } = null;
        static List<ModelOptions> tmpoptions = new List<ModelOptions>();
        public static IEnumerable<ModelOptionKey> AllAddedOptions => Data.ExperimentOptions.Select(opt => opt.Key).Concat(tmpoptions.Select(mo => mo.Key).ToList());

        public ExperimentDetailsPopoverController() : base()
        {

        }

		public ExperimentDetailsPopoverController (IntPtr handle) : base (handle)
		{
		}

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();

            tmpoptions.Clear();
            ExperimentNameField.StringValue = Data.FileName;
			CellConcentrationField.DoubleValue = Data.CellConcentration.Value * 1000000;
            if (Data.CellConcentration.HasError) CellConcentrationErrorField.DoubleValue = Data.CellConcentration.SD * 1000000;
			SyringeConcentrationField.DoubleValue = Data.SyringeConcentration * 1000000;
            if (Data.SyringeConcentration.HasError) SyringeConcentrationErrorField.DoubleValue = Data.SyringeConcentration.SD * 1000000;
            TemperatureField.DoubleValue = Data.MeasuredTemperature;

            foreach (var opt in Data.ExperimentOptions)
            {
                AddAttribute(opt);
                //AttributeStackView.AddArrangedSubview(new ExperimentAttributeView(new CGRect(0, 0, AttributeStackView.Frame.Width - 20, 14), opt.Value));
            }

            //if (Data.ExperimentOptions.Count == ModelOptions.AvailableExperimentAttributes.Count) AddAttributeButton.Enabled = false;
        }

        partial void AddAttribute(NSObject sender)
        {
            var opt = new ModelOptions();
            AddAttribute(opt);
        }

        void AddAttribute(ModelOptions opt)
        {
            tmpoptions.Add(opt);

            var sv = new ExperimentAttributeView(new CGRect(0, 0, AttributeStackView.Frame.Width - 20, 14), opt);
            sv.Remove += Sv_Remove;
            sv.KeyChanged += Sv_KeyChanged;

            AttributeStackView.AddArrangedSubview(sv);

            if (AttributeStackView.Subviews.Count() == ModelOptions.AvailableExperimentAttributes.Count) AddAttributeButton.Enabled = false;

        }

        private void Sv_KeyChanged(object sender, EventArgs e) //Update available menu items
        {
            foreach (var sv in AttributeStackView.Views)
            {
                (sv as ExperimentAttributeView).UpdateKeyMenu();
            }
        }

        private void Sv_Remove(object sender, EventArgs e)
        {
            AttributeStackView.RemoveView(sender as NSView);
            AttributeStackView.Layout();

            tmpoptions.Remove((sender as ExperimentAttributeView).Option);

            if (AttributeStackView.Subviews.Count() < ModelOptions.AvailableExperimentAttributes.Count) AddAttributeButton.Enabled = true;

            Sv_KeyChanged(null, null);
        }

        partial void Apply(NSObject sender)
        {
            if (!string.IsNullOrEmpty(SyringeConcentrationField.StringValue)) Data.SyringeConcentration = new(SyringeConcentrationField.DoubleValue / 1000000, SyringeConcentrationErrorField.DoubleValue / 1000000);
            if (!string.IsNullOrEmpty(CellConcentrationField.StringValue)) Data.CellConcentration = new(CellConcentrationField.DoubleValue / 1000000, CellConcentrationErrorField.DoubleValue / 1000000);
            if (!string.IsNullOrEmpty(TemperatureField.StringValue)) Data.MeasuredTemperature = TemperatureField.DoubleValue;
            if (!string.IsNullOrEmpty(ExperimentNameField.StringValue)) Data.FileName = ExperimentNameField.StringValue;

            Data.ExperimentOptions.Clear();

            foreach (var sv in AttributeStackView.Subviews) (sv as ExperimentAttributeView).ApplyOption(Data);

            DataReaders.RawDataReader.ProcessInjections(Data);

            DismissViewController(this);

            UpdateTable?.Invoke(this, null);
        }

        partial void Cancel(NSObject sender)
        {
            DismissViewController(this);
        }
    }
}
