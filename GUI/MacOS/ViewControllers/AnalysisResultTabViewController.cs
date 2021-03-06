// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using System.Linq;
using System.Collections.Generic;

namespace AnalysisITC
{
	public partial class AnalysisResultTabViewController : NSViewController
	{
        AnalysisResult AnalysisResult { get; set; }

		public AnalysisResultTabViewController (IntPtr handle) : base (handle)
		{
            DataManager.AnalysisResultSelected += DataManager_AnalysisResultSelected;
            SpolarRecordAnalysisController.IterationFinished += SpolarRecordAnalysisController_IterationFinished;
            SpolarRecordAnalysisController.AnalysisFinished += SpolarRecordAnalysisController_AnalysisFinished;
		}

        private void SpolarRecordAnalysisController_AnalysisFinished(object sender, Tuple<int, TimeSpan> e)
        {
            StatusBarManager.StopIndeterminateProgress();
            StatusBarManager.ClearAppStatus();

            if (e != null)
            {
                StatusBarManager.SetStatus(e.Item1 + " iterations, " + e.Item2.TotalMilliseconds + "ms", 6000);
                if (e.Item1 == SpolarRecordAnalysisController.CalculationIterations) StatusBarManager.SetStatus("SR Method Finished", 2000);
                else StatusBarManager.SetStatus("SR Method Stopped", 2000);
            }

            var sr = sender as FTSRMethod;

            var result = new List<string>()
            {
                sr.SRFoldedMode switch { SpolarRecordAnalysisController.SRFoldedMode.Glob => "Globular Mode", SpolarRecordAnalysisController.SRFoldedMode.Intermediate => "Intermediate Mode", SpolarRecordAnalysisController.SRFoldedMode.ID => "ID Interaction Mode", },
                sr.SRTempMode switch { SpolarRecordAnalysisController.SRTempMode.IsoEntropicPoint => "Isoentropic (", SpolarRecordAnalysisController.SRTempMode.MeanTemperature => "Data Set Mean (", SpolarRecordAnalysisController.SRTempMode.ReferenceTemperature => "Set Reference (" } + sr.AnalysisResult.ReferenceTemperature.ToString() + " °C)",
                new Energy(sr.AnalysisResult.HydrationContribution(sr.EvalutationTemperature(false))).ToString(EnergyUnit.KiloJoule, permole: true),
                new Energy(sr.AnalysisResult.ConformationalContribution(sr.EvalutationTemperature(false))).ToString(EnergyUnit.KiloJoule, permole: true),
                sr.AnalysisResult.Rvalue.ToString() + " residues",
            };

            SRResultTextField.StringValue = string.Join(Environment.NewLine, result);

            ToggleFitButtons(true);
        }

        private void SpolarRecordAnalysisController_IterationFinished(object sender, Tuple<int, int, float> e)
        {
            StatusBarManager.Progress = e.Item3;
            StatusBarManager.SetStatus("Calculating...", 0);
            StatusBarManager.SetSecondaryStatus(e.Item1 + "/" + e.Item2, 0);
        }

        private void DataManager_AnalysisResultSelected(object sender, AnalysisResult e)
        {
            AnalysisResult = e;

            Graph.Initialize(e);

            Setup();
        }

        EnergyUnit EnergyUnit => (int)EnergyControl.SelectedSegment switch { 0 => EnergyUnit.Joule, 1 => EnergyUnit.KiloJoule, 2 => EnergyUnit.Cal, 3 => EnergyUnit.KCal, _ => EnergyUnit.KiloJoule, };
        public bool UseKelvin => TempControl.SelectedSegment == 1;
        double Mag = -1;

