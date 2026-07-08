using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Tools
{
    public sealed class ExperimentDesignerWindow : Window
    {
        const double MicroliterToLiter = 1.0 / 1_000_000.0;
        const double LiterToMicroliter = 1_000_000.0;
        const double SmallInjectionVolume = 0.5 / 1_000_000.0;
        const double DesignerTandemMixingFraction = 0.20;

        readonly IntegratedHeatsGraphControl graph = new IntegratedHeatsGraphControl();
        readonly ComboBox instrumentCombo = Combo(190);
        readonly ComboBox modelCombo = Combo(190);
        readonly TextBox cellConcentrationBox = TextBox("10");
        readonly TextBox syringeConcentrationBox = TextBox("100");
        readonly TextBox injectionCountBox = TextBox("20");
        readonly TextBox injectionVolumeBox = TextBox("2");
        readonly CheckBox autoVolumeCheck = Check("Automatic injection volume", true);
        readonly CheckBox smallFirstInjectionCheck = Check("Small first injection", true);
        readonly CheckBox simulateNoiseCheck = Check("Simulate noise", false);
        readonly CheckBox tandemCheck = Check("Tandem simulation", false);
        readonly TextBox tandemSegmentCountBox = TextBox("2");
        readonly TextBlock instrumentInfoText = Text();
        readonly TextBlock injectionInfoText = Text();
        readonly TextBlock statusText = Text();
        readonly StackPanel parameterPanel = InspectorPanel();
        readonly StackPanel optionPanel = InspectorPanel();
        readonly Button fitButton = Button("Apply / Fit", 96);

        readonly ITCInstrument[] instruments = ITCInstrumentAttribute.GetITCInstruments().ToArray();
        readonly AnalysisModel[] models = AnalysisModelAttribute.GetAll().ToArray();
        readonly Random random = new Random();

        ExperimentData? data;
        SingleModelFactory? factory;
        bool isUpdating;
        bool isFitting;

        ITCInstrument Instrument => instruments.ElementAtOrDefault(Math.Max(0, instrumentCombo.SelectedIndex));
        AnalysisModel ModelType => models.ElementAtOrDefault(Math.Max(0, modelCombo.SelectedIndex));

        public ExperimentDesignerWindow()
        {
            Title = "Experiment Designer";
            Width = 960;
            Height = 680;
            MinWidth = 780;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            BuildLayout();
            PopulateSelectors();
            WireEvents();
            SetupExperiment();
            SolverInterface.AnalysisFinished += OnAnalysisFinished;
            SolverInterface.AnalysisStarted += OnAnalysisStarted;
        }

        protected override void OnClosed(EventArgs e)
        {
            SolverInterface.AnalysisFinished -= OnAnalysisFinished;
            SolverInterface.AnalysisStarted -= OnAnalysisStarted;
            base.OnClosed(e);
        }

        void BuildLayout()
        {
            fitButton.Click += (_, _) => FitSyntheticData();

            var setupPanel = InspectorPanel();
            setupPanel.Children.Add(Section("Instrument",
                Labeled("Instrument", instrumentCombo),
                instrumentInfoText));
            setupPanel.Children.Add(Section("Concentrations",
                Labeled("Cell uM", cellConcentrationBox),
                Labeled("Syringe uM", syringeConcentrationBox)));
            setupPanel.Children.Add(Section("Injections",
                Labeled("Count", injectionCountBox),
                Labeled("Volume uL", injectionVolumeBox),
                autoVolumeCheck,
                smallFirstInjectionCheck,
                injectionInfoText));
            setupPanel.Children.Add(Section("Tandem",
                tandemCheck,
                Labeled("Segments", tandemSegmentCountBox)));
            setupPanel.Children.Add(Section("Simulation", simulateNoiseCheck));

            var modelPanel = InspectorPanel();
            modelPanel.Children.Add(Section("Model", Labeled("Type", modelCombo)));
            modelPanel.Children.Add(Section("Parameters", parameterPanel));
            modelPanel.Children.Add(Section("Options", optionPanel));

            var tabs = Inspector(
                InspectorTab("Setup", setupPanel),
                InspectorTab("Model", modelPanel));

            Content = WorkspaceControlBuilder.Workspace(
                ContentBorder(graph),
                tabs,
                InspectorFooter(Section("Fit",
                    fitButton,
                    statusText)));
        }

        void PopulateSelectors()
        {
            instrumentCombo.Items.Clear();
            foreach (var instrument in instruments)
                instrumentCombo.Items.Add(instrument.GetProperties().Name);
            var defaultInstrument = Array.IndexOf(instruments, AppSettings.DefaultDesignerInstrument);
            instrumentCombo.SelectedIndex = defaultInstrument >= 0 ? defaultInstrument : 0;

            modelCombo.Items.Clear();
            foreach (var model in models)
                modelCombo.Items.Add(model.GetProperties().Name);
            var defaultModel = Array.IndexOf(models, AnalysisModel.OneSetOfSites);
            modelCombo.SelectedIndex = defaultModel >= 0 ? defaultModel : 0;
        }

        void WireEvents()
        {
            instrumentCombo.SelectionChanged += (_, _) => SetupExperiment();
            modelCombo.SelectionChanged += (_, _) => SetupModel();
            foreach (var textBox in new[] { cellConcentrationBox, syringeConcentrationBox, injectionCountBox, injectionVolumeBox, tandemSegmentCountBox })
            {
                textBox.LostFocus += (_, _) => SetupExperiment();
                textBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) SetupExperiment();
                };
            }

            autoVolumeCheck.IsCheckedChanged += (_, _) => SetupExperiment();
            smallFirstInjectionCheck.IsCheckedChanged += (_, _) => SetupExperiment();
            tandemCheck.IsCheckedChanged += (_, _) => SetupExperiment();
            simulateNoiseCheck.IsCheckedChanged += (_, _) => UpdateSyntheticData();
        }

        void SetupExperiment()
        {
            if (isUpdating) return;

            try
            {
                isUpdating = true;
                var instrument = Instrument;
                data = new ExperimentData("ExperimentDesignerData")
                {
                    Name = "Experiment Designer",
                    Instrument = instrument,
                    CellVolume = instrument.GetProperties().StandardCellVolume,
                    CellConcentration = new FloatWithError(ReadDouble(cellConcentrationBox, 10) / 1_000_000.0, 0),
                    SyringeConcentration = new FloatWithError(ReadDouble(syringeConcentrationBox, 100) / 1_000_000.0, 0),
                    MeasuredTemperature = AppSettings.ReferenceTemperature,
                    TargetTemperature = AppSettings.ReferenceTemperature
                };

                var injectionCount = Math.Max(2, ReadInt(injectionCountBox, 20));
                injectionCountBox.Text = injectionCount.ToString(CultureInfo.CurrentCulture);
                var volume = InjectionVolume(injectionCount);
                var segmentCount = UseTandem ? Math.Max(2, ReadInt(tandemSegmentCountBox, 2)) : 1;
                tandemSegmentCountBox.Text = segmentCount.ToString(CultureInfo.CurrentCulture);

                var segments = new List<TandemConcatenation.TandemInjectionSegment>();
                for (var segment = 0; segment < segmentCount; segment++)
                {
                    var segmentStart = data.Injections.Count;
                    for (var i = 0; i < injectionCount; i++)
                    {
                        var injectionId = data.Injections.Count;
                        var isSmall = i == 0 && smallFirstInjectionCheck.IsChecked == true;
                        data.Injections.Add(new InjectionData(data, injectionId, isSmall ? SmallInjectionVolume : volume, 0, !isSmall));
                    }

                    if (UseTandem)
                        segments.Add(new TandemConcatenation.TandemInjectionSegment(segmentStart, injectionCount, $"Load {segment + 1}"));
                }

                if (UseTandem)
                    TandemConcatenation.ProcessInjectionsWithBackMixing(data, segments, DesignerBackMixingSettings());
                else
                    RawDataReader.ProcessInjections(data);

                instrumentInfoText.Text = $"Syringe volume: {instrument.GetProperties().StandardSyringeVolume * LiterToMicroliter:F1} uL\nCell volume: {instrument.GetProperties().StandardCellVolume * LiterToMicroliter:F1} uL";
                injectionInfoText.Text = InjectionDescription(data);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
            finally
            {
                isUpdating = false;
            }

            SetupModel();
        }

        void SetupModel()
        {
            if (data == null || isFitting) return;

            try
            {
                factory = new SingleModelFactory(ModelType);
                factory.InitializeModel(data);
                foreach (var parameter in factory.GetExposedParameters())
                {
                    if (parameter.Key.GetProperties().ParentType == ParameterType.Enthalpy1)
                        parameter.Update(-30000);
                    if (parameter.Key.GetProperties().ParentType == ParameterType.Nvalue1)
                        parameter.Update(1);
                }

                RebuildParameterRows();
                RebuildOptionRows();
                UpdateSyntheticData();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        void RebuildParameterRows()
        {
            parameterPanel.Children.Clear();
            if (factory == null)
            {
                parameterPanel.Children.Add(Text("No parameters."));
                return;
            }

            foreach (var parameter in factory.GetExposedParameters())
            {
                parameterPanel.Children.Add(AnalysisParameterRowBuilder.Build(
                    parameter,
                    apply: (key, value, locked) =>
                    {
                        factory.UpdateParameter(key, value, locked);
                        UpdateSyntheticData();
                    },
                    reset: key =>
                    {
                        var parameterToReset = factory.GetExposedParameters().FirstOrDefault(parameter => parameter.Key == key);
                        if (parameterToReset != null) factory.ReinitializeParameter(parameterToReset);
                        RebuildParameterRows();
                        UpdateSyntheticData();
                    },
                    setStatus: SetStatus,
                    isUpdating: () => isUpdating));
            }
        }

        void RebuildOptionRows()
        {
            optionPanel.Children.Clear();
            if (factory == null || factory.GetExposedModelOptions().Count == 0)
            {
                optionPanel.Children.Add(Text("No model options for this model."));
                return;
            }

            foreach (var option in factory.GetExposedModelOptions())
                optionPanel.Children.Add(BuildOptionRow(option.Key, option.Value));
        }

        Control BuildOptionRow(AttributeKey key, ExperimentAttribute option)
        {
            var properties = key.GetProperties();
            var title = new TextBlock
            {
                Text = properties.Name,
                FontWeight = FontWeight.SemiBold,
                Foreground = SectionHeaderBrush,
                TextWrapping = TextWrapping.Wrap
            };

            Control editor = properties.Type switch
            {
                ExperimentAttribute.AttributeType.Bool => BoolOptionEditor(option),
                ExperimentAttribute.AttributeType.Int => NumericOptionEditor(option, option.IntValue.ToString(CultureInfo.CurrentCulture), integer: true),
                ExperimentAttribute.AttributeType.Double => NumericOptionEditor(option, option.DoubleValue.ToString("G6", CultureInfo.CurrentCulture), integer: false),
                ExperimentAttribute.AttributeType.Parameter => NumericOptionEditor(option, option.ParameterValue.Value.ToString("G6", CultureInfo.CurrentCulture), integer: false, parameter: true),
                ExperimentAttribute.AttributeType.ParameterAffinity => NumericOptionEditor(option, option.ParameterValue.Value.ToString("G6", CultureInfo.CurrentCulture), integer: false, parameter: true),
                ExperimentAttribute.AttributeType.ParameterConcentration => NumericOptionEditor(option, option.ParameterValue.Value.ToString("G6", CultureInfo.CurrentCulture), integer: false, parameter: true),
                ExperimentAttribute.AttributeType.String => StringOptionEditor(option),
                _ => Text("Read-only: " + option.GetDisplayValue())
            };

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(title);
            panel.Children.Add(editor);
            return new Border
            {
                BorderBrush = SectionBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Child = panel
            };
        }

        Control BoolOptionEditor(ExperimentAttribute option)
        {
            var check = Check("Enabled", option.BoolValue);
            check.IsCheckedChanged += (_, _) =>
            {
                var copy = option.Copy();
                copy.BoolValue = check.IsChecked == true;
                factory?.SetModelOption(copy);
                UpdateSyntheticData();
            };
            return check;
        }

        Control StringOptionEditor(ExperimentAttribute option)
        {
            var box = TextBox(option.StringValue ?? "");
            box.LostFocus += (_, _) =>
            {
                var copy = option.Copy();
                copy.StringValue = box.Text ?? "";
                factory?.SetModelOption(copy);
                UpdateSyntheticData();
            };
            return box;
        }

        Control NumericOptionEditor(ExperimentAttribute option, string text, bool integer, bool parameter = false)
        {
            var box = TextBox(text);
            box.LostFocus += (_, _) =>
            {
                var copy = option.Copy();
                if (integer)
                {
                    if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value))
                    {
                        SetStatus("Invalid option value.");
                        return;
                    }
                    copy.IntValue = value;
                }
                else
                {
                    if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
                    {
                        SetStatus("Invalid option value.");
                        return;
                    }

                    if (parameter) copy.ParameterValue = new FloatWithError(value);
                    else copy.DoubleValue = value;
                }

                factory?.SetModelOption(copy);
                UpdateSyntheticData();
            };
            return box;
        }

        void UpdateSyntheticData()
        {
            if (isFitting || factory == null || data == null) return;

            try
            {
                factory.BuildModel();
                SimulateSyntheticData();
                SetStatus("Synthetic experiment updated.");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        void SimulateSyntheticData()
        {
            if (data?.Model == null) return;

            data.Model.Solution = SolutionInterface.FromModel(data.Model, SolverConvergence.ReportStopped(DateTime.Now));
            foreach (var injection in data.Injections)
            {
                var injectionMass = IsSmallInitialInjection(injection) ? injection.InjectionMass * 0.8 : injection.InjectionMass;
                var enthalpy = data.Model.EvaluateEnthalpy(injection.ID);
                var noise = simulateNoiseCheck.IsChecked == true
                    ? 2000 / Math.Sqrt(Math.Max(1e-30, injection.InjectionMass * Math.Pow(10, 11)))
                    : 0;
                var heat = injectionMass * Sample(new FloatWithError(enthalpy, noise));
                injection.SetPeakArea(new FloatWithError(heat));
            }

            graph.Experiment = data;
            graph.FitToData();
        }

        void FitSyntheticData()
        {
            if (isFitting || factory == null || data?.Model == null) return;

            try
            {
                factory.BuildModel();
                data.UpdateSolution(data.Model);
                graph.Experiment = null;
                graph.Experiment = data;
                graph.FitToData();

                var solver = SolverInterface.Initialize(factory);
                solver.CanCreateAnalysisResult = false;
                solver.SolverToleranceModifier = 2;
                solver.ErrorEstimationMethod = simulateNoiseCheck.IsChecked == true ? ErrorEstimationMethod.BootstrapResiduals : ErrorEstimationMethod.None;
                solver.BootstrapIterations = 50;
                solver.UseErrorWeightedFitting = FittingOptionsController.UseErrorWeightedFitting;
                if (solver is Solver singleSolver)
                {
                    singleSolver.Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap = true;
                    singleSolver.Model.ModelCloneOptions.EnableAutoConcentrationVariance = false;
                }

                solver.Analyze();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        void OnAnalysisStarted(object? sender, TerminationFlag e)
        {
            if (factory == null || sender is not Solver solver || !ReferenceEquals(solver.Model?.Data, data)) return;

            isFitting = true;
            Dispatcher.UIThread.Post(() =>
            {
                fitButton.IsEnabled = false;
                SetStatus("Fitting synthetic experiment...");
            });
        }

        void OnAnalysisFinished(object? sender, SolverConvergence e)
        {
            if (sender is not Solver solver || !ReferenceEquals(solver.Model?.Data, data)) return;

            isFitting = false;
            Dispatcher.UIThread.Post(() =>
            {
                fitButton.IsEnabled = true;
                graph.Experiment = data;
                graph.FitToData();
                SetStatus(e?.Failed == true ? "Fit failed." : "Synthetic fit complete.");
            });
        }

        double InjectionVolume(int injectionCount)
        {
            if (autoVolumeCheck.IsChecked == true)
            {
                var syringeVolume = Instrument.GetProperties().StandardSyringeVolume;
                var volume = smallFirstInjectionCheck.IsChecked == true
                    ? (syringeVolume - SmallInjectionVolume) / Math.Max(1, injectionCount - 1)
                    : syringeVolume / injectionCount;
                injectionVolumeBox.Text = (Math.Floor(volume * 10_000_000) / 10_000_000 * LiterToMicroliter).ToString("G6", CultureInfo.CurrentCulture);
                injectionVolumeBox.IsEnabled = false;
                return volume;
            }

            injectionVolumeBox.IsEnabled = true;
            return Math.Max(0.1, ReadDouble(injectionVolumeBox, 2)) * MicroliterToLiter;
        }

        TandemConcatenation.BackMixingSettings DesignerBackMixingSettings()
        {
            return new TandemConcatenation.BackMixingSettings
            {
                UseBackMixingMethod = true,
                DidRemoveOverflow = true,
                DeadVolume = Instrument.GetProperties().DeadVolume,
                MixingFraction = DesignerTandemMixingFraction,
                RemoveOverflowVolume = 0
            };
        }

        bool IsSmallInitialInjection(InjectionData injection)
        {
            if (smallFirstInjectionCheck.IsChecked != true) return false;
            if (data?.Segments != null)
                return data.Segments.Any(segment => segment.FirstInjectionID == injection.ID);
            return injection.ID == 0;
        }

        bool UseTandem => tandemCheck.IsChecked == true;

        static string InjectionDescription(ExperimentData data)
        {
            if (data.Injections.Count == 0) return "";

            var groups = data.Injections
                .GroupBy(injection => Math.Round(injection.Volume * LiterToMicroliter, 3))
                .Select(group => $"{group.Count()} x {group.Key:G4} uL");
            return string.Join(", ", groups);
        }

        static int ReadInt(TextBox box, int fallback)
        {
            return int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
                ? value
                : fallback;
        }

        static double ReadDouble(TextBox box, double fallback)
        {
            return double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
                ? value
                : fallback;
        }

        double Sample(FloatWithError value)
        {
            if (value.SD <= 0) return value.Value;

            var u1 = 1.0 - random.NextDouble();
            var u2 = 1.0 - random.NextDouble();
            var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return value.Value + normal * value.SD;
        }

        void SetStatus(string message)
        {
            statusText.Text = message ?? "";
        }
    }
}
