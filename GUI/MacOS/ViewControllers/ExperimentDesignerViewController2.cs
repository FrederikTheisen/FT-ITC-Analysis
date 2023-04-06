// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using System.Collections.Generic;
using DataReaders;
using AnalysisITC.AppClasses.Analysis2.Models;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.GUI.MacOS.CustomViews;
using System.Linq;

namespace AnalysisITC
{
	public partial class ExperimentDesignerViewController2 : NSViewController
	{
        public static bool AutoRunExperimentSimulation { get; set; } = true;

        private ITCInstrument Instrument { get; set; } = ITCInstrument.MicroCalITC200;
        private ExperimentData Data { get; set; }
        private AnalysisModel Model { get; set; } = AnalysisModel.OneSetOfSites;
        private SingleModelFactory Factory { get; set; }
        private List<ParameterValueAdjustmentView> ParameterControls = new List<ParameterValueAdjustmentView>();

        private double SmallInjectionVolume = 0.5 / 1000000.0;

        private double GetConcFieldValue(NSTextField field, double def = 0) => field.StringValue.Length > 0 ? field.DoubleValue / 1000000 : def / 1000000;
        private FloatWithError CellConcentration
        {
            get
            {
                var conc = GetConcFieldValue(CellConcField, 10);
                var error = GetConcFieldValue(CellConcErrorField);

                return new FloatWithError(conc, error);
            }
        }
        private FloatWithError SyringeConcentration
        {
            get
            {
                var conc = GetConcFieldValue(SyringeConcField, 100);
                var error = GetConcFieldValue(SyringeConcErrorField);

                return new FloatWithError(conc, error);
            }
        }
        private bool UseSmallFirstInjection => SmallInitialInjControl.State == NSCellStateValue.On;

        public ExperimentDesignerViewController2 (IntPtr handle) : base (handle)
		{
		}

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            ModelMenu.RemoveAllItems();
            InstrumentMenu.RemoveAllItems();

            foreach (var instrument in DataReaders.ITCInstrumentAttribute.GetITCInstruments())
            {
                InstrumentMenu.AddItem(new NSMenuItem(instrument.GetProperties().Name) { Tag = (int)instrument });
            }

            foreach (var type in AnalysisModelAttribute.GetAll())
            {
                var att = type.GetProperties();

                var valid = AppClasses.Analysis2.ModelFactory.InitializeFactory(type, false) != null;

                if (true)
                {
                    ModelMenu.AddItem(new NSMenuItem(type.GetProperties().Name) { Tag = (int)type });
                }
            }

            InjectionCountField.Changed += InjectionCountField_Changed;
            InjectionCountStepper.Activated += InjectionCountStepper_Activated;
            ModelControl.Enabled = false;

            SolverInterface.AnalysisStarted += SolverInterface_AnalysisStarted;
            SolverInterface.AnalysisFinished += SolverInterface_AnalysisFinished;
        }

        private void SolverInterface_AnalysisFinished(object sender, SolverConvergence e) => ApplyModelButton.Enabled = true;
        private void SolverInterface_AnalysisStarted(object sender, TerminationFlag e) => ApplyModelButton.Enabled = false;

        partial void SyringeCellAction(NSObject sender)
        {
            SetupExperiment();
        }

        partial void SimulateNoiseControlAction(NSObject sender)
        {
            SetupExperiment();
        }

        private void InjectionCountStepper_Activated(object sender, EventArgs e)
        {
            InjectionCountField.IntValue = InjectionCountStepper.IntValue;

            SetupExperiment();
        }

        private void InjectionCountField_Changed(object sender, EventArgs e)
        {
            InjectionCountStepper.IntValue = InjectionCountField.IntValue;

            SetupExperiment();
        }

        partial void InjectionInputChanged(NSObject sender)
        {
            SetupExperiment();
        }

        partial void InstrumentControlAction(NSPopUpButton sender)
        {
            Instrument = (DataReaders.ITCInstrument)(int)sender.SelectedItem.Tag;

            var instrumentinfo = "";

            instrumentinfo += "Syringe volume:\t " + (1000000 * Instrument.GetProperties().StandardSyringeVolume).ToString("F1") + " µl" + Environment.NewLine;
            instrumentinfo += "Cell volume: \t " + (1000000 * Instrument.GetProperties().StandardCellVolume).ToString("F1") + " µl";

            InstrumentDescriptionField.StringValue = instrumentinfo;

            SetupExperiment();
        }

        partial void ModelControlAction(NSPopUpButton sender)
        {
            SetupModel();
        }