        public void Setup()
        {
            var kd = AnalysisResult.Solution.Solutions.Average(s => s.Kd);

            Mag = Math.Log10(kd);

            var kdunit = Mag switch
            {
                > 0 => "M",
                > -3 => "mM",
                > -6 => "µM",
                > -9 => "nM",
                > -12 => "pM",
                _ => "M"
            };

            var source = new ResultViewDataSource(AnalysisResult)
            {
                KdMag = Mag,
                EnergyUnit = EnergyUnit,
                UseKelvin = UseKelvin,
            };
            ResultsTableView.DataSource = source;
            ResultsTableView.Delegate = new ResultViewDelegate(source);
            ResultsTableView.TableColumns()[0].Title = "Temperature (" + (UseKelvin ? "K" : "°C") + ")";
            ResultsTableView.TableColumns()[2].Title = "Kd (" + kdunit + ")";
            ResultsTableView.TableColumns()[3].Title = "∆H (" + EnergyUnit.GetUnit() + Energy.Suffix(true) + ")";
            ResultsTableView.TableColumns()[4].Title = "-T∆S (" + EnergyUnit.GetUnit() + Energy.Suffix(true) + ")";
            ResultsTableView.TableColumns()[5].Title = "∆G (" + EnergyUnit.GetUnit() + Energy.Suffix(true) + ")";

            ExperimentListButton.Title = AnalysisResult.Solution.Solutions.Count + " experiments";
            ResultSummaryLabel.StringValue = string.Join(Environment.NewLine, new string[] { AnalysisResult.Solution.Model.Models[0].ModelName, AnalysisResult.Solution.Convergence.Algorithm.Description(), "---" , AnalysisResult.Solution.BootstrapIterations.ToString() });

            var refT = AnalysisResult.Solution.ReferenceTemperature;
            if (UseKelvin)
            {
                refT += 273.15;
                ResultEvalTempUnitLabel.StringValue = "K";
            }
            else ResultEvalTempUnitLabel.StringValue = "°C";
            string tempunit = " " + (UseKelvin ? "K" : "°C") + " / ";

            TemperatureDependenceLabel.StringValue = string.Join(Environment.NewLine, new string[] { refT.ToString("F2") + tempunit + EnergyUnit.GetUnit() + "/mol", AnalysisResult.Solution.EnthalpyLine.ToString(EnergyUnit), AnalysisResult.Solution.EntropyLine.ToString(EnergyUnit), AnalysisResult.Solution.GibbsLine.ToString(EnergyUnit) });

            EvaluateParameters(null);
        }

        void ToggleFitButtons(bool enable)
        {
            SRFitButton.Enabled = enable;
        }

        partial void PerformSRAnalysis(NSObject sender)
        {
            ToggleFitButtons(false);

            SpolarRecordAnalysisController.TempMode = (SpolarRecordAnalysisController.SRTempMode)(int)SRTemperatureModeSegControl.SelectedSegment;
            SpolarRecordAnalysisController.FoldedDegree = (SpolarRecordAnalysisController.SRFoldedMode)(int)SRFoldedDegreeSegControl.SelectedSegment;
            SpolarRecordAnalysisController.Analyze(AnalysisResult.Solution);
        }

        partial void EvaluateParameters(NSObject sender)
        {
            StatusBarManager.StartInderminateProgress();
            StatusBarManager.SetStatus("Evaluating...");

            try
            {
                var T = EvaluateionTemperatureTextField.FloatValue;

                if (UseKelvin) T -= 273.15f;

                var H = new Energy(AnalysisResult.Solution.EnthalpyLine.Evaluate(T, 10000));
                var S = new Energy(AnalysisResult.Solution.EntropyLine.Evaluate(T, 10000));
                var G = new Energy(AnalysisResult.Solution.GibbsLine.Evaluate(T, 10000));

                T += 273.15f;

                var kdexponent = G / (T * Energy.R);
                var Kd = FWEMath.Exp(kdexponent.FloatWithError);

                EvaluationOutputLabel.StringValue = string.Join(Environment.NewLine, new string[] { H.ToString(EnergyUnit,permole:true), S.ToString(EnergyUnit, permole: true), G.ToString(EnergyUnit, permole: true), Kd.AsDissociationConstant() });
            }
            catch (Exception ex)
            {
                EvaluationOutputLabel.StringValue = string.Join(Environment.NewLine, new string[] { "---", "---", "---", "---" });
                StatusBarManager.SetStatusScrolling(ex.Message);
            }

            StatusBarManager.StopIndeterminateProgress();
            StatusBarManager.ClearAppStatus();
        }

        partial void TempControlClicked(NSSegmentedControl sender)
        {
            Setup();
        }

        partial void EnergyControlClicked(NSSegmentedControl sender)
        {
            Setup();
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

            NSPasteboard.GeneralPasteboard.SetStringForType(paste, "NSStringPboardType");

            StatusBarManager.SetStatus("Results copied to clipboard", 3333);
        }
    }
}
