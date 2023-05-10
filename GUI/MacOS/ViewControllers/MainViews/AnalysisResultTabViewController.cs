// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using System.Linq;
using System.Collections.Generic;
using AnalysisITC.AppClasses.Analysis2;
using CoreServices;
using System.Threading.Tasks;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.Utils;

namespace AnalysisITC
{
	public partial class AnalysisResultTabViewController : NSViewController
	{
        AnalysisResult AnalysisResult { get; set; }
        GlobalSolution Solution => AnalysisResult.Solution;

        EnergyUnit EnergyUnit => AppSettings.EnergyUnit;
        ConcentrationUnit AppropriateAutoConcUnit { get; set; } = ConcentrationUnit.µM;
        public bool UseKelvin => TempControl.SelectedSegment == 1;

        ResultGraphView.ResultGraphType DisplayedGraphType = ResultGraphView.ResultGraphType.Parameters;
        //Saving tabview for removal and addition, extremily hack
        NSTabViewItem FoldingAnalysisTab { get; set; }
        NSTabViewItem IonicStrengthAnalysisTab { get; set; }
        NSTabViewItem ProtonationAnalysisTab { get; set; }

        public AnalysisResultTabViewController (IntPtr handle) : base (handle)
		{
            DataManager.AnalysisResultSelected += DataManager_AnalysisResultSelected;
            ResultAnalysisController.IterationFinished += ResultAnalysisProgressReport;
            ResultAnalysisController.AnalysisFinished += ResultsAnalysisCompleted;
            AppDelegate.StartPrintOperation += AppDelegate_StartPrintOperation;
		}

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            TempDependenceResultDescField.AttributedStringValue = MacStrings.FromMarkDownString(string.Join(Environment.NewLine, new List<string>()
            {
                "Reference Temp / Unit:",
                "Enthalpy (" + MarkdownStrings.Enthalpy + "):",
                "Entropy (" + MarkdownStrings.EntropyContribution + "):",
                "Free Energy (" + MarkdownStrings.GibbsFreeEnergy + "):",
            }), NSFont.SystemFontOfSize(11));

            EvalResultDescField.AttributedStringValue = MacStrings.FromMarkDownString(string.Join(Environment.NewLine, new List<string>()
            {
                "Enthalpy (" + MarkdownStrings.Enthalpy + "):",
                "Entropy (" + MarkdownStrings.EntropyContribution + "):",
                "Free Energy (" + MarkdownStrings.GibbsFreeEnergy + "):",
                "Affinity (" + MarkdownStrings.DissociationConstant + "):",
            }), NSFont.SystemFontOfSize(11));

            ElectroResultDescField.AttributedStringValue = MacStrings.FromMarkDownString(string.Join(Environment.NewLine, new List<string>()
            {
                MarkdownStrings.DissociationConstant + " with no salt:",
                MarkdownStrings.DissociationConstant + " at infinite salt:",
                "Electrostatic interaction strength:",
            }), NSFont.SystemFontOfSize(11));

            ProtonationAnalysisResultDescriptionField.AttributedStringValue = MacStrings.FromMarkDownString(string.Join(Environment.NewLine, new List<string>()
            {
                "Protein proton change upon binding:",
                MarkdownStrings.Enthalpy + " at ∆*H*{prot,buffer} = 0:",
            }), NSFont.SystemFontOfSize(11));


            FoldingAnalysisTab = TabView.Item(1);
            IonicStrengthAnalysisTab = TabView.Item(2);
            ProtonationAnalysisTab = TabView.Item(3);

            TabView.DidSelect += TabView_DidSelect;
        }

        private void TabView_DidSelect(object sender, NSTabViewItemEventArgs e)
        {
            if (e.Item == FoldingAnalysisTab)
            {
                DisplayedGraphType = ResultGraphView.ResultGraphType.TemperatureDependence;
            }
            else if (e.Item == IonicStrengthAnalysisTab)
            {
                DisplayedGraphType = ResultGraphView.ResultGraphType.IonicStrengthDependence;
            }
            else if (e.Item == ProtonationAnalysisTab)
            {
                DisplayedGraphType = ResultGraphView.ResultGraphType.ProtonationAnalysis;
            }
            else
            {
                DisplayedGraphType = ResultGraphView.ResultGraphType.Parameters;
            }

            SetupGraphView(DisplayedGraphType);
        }