        void SetupExperiment()
        {
            Data = new ExperimentData("TestData");

            Data.Instrument = Instrument;

            Data.CellVolume = Instrument.GetProperties().StandardCellVolume;
            Data.CellConcentration = CellConcentration;
            Data.SyringeConcentration = SyringeConcentration;

            int injcount = Math.Max(InjectionCountField.IntValue, 2);

            double volume = UseSmallFirstInjection ?
                (Instrument.GetProperties().StandardSyringeVolume - SmallInjectionVolume) / (injcount - 1) :
                (Instrument.GetProperties().StandardSyringeVolume) / (injcount);

            volume = Math.Floor(volume * 10000000) / 10000000;

            for (int i = 0; i < injcount; i++)
            {
                if (i == 0 && UseSmallFirstInjection)
                {
                    Data.Injections.Add(new InjectionData(i, SmallInjectionVolume, 0, false));
                }
                else Data.Injections.Add(new InjectionData(i, volume, 0, true));
            }

            RawDataReader.ProcessInjections(Data);

            SetInjectionDescription();

            ModelControl.Enabled = true;

            SetupModel();
        }

        private void SetInjectionDescription()
        {
            string injdescription = "";

            for (int i = 0; i < Data.Injections.Count; i++)
            {
                var curr = Data.Injections[i];
                var next = i < Data.InjectionCount - 1 ? Data.Injections[i + 1] : null;
                var prev = i > 0 ? Data.Injections[i - 1] : null;

                if (Data.Injections.Where(inj => inj.ID > i).All(inj => inj.Volume == curr.Volume))
                {
                    injdescription += "#" + (i + 1).ToString() + "-" + Data.Injections.Count.ToString() + ": " + (1000000 * curr.Volume).ToString("F1") + " µl, ";
                    break;
                }
                else if (next != null && curr.Volume != next.Volume)
                    injdescription += "#" + (i + 1).ToString() + ": " + (1000000 * curr.Volume).ToString("F1") + " µl, ";
                else if (prev != null && curr.Volume != prev.Volume)
                    injdescription += "#" + (i + 1).ToString() + ": " + (1000000 * curr.Volume).ToString("F1") + " µl, ";
            }

            injdescription = injdescription.Substring(0, injdescription.Length - 2);

            InjectionInfoField.StringValue = injdescription;
        }

        void SetupModel()
        {
            if (Data == null) return;
            Factory = new SingleModelFactory(Model);

            Factory.InitializeModel(Data);

            if (Factory == null) return;

            ModelOptionsStackView.Subviews = new NSView[0];

            ParameterValueAdjustmentView[] tmppars = new ParameterValueAdjustmentView[ParameterControls.Count];

            ParameterControls.CopyTo(tmppars);
            ParameterControls.Clear();

            foreach (var par in Factory.GetExposedParameters())
            {
                ParameterValueAdjustmentView sv;

                if (tmppars.ToList().Exists(view => view.Key == par.Key))
                {
                    sv = tmppars.ToList().Find(view => view.Key == par.Key);
                }
                else
                {
                    sv = new ParameterValueAdjustmentView(new CoreGraphics.CGRect(0, 0, ModelOptionsStackView.Frame.Width, 20));
                    sv.EnableLock = false;
                    sv.Setup(par);
                }
                ParameterControls.Add(sv);

                ParameterStackView.AddArrangedSubview(sv);
            }

            bool showoptions = (Factory.Model.ModelOptions.Count > 0);
            ModelOptionsLine.Hidden = !showoptions;
            ModelOptionsLabel.Hidden = !showoptions;

            foreach (var opt in Factory.GetExposedModelOptions())
            {

            }

            if (AutoRunExperimentSimulation) ApplyModelSettings(null);
        }

        partial void ApplyModelSettings(NSButton sender)
        {
            if (Factory == null) return;

            foreach (var sv in ParameterControls)
            {
                Factory.SetCustomParameter(sv.Key, sv.Value, false);
            }

            Factory.BuildModel();

            Data.Model.Solution = SolutionInterface.FromModel(Data.Model, Data.Model.Parameters.ToArray(), SolverConvergence.ReportStopped(DateTime.Now));

            foreach (var inj in Data.Injections)
            {
                var injmass = inj.ID == 0 && UseSmallFirstInjection ? inj.InjectionMass * 0.8 : inj.InjectionMass;
                var dH = Data.Model.EvaluateEnthalpy(inj.ID);
                var noise = SimulateNoiseControl.State == NSCellStateValue.On ?
                    2000 / (Math.Sqrt(inj.InjectionMass * Math.Pow(10, 11))) :
                    0;
                var heat = injmass * new FloatWithError(dH, noise).Sample();
                inj.SetPeakArea(new(heat));
            }

            SimGraphView.Initialize(Data);

            var solver = Solver.Initialize(Factory);
            solver.SolverFunctionTolerance = 1.0E-50;
            solver.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            solver.BootstrapIterations = 50;
            (solver as Solver).Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap = true;

            solver.Analyze();
        }
    }
}
