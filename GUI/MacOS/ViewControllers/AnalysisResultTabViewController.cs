// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using System.Linq;
using System.Collections.Generic;
using AnalysisITC.AppClasses.Analysis2;
using CoreServices;
using System.Threading.Tasks;

namespace AnalysisITC
{
	public partial class AnalysisResultTabViewController : NSViewController
	{
        AnalysisResult AnalysisResult { get; set; }
        GlobalSolution Solution => AnalysisResult.Solution;

        EnergyUnit EnergyUnit => AppSettings.EnergyUnit;
        public bool UseKelvin => TempControl.SelectedSegment == 1;
        double Mag = -1;

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
                sr.SRFoldedMode switch { SpolarRecordAnalysisController.SRFoldedMode.Glob => "Globular Mode", SpolarRecordAnalysisController.SRFoldedMode.Intermediate => "Intermediate Mode", SpolarRecordAnalysisController.SRFoldedMode.ID => "ID Interaction Mode"},
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
            ClearUI();

            AnalysisResult = e;

            Graph.Initialize(e, EnergyUnit);

            Setup();
        }

        public void ClearUI()
        {
            SRResultTextField.StringValue = string.Join(Environment.NewLine, new string[] { "-", "-", "-", "-", "-" });
        }

        public void Setup()
        {
            EnergyControl.SelectedSegment = AppSettings.EnergyUnit switch { EnergyUnit.Joule => 0, EnergyUnit.KiloJoule => 1, EnergyUnit.Cal => 2, EnergyUnit.KCal => 3, _ => 1, };

            var kd = Solution.Solutions.Average(s => s.ReportParameters[AppClasses.Analysis2.ParameterTypes.Affinity1]);

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

            while (ResultsTableView.ColumnCount > 0) ResultsTableView.RemoveColumn(ResultsTableView.TableColumns()[0]);

            ResultsTableView.AddColumn(new NSTableColumn("Temp") { Title = "Temperature (" + (UseKelvin ? "K" : "°C") + ")" });
            foreach (var par in Solution.IndividualModelReportParameters)
            {
                ResultsTableView.AddColumn(new NSTableColumn(ParameterTypesAttribute.TableHeaderTitle(par, true))
                {
                    Title = ParameterTypesAttribute.TableHeader(par, Solution.Solutions[0].ParametersConformingToKey(par).Count > 1, EnergyUnit, kdunit),
                });
            }
            ResultsTableView.AddColumn(new NSTableColumn("Loss") { Title = "Loss" });

            ExperimentListButton.Title = Solution.Solutions.Count + " experiments";
            ResultSummaryLabel.StringValue = string.Join(Environment.NewLine, new string[]
            {
                Solution.SolutionName,
                Solution.Convergence.Algorithm.Description(),
                Solution.ErrorEstimationMethod.Description(),
                Solution.ErrorEstimationMethod == ErrorEstimationMethod.None ? "-" : Solution.BootstrapIterations.ToString() });

            var refT = Solution.MeanTemperature;
            if (UseKelvin)
            {
                refT += 273.15;
                ResultEvalTempUnitLabel.StringValue = "K";
            }
            else ResultEvalTempUnitLabel.StringValue = "°C";
            string tempunit = " " + (UseKelvin ? "K" : "°C");

            var dependencies = new List<string>() { refT.ToString("F2") + tempunit + " | " + EnergyUnit.GetUnit() + "/mol" };

            foreach (var dep in Solution.TemperatureDependence) dependencies.Add(dep.Value.ToString(EnergyUnit));

            TemperatureDependenceLabel.StringValue = string.Join(Environment.NewLine, dependencies);

            EvaluateParameters();
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
            EvaluateParameters();
        }

        async void EvaluateParameters()
        {
            StatusBarManager.StartInderminateProgress();
            StatusBarManager.SetStatus("Evaluating...", 0);

            try
            {
                var T = EvaluateionTemperatureTextField.FloatValue;
                if (UseKelvin) T -= 273.15f;

                var unit = EnergyUnit;

                var s = await Task.Run(() =>
                {
                    var H = new Energy(Solution.TemperatureDependence[AppClasses.Analysis2.ParameterTypes.Enthalpy1].Evaluate(T, 100000));
                    var S = new Energy(Solution.TemperatureDependence[AppClasses.Analysis2.ParameterTypes.EntropyContribution1].Evaluate(T, 100000));
                    var G = new Energy(Solution.TemperatureDependence[AppClasses.Analysis2.ParameterTypes.Gibbs1].Evaluate(T, 100000));

                    T += 273.15f;

                    var kdexponent = G / (T * Energy.R);
                    var Kd = FWEMath.Exp(kdexponent.FloatWithError);

                    return string.Join(Environment.NewLine, new string[] { H.ToString(unit, permole: true), S.ToString(unit, permole: true), G.ToString(unit, permole: true), Kd.AsDissociationConstant() });
                });

                EvaluationOutputLabel.StringValue = s;

                StatusBarManager.StopIndeterminateProgress();
                StatusBarManager.ClearAppStatus();
            }
            catch (Exception ex)
            {
                EvaluationOutputLabel.StringValue = string.Join(Environment.NewLine, new string[] { "---", "---", "---", "---" });
                StatusBarManager.StopIndeterminateProgress();
                StatusBarManager.ClearAppStatus();
                StatusBarManager.SetStatusScrolling(ex.Message);
            }
        }

        bool usekelvin = false;
        partial void TempControlClicked(NSSegmentedControl sender)
        {
            if (usekelvin == UseKelvin) return;
            usekelvin = UseKelvin;

            EvaluateionTemperatureTextField.FloatValue += (UseKelvin ? 273.15f : -273.15f); //Fix temperature unit change

            Setup();
        }

        partial void EnergyControlClicked(NSSegmentedControl sender)
        {
            AppSettings.EnergyUnit = (int)EnergyControl.SelectedSegment switch { 0 => EnergyUnit.Joule, 1 => EnergyUnit.KiloJoule, 2 => EnergyUnit.Cal, 3 => EnergyUnit.KCal, _ => EnergyUnit.KiloJoule, };

            Setup();

            Graph.Initialize(AnalysisResult, EnergyUnit);
        }

        partial void CopyToClipboard(NSObject sender)
        {
            FTITCWriter.CopyToClipboard(Solution, Mag, EnergyUnit, UseKelvin);
        }
    }
}