        private void AppDelegate_StartPrintOperation(object sender, EventArgs e)
        {
            if (StateManager.CurrentState != ProgramState.AnalysisView) return;

            Graph.Print();
        }

        private void ResultsAnalysisCompleted(object sender, Tuple<int, TimeSpan> e)
        {
            StatusBarManager.ClearAppStatus();

            if (e != null)
            {
                StatusBarManager.SetStatus(e.Item1 + " iterations, " + e.Item2.TotalMilliseconds + "ms", 6000);
                if (ResultAnalysisController.TerminateAnalysisFlag.Down) StatusBarManager.SetStatus("Analysis Completed", 2000);
                else StatusBarManager.SetStatus("Analysis Stopped", 2000);
            }
            

            SetupAnalyisResultView(sender);

            SetupGraphView(DisplayedGraphType);

            Graph.Invalidate();

            ToggleFitButtons(true);
        }

        private void ResultAnalysisProgressReport(object sender, Tuple<int, int, float> e)
        {
            StatusBarManager.Progress = e.Item3;
            StatusBarManager.SetStatus("Calculating...", 0);
            StatusBarManager.SetSecondaryStatus(e.Item1 + "/" + e.Item2, 0);
        }

        private void DataManager_AnalysisResultSelected(object sender, AnalysisResult e)
        {
            ClearUI();

            AnalysisResult = e;

            SetupAnalysisTabView();
        }

        public void ClearUI()
        {
            SRResultTextField.StringValue = string.Join(Environment.NewLine, new string[] { "-", "-", "-", "-", "-" });
        }

        public void SetupAnalysisTabView()
        {
            if (AnalysisResult.IsTemperatureDependenceEnabled)
            {
                if (!TabView.Items.Contains(FoldingAnalysisTab)) TabView.Add(FoldingAnalysisTab);
            }
            else if (TabView.Items.Contains(FoldingAnalysisTab)) TabView.Remove(FoldingAnalysisTab);

            if (AnalysisResult.IsElectrostaticsAnalysisDependenceEnabled)
            {
                if (!TabView.Items.Contains(IonicStrengthAnalysisTab)) TabView.Add(IonicStrengthAnalysisTab);
            }
            else if (TabView.Items.Contains(IonicStrengthAnalysisTab)) TabView.Remove(IonicStrengthAnalysisTab);

            if (AnalysisResult.IsProtonationAnalysisEnabled)
            {
                if (!TabView.Items.Contains(ProtonationAnalysisTab)) TabView.Add(ProtonationAnalysisTab);
            }
            else if (TabView.Items.Contains(ProtonationAnalysisTab)) TabView.Remove(ProtonationAnalysisTab);

            SetupResultView();

            SetupGraphView(DisplayedGraphType);
        }

        public void SetupResultView()
        {
            EnergyControl.SelectedSegment = AppSettings.EnergyUnit switch { EnergyUnit.Joule => 0, EnergyUnit.KiloJoule => 1, EnergyUnit.Cal => 2, EnergyUnit.KCal => 3, _ => 1, };

            var kd = Solution.Solutions.Average(s => s.ReportParameters[AppClasses.Analysis2.ParameterType.Affinity1]);

            AppropriateAutoConcUnit = ConcentrationUnitAttribute.FromConc(kd);
            ResultsTableView.SizeToFit();
            var source = new ResultViewDataSource(AnalysisResult)
            {
                KdUnit = AppropriateAutoConcUnit,
                EnergyUnit = EnergyUnit,
                UseKelvin = UseKelvin,
            };
            ResultsTableView.DataSource = source;
            ResultsTableView.Delegate = new ResultViewDelegate(source);

            while (ResultsTableView.ColumnCount > 0) ResultsTableView.RemoveColumn(ResultsTableView.TableColumns()[0]);

            if (AnalysisResult.IsTemperatureDependenceEnabled) ResultsTableView.AddColumn(new NSTableColumn("Temp") { Title = "Temperature (" + (UseKelvin ? "K" : "°C") + ")" });
            if (AnalysisResult.IsElectrostaticsAnalysisDependenceEnabled) ResultsTableView.AddColumn(new NSTableColumn("IS") { Title = "[Ions] (mM)" });
            if (AnalysisResult.IsProtonationAnalysisEnabled) ResultsTableView.AddColumn(new NSTableColumn("HPROT") { Title = "∆H,prot (" + EnergyUnit.GetUnit() + "/mol)" });

            foreach (var par in Solution.IndividualModelReportParameters)
            {
                var column = new NSTableColumn(ParameterTypeAttribute.TableHeaderTitle(par, true))
                {
                    Title = ParameterTypeAttribute.TableHeader(par, Solution.Solutions[0].ParametersConformingToKey(par).Count > 1, EnergyUnit, AppropriateAutoConcUnit.GetName()),
                };
                column.HeaderCell.Alignment = NSTextAlignment.Center;

                ResultsTableView.AddColumn(column);
            }
            ResultsTableView.AddColumn(new NSTableColumn("Loss") { Title = "Loss" });

            ExperimentListButton.Title = Solution.Solutions.Count + " experiments";

            var solverdesc = new List<string>()
            {
                Solution.SolutionName,
                Solution.Convergence.Algorithm.GetProperties().Name + " | RMSD = " + Solution.Loss.ToString("G3"),
                Solution.ErrorEstimationMethod.Description() + (Solution.ErrorEstimationMethod == ErrorEstimationMethod.None ? "" : " x " + Solution.BootstrapIterations.ToString() )
            };

            if (Solution.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap)
            {
                string line = "Conc. error considered";

                if (Solution.ModelCloneOptions.EnableAutoConcentrationVariance)
                    line += " [AUTO: " + (100 * Solution.ModelCloneOptions.AutoConcentrationVariance).ToString("F1") + "%]";

                solverdesc.Add(line);
            }

            ResultSummaryLabel.StringValue = string.Join(Environment.NewLine, solverdesc);

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
            ResultsTableView.SizeToFit();
        }

        void SetupGraphView(ResultGraphView.ResultGraphType type)
        {
            switch (type)
            {
                case ResultGraphView.ResultGraphType.Parameters:
                case ResultGraphView.ResultGraphType.TemperatureDependence: Graph.Setup(type, AnalysisResult); break;
                case ResultGraphView.ResultGraphType.ProtonationAnalysis: Graph.Setup(AnalysisResult.ProtonationAnalysis); break;
                case ResultGraphView.ResultGraphType.IonicStrengthDependence: Graph.Setup(AnalysisResult.ElectrostaticsAnalysis); break;
            }
        }

        void SetupAnalyisResultView(object analysis)
        {
            var result = new List<string>();

            switch (analysis)
            {
                case FTSRMethod sr:
                    result = new List<string>()
                    {
                        sr.FoldedMode switch { FTSRMethod.SRFoldedMode.Glob => "Globular Mode", FTSRMethod.SRFoldedMode.Intermediate => "Intermediate Mode", FTSRMethod.SRFoldedMode.ID => "ID Interaction Mode"},
                        sr.TempMode switch { FTSRMethod.SRTempMode.IsoEntropicPoint => "Isoentropic (", FTSRMethod.SRTempMode.MeanTemperature => "Data Set Mean (", FTSRMethod.SRTempMode.ReferenceTemperature => "Set Reference (" } + sr.Result.ReferenceTemperature.AsNumber() + " °C)",
                        new Energy(sr.Result.HydrationContribution(sr.EvalutationTemperature(false))).ToFormattedString(AppSettings.EnergyUnit, permole: true),
                        new Energy(sr.Result.ConformationalContribution(sr.EvalutationTemperature(false))).ToFormattedString(AppSettings.EnergyUnit, permole: true),
                        sr.Result.Rvalue.AsNumber() + " residues",
                    };

                    SRResultTextField.StringValue = string.Join(Environment.NewLine, result);
                    break;
                case ElectrostaticsAnalysis ea:
                    result = new List<string>()
                    {
                        (ea.Fit as ElectrostaticsFit).Kd0.AsFormattedConcentration(AppropriateAutoConcUnit, withunit: true),
                        (ea.Fit as ElectrostaticsFit).Plateau.AsFormattedConcentration(AppropriateAutoConcUnit, withunit: true),
                        ea.ElectrostaticStrength.ToFormattedString(AppSettings.EnergyUnit,withunit: true, permole: true)
                    };
                    ElectrostaticAnalysisOutput.StringValue = string.Join(Environment.NewLine, result);
                    break;
                case ProtonationAnalysis pa:
                    {
                        result = new List<string>()
                        {
                            (-1 * (pa.Fit as LinearFitWithError).Slope).AsNumber(),
                            new Energy((pa.Fit as LinearFitWithError).Evaluate(0)).ToFormattedString(AppSettings.EnergyUnit, true, true, false)
                        };
                        ProtonationAnalysisOutput.StringValue = string.Join(Environment.NewLine, result);
                    }
                    break;
            }
        }

        void ToggleFitButtons(bool enable)
        {
            SRFitButton.Enabled = enable;
        }

        partial void PerformSRAnalysis(NSObject sender)
        {
            ToggleFitButtons(false);

            var tempmode = (FTSRMethod.SRTempMode)(int)SRTemperatureModeSegControl.SelectedSegment;
            var foldedmode = (FTSRMethod.SRFoldedMode)(int)SRFoldedDegreeSegControl.SelectedSegment;

            AnalysisResult.SpolarRecordAnalysis.TempMode = tempmode;
            AnalysisResult.SpolarRecordAnalysis.FoldedMode = foldedmode;
            AnalysisResult.SpolarRecordAnalysis.PerformAnalysis();
        }

        partial void PerformProtonationAnalysis(NSButton sender)
        {
            ToggleFitButtons(false);

            AnalysisResult.ProtonationAnalysis.PerformAnalysis();
        }

        partial void PeformIonicStrengthAnalysis(NSButton sender)
        {
            ToggleFitButtons(false);

            AnalysisResult.ElectrostaticsAnalysis.Model = (ElectrostaticsAnalysis.DissocFitMode)(int)ElectrostaticAnalysisModel.SelectedSegment;
            AnalysisResult.ElectrostaticsAnalysis.PerformAnalysis();
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
                    var H = new Energy(Solution.TemperatureDependence[AppClasses.Analysis2.ParameterType.Enthalpy1].Evaluate(T, 100000));
                    var S = new Energy(Solution.TemperatureDependence[AppClasses.Analysis2.ParameterType.EntropyContribution1].Evaluate(T, 100000));
                    var G = new Energy(Solution.TemperatureDependence[AppClasses.Analysis2.ParameterType.Gibbs1].Evaluate(T, 100000));

                    T += 273.15f;

                    var kdexponent = G / (T * Energy.R);
                    var Kd = FWEMath.Exp(kdexponent.FloatWithError);

                    return string.Join(Environment.NewLine, new string[] { H.ToFormattedString(unit, permole: true), S.ToFormattedString(unit, permole: true), G.ToFormattedString(unit, permole: true), Kd.AsFormattedConcentration(true) });
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

            SetupResultView();
        }

        partial void EnergyControlClicked(NSSegmentedControl sender)
        {
            AppSettings.EnergyUnit = (int)EnergyControl.SelectedSegment switch { 0 => EnergyUnit.Joule, 1 => EnergyUnit.KiloJoule, 2 => EnergyUnit.Cal, 3 => EnergyUnit.KCal, _ => EnergyUnit.KiloJoule, };

            SetupResultView();

            Graph.Invalidate();
        }

        partial void CopyToClipboard(NSObject sender)
        {
            Exporter.CopyToClipboard(AnalysisResult, AppropriateAutoConcUnit, EnergyUnit, UseKelvin);
        }
    }
}
