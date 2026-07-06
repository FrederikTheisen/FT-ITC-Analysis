using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

using SkiaSharp;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.FinalFigure
{
    public sealed class FinalFigureWorkspaceControl : UserControl
    {
        static readonly EnergyUnit[] EnergyUnits = EnergyUnitAttribute.GetSelectableUnits().ToArray();
        static readonly TimeUnit[] TimeUnits = { TimeUnit.Second, TimeUnit.Minute, TimeUnit.Hour };
        static readonly UncertaintyDisplayStyle[] UncertaintyStyles =
        {
            UncertaintyDisplayStyle.Automatic,
            UncertaintyDisplayStyle.StandardDeviation,
            UncertaintyDisplayStyle.ConfidenceInterval,
            UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            UncertaintyDisplayStyle.None
        };

        readonly SkiaFigureRenderer renderer = new SkiaFigureRenderer();
        readonly Border previewHost = new Border();
        readonly Image image = new Image
        {
            Stretch = Stretch.Uniform
        };
        readonly TextBlock statusText = Text();

        readonly Button fitPageButton = Button("Fit Page", 86);
        readonly Button exportPdfButton = Button("Export PDF", 96);

        readonly TextBox widthBox = TextBox("6");
        readonly TextBox heightBox = TextBox("10");
        readonly ComboBox energyUnitCombo = Combo(EnergyUnits.Select(unit => unit.GetUnit()).ToArray(), 0, 126);
        readonly ComboBox timeUnitCombo = Combo(TimeUnits.Select(unit => unit.GetProperties().Name).ToArray(), 1, 126);
        readonly ComboBox uncertaintyCombo = Combo(new[] { "Automatic", "SD", "CI", "SD + CI", "None" }, 1, 126);
        readonly ComboBox infoPlacementCombo = Combo(new[] { "Auto", "Upper", "Lower" }, 0, 126);

        readonly CheckBox showThermogramCheck = Check("Data graph", true);
        readonly CheckBox axisTitlesCheck = Check("Axis titles", true);
        readonly CheckBox sanitizeTicksCheck = Check("Nice ticks", true);
        readonly CheckBox experimentDetailsCheck = Check("Experiment details", true);
        readonly CheckBox modelInfoCheck = Check("Model info", true);
        readonly CheckBox fitParametersCheck = Check("Fit parameters", true);
        readonly CheckBox thermodynamicCheck = Check("Thermodynamic", true);
        readonly CheckBox derivedCheck = Check("Derived", true);
        readonly CheckBox offsetParameterCheck = Check("Offset parameter", false);
        readonly CheckBox temperatureCheck = Check("Temperature", true);
        readonly CheckBox concentrationsCheck = Check("Concentrations", true);
        readonly CheckBox injectionDelayCheck = Check("Injection delay", false);
        readonly CheckBox instrumentCheck = Check("Instrument", false);
        readonly CheckBox attributesCheck = Check("Attributes", true);

        readonly TextBox powerAxisTitleBox = TextBox("Differential Power (<unit>)");
        readonly TextBox timeAxisTitleBox = TextBox("Time (<unit>)");
        readonly TextBox dataXTickBox = TextBox("7");
        readonly TextBox dataYTickBox = TextBox("7");
        readonly TextBox dataXMinBox = TextBox("");
        readonly TextBox dataXMaxBox = TextBox("");
        readonly TextBox dataYMinBox = TextBox("");
        readonly TextBox dataYMaxBox = TextBox("");
        readonly CheckBox correctedDataCheck = Check("Corrected data", true);

        readonly TextBox enthalpyAxisTitleBox = TextBox("<unit> of injectant");
        readonly TextBox fitXAxisTitleBox = TextBox("");
        readonly TextBox fitXTickBox = TextBox("7");
        readonly TextBox fitYTickBox = TextBox("7");
        readonly TextBox fitXMinBox = TextBox("");
        readonly TextBox fitXMaxBox = TextBox("");
        readonly TextBox fitYMinBox = TextBox("");
        readonly TextBox fitYMaxBox = TextBox("");
        readonly TextBox residualYMinBox = TextBox("");
        readonly TextBox residualYMaxBox = TextBox("");
        readonly TextBox residualYTickBox = TextBox("3");
        readonly TextBox residualFractionBox = TextBox("0.2");
        readonly TextBox symbolSizeBox = TextBox("6");
        readonly ComboBox symbolCombo = Combo(new[] { "Square", "Circle" }, 0, 126);
        readonly CheckBox residualsCheck = Check("Residuals", true);
        readonly CheckBox residualGapCheck = Check("Residual gap", true);
        readonly CheckBox zeroLineCheck = Check("Zero line", true);
        readonly CheckBox confidenceCheck = Check("Confidence band", true);
        readonly CheckBox errorBarsCheck = Check("Error bars", true);
        readonly CheckBox excludedCheck = Check("Excluded points", true);
        readonly CheckBox excludedErrorBarsCheck = Check("Excluded error bars", false);
        readonly CheckBox offsetCorrectedCheck = Check("Offset-corrected heats", true);

        Bitmap? bitmap;
        ITCDataContainer? selectedItem;
        ExperimentData? figureExperiment;
        string? cacheKey;

        public event EventHandler<string>? StatusChanged;

        public FinalFigureWorkspaceControl()
        {
            BuildLayout();
            WireEvents();
            energyUnitCombo.SelectedIndex = Array.IndexOf(EnergyUnits, AppSettings.EnergyUnit);
            if (energyUnitCombo.SelectedIndex < 0) energyUnitCombo.SelectedIndex = 0;
            uncertaintyCombo.SelectedIndex = Array.IndexOf(UncertaintyStyles, AppSettings.UncertaintyDisplayStyle);
            if (uncertaintyCombo.SelectedIndex < 0) uncertaintyCombo.SelectedIndex = 1;
        }

        public ITCDataContainer? SelectedItem
        {
            get => selectedItem;
            set
            {
                if (ReferenceEquals(selectedItem, value)) return;
                selectedItem = value;
                UpdateContext();
            }
        }

        public void FitToPage()
        {
            RefreshPreview(force: true);
        }

        public void InvalidatePreview()
        {
            cacheKey = null;
            if (figureExperiment != null)
                RefreshPreview(force: true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            bitmap?.Dispose();
            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            previewHost.Background = Solid("#F1F4F7");
            previewHost.BorderBrush = Solid("#D4DAE1");
            previewHost.BorderThickness = new Thickness(1);
            previewHost.Padding = new Thickness(12);
            previewHost.Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = image
            };

            var root = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,330"),
                Background = Solid("#F5F7FA")
            };
            Grid.SetColumn(previewHost, 0);

            var inspector = new TabControl
            {
                Margin = new Thickness(10, 0, 0, 0)
            };
            inspector.Items.Add(Tab("General", BuildGeneralTab()));
            inspector.Items.Add(Tab("Data Graph", BuildDataGraphTab()));
            inspector.Items.Add(Tab("Fit Graph", BuildFitGraphTab()));
            inspector.Items.Add(Tab("Export", BuildExportTab()));
            Grid.SetColumn(inspector, 1);

            root.Children.Add(previewHost);
            root.Children.Add(inspector);
            Content = root;
        }

        Control BuildGeneralTab()
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(Section("Page", new Control[]
            {
                Row(fitPageButton),
                Labeled("Width cm", widthBox),
                Labeled("Height cm", heightBox),
                Labeled("Energy", energyUnitCombo),
                Labeled("Time", timeUnitCombo)
            }));
            panel.Children.Add(Section("Content", new Control[]
            {
                showThermogramCheck,
                axisTitlesCheck,
                sanitizeTicksCheck,
                experimentDetailsCheck,
                modelInfoCheck,
                fitParametersCheck,
                Labeled("Info box", infoPlacementCombo),
                Labeled("Uncertainty", uncertaintyCombo)
            }));
            panel.Children.Add(Section("Parameters", new Control[]
            {
                thermodynamicCheck,
                derivedCheck,
                offsetParameterCheck,
                temperatureCheck,
                concentrationsCheck,
                injectionDelayCheck,
                instrumentCheck,
                attributesCheck
            }));
            panel.Children.Add(statusText);

            return Scroll(panel);
        }

        Control BuildDataGraphTab()
        {
            return Scroll(Section("Data graph", new Control[]
            {
                Labeled("Power title", powerAxisTitleBox),
                Labeled("Time title", timeAxisTitleBox),
                Labeled("X ticks", dataXTickBox),
                Labeled("Y ticks", dataYTickBox),
                Labeled("X min", dataXMinBox),
                Labeled("X max", dataXMaxBox),
                Labeled("Y min", dataYMinBox),
                Labeled("Y max", dataYMaxBox),
                correctedDataCheck
            }));
        }

        Control BuildFitGraphTab()
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(Section("Fit graph", new Control[]
            {
                Labeled("Y title", enthalpyAxisTitleBox),
                Labeled("X title", fitXAxisTitleBox),
                Labeled("X ticks", fitXTickBox),
                Labeled("Y ticks", fitYTickBox),
                Labeled("X min", fitXMinBox),
                Labeled("X max", fitXMaxBox),
                Labeled("Y min", fitYMinBox),
                Labeled("Y max", fitYMaxBox),
                Labeled("Symbol", symbolCombo),
                Labeled("Symbol size", symbolSizeBox)
            }));
            panel.Children.Add(Section("Residuals", new Control[]
            {
                residualsCheck,
                residualGapCheck,
                Labeled("Y ticks", residualYTickBox),
                Labeled("Height", residualFractionBox),
                Labeled("Y min", residualYMinBox),
                Labeled("Y max", residualYMaxBox)
            }));
            panel.Children.Add(Section("Display", new Control[]
            {
                zeroLineCheck,
                confidenceCheck,
                errorBarsCheck,
                excludedCheck,
                excludedErrorBarsCheck,
                offsetCorrectedCheck
            }));

            return Scroll(panel);
        }

        Control BuildExportTab()
        {
            return Scroll(Section("Export", new Control[]
            {
                exportPdfButton
            }));
        }

        void WireEvents()
        {
            previewHost.SizeChanged += (_, _) => RefreshPreview();
            fitPageButton.Click += (_, _) => RefreshPreview(force: true);
            exportPdfButton.Click += async (_, _) => await ExportPdfAsync();

            foreach (var check in AllChecks())
                check.IsCheckedChanged += (_, _) => RefreshPreview(force: true);

            foreach (var combo in AllCombos())
                combo.SelectionChanged += (_, _) => RefreshPreview(force: true);

            foreach (var textBox in AllTextBoxes())
            {
                textBox.LostFocus += (_, _) => RefreshPreview(force: true);
                textBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                        RefreshPreview(force: true);
                };
            }
        }

        IEnumerable<CheckBox> AllChecks()
        {
            return new[]
            {
                showThermogramCheck,
                axisTitlesCheck,
                sanitizeTicksCheck,
                experimentDetailsCheck,
                modelInfoCheck,
                fitParametersCheck,
                thermodynamicCheck,
                derivedCheck,
                offsetParameterCheck,
                temperatureCheck,
                concentrationsCheck,
                injectionDelayCheck,
                instrumentCheck,
                attributesCheck,
                correctedDataCheck,
                residualsCheck,
                residualGapCheck,
                zeroLineCheck,
                confidenceCheck,
                errorBarsCheck,
                excludedCheck,
                excludedErrorBarsCheck,
                offsetCorrectedCheck
            };
        }

        IEnumerable<ComboBox> AllCombos()
        {
            return new[]
            {
                energyUnitCombo,
                timeUnitCombo,
                uncertaintyCombo,
                infoPlacementCombo,
                symbolCombo
            };
        }

        IEnumerable<TextBox> AllTextBoxes()
        {
            return new[]
            {
                widthBox,
                heightBox,
                powerAxisTitleBox,
                timeAxisTitleBox,
                dataXTickBox,
                dataYTickBox,
                dataXMinBox,
                dataXMaxBox,
                dataYMinBox,
                dataYMaxBox,
                enthalpyAxisTitleBox,
                fitXAxisTitleBox,
                fitXTickBox,
                fitYTickBox,
                fitXMinBox,
                fitXMaxBox,
                fitYMinBox,
                fitYMaxBox,
                residualYMinBox,
                residualYMaxBox,
                residualYTickBox,
                residualFractionBox,
                symbolSizeBox
            };
        }

        void UpdateContext()
        {
            cacheKey = null;

            if (selectedItem is ExperimentData experiment)
            {
                figureExperiment = experiment;
                RefreshPreview(force: true);
                return;
            }

            if (selectedItem is AnalysisResult result)
            {
                DataManager.LoadResultSolutionsToExperiments(result);
                figureExperiment = GetResultExperiments(result).FirstOrDefault();
                RefreshPreview(force: true);
                return;
            }

            figureExperiment = null;
            bitmap?.Dispose();
            bitmap = null;
            image.Source = null;
            statusText.Text = "No figure selected";
        }

        void RefreshPreview(bool force = false)
        {
            if (figureExperiment == null)
            {
                statusText.Text = "No figure selected";
                return;
            }

            var options = BuildOptions();
            var hostWidth = previewHost.Bounds.Width;
            var pixelWidth = Math.Max(850, Math.Min(2200, (int)Math.Round((hostWidth > 1 ? hostWidth : 1000) * 2)));
            var solutionKey = figureExperiment.Solution == null ? "no-solution" : figureExperiment.Solution.GetHashCode().ToString();
            var nextKey = $"{figureExperiment.UniqueID}|{solutionKey}|{pixelWidth}|{options.CacheKey}";

            if (!force && cacheKey == nextKey) return;

            try
            {
                var document = PublicationFigureBuilder.Build(figureExperiment, options);
                using var rendered = renderer.RenderBitmap(document, pixelWidth);
                var nextBitmap = ToAvaloniaBitmap(rendered);

                bitmap?.Dispose();
                bitmap = nextBitmap;
                image.Source = nextBitmap;
                cacheKey = nextKey;
                statusText.Text = figureExperiment.Solution == null
                    ? $"{figureExperiment.Name}: preview without fitted solution"
                    : $"{figureExperiment.Name}: publication figure";
            }
            catch (Exception ex)
            {
                bitmap?.Dispose();
                bitmap = null;
                image.Source = null;
                statusText.Text = $"Could not render figure: {ex.Message}";
            }
        }

        PublicationFigureOptions BuildOptions()
        {
            var defaults = new PublicationFigureOptions();
            var display = BuildDisplayParameters();

            return new PublicationFigureOptions
            {
                PlotWidthCentimeters = ParseDouble(widthBox.Text, defaults.PlotWidthCentimeters, 3, 20),
                PlotHeightCentimeters = ParseDouble(heightBox.Text, defaults.PlotHeightCentimeters, 4, 28),
                EnergyUnit = SelectedEnergyUnit(),
                TimeUnit = SelectedTimeUnit(),
                ShowThermogram = showThermogramCheck.IsChecked == true,
                ShowResiduals = residualsCheck.IsChecked == true,
                ShowErrorBars = errorBarsCheck.IsChecked == true,
                ShowConfidenceBand = confidenceCheck.IsChecked == true,
                ShowExperimentDetails = experimentDetailsCheck.IsChecked == true,
                ShowFitParameters = modelInfoCheck.IsChecked == true || fitParametersCheck.IsChecked == true,
                ShowAxisTitles = axisTitlesCheck.IsChecked == true,
                DrawFitOffsetCorrected = offsetCorrectedCheck.IsChecked == true,
                ShowBadData = excludedCheck.IsChecked == true,
                ShowBadDataErrorBars = excludedErrorBarsCheck.IsChecked == true,
                IncludeResidualGraphGap = residualGapCheck.IsChecked == true,
                SanitizeTicks = sanitizeTicksCheck.IsChecked == true,
                DrawBaselineCorrected = correctedDataCheck.IsChecked == true,
                ShowZeroLine = zeroLineCheck.IsChecked == true,
                DataXTickCount = ParseInt(dataXTickBox.Text, defaults.DataXTickCount, 2, 12),
                DataYTickCount = ParseInt(dataYTickBox.Text, defaults.DataYTickCount, 2, 12),
                FitXTickCount = ParseInt(fitXTickBox.Text, defaults.FitXTickCount, 2, 12),
                FitYTickCount = ParseInt(fitYTickBox.Text, defaults.FitYTickCount, 2, 12),
                ResidualYTickCount = ParseInt(residualYTickBox.Text, defaults.ResidualYTickCount, 2, 7),
                ResidualPanelFraction = ParseDouble(residualFractionBox.Text, defaults.ResidualPanelFraction, 0.05, 0.5),
                InformationBoxPlacement = SelectedInfoBoxPlacement(),
                SymbolShape = symbolCombo.SelectedIndex == 1 ? PublicationSymbolShape.Circle : PublicationSymbolShape.Square,
                SymbolSize = ParseDouble(symbolSizeBox.Text, defaults.SymbolSize, 3, 14),
                PowerAxisTitle = string.IsNullOrWhiteSpace(powerAxisTitleBox.Text) ? defaults.PowerAxisTitle : powerAxisTitleBox.Text!,
                TimeAxisTitle = string.IsNullOrWhiteSpace(timeAxisTitleBox.Text) ? defaults.TimeAxisTitle : timeAxisTitleBox.Text!,
                EnthalpyAxisTitle = string.IsNullOrWhiteSpace(enthalpyAxisTitleBox.Text) ? defaults.EnthalpyAxisTitle : enthalpyAxisTitleBox.Text!,
                XAxisTitle = fitXAxisTitleBox.Text ?? "",
                DataXAxisMinimum = ParseOptionalDouble(dataXMinBox.Text),
                DataXAxisMaximum = ParseOptionalDouble(dataXMaxBox.Text),
                DataYAxisMinimum = ParseOptionalDouble(dataYMinBox.Text),
                DataYAxisMaximum = ParseOptionalDouble(dataYMaxBox.Text),
                FitXAxisMinimum = ParseOptionalDouble(fitXMinBox.Text),
                FitXAxisMaximum = ParseOptionalDouble(fitXMaxBox.Text),
                FitYAxisMinimum = ParseOptionalDouble(fitYMinBox.Text),
                FitYAxisMaximum = ParseOptionalDouble(fitYMaxBox.Text),
                ResidualYAxisMinimum = ParseOptionalDouble(residualYMinBox.Text),
                ResidualYAxisMaximum = ParseOptionalDouble(residualYMaxBox.Text),
                DisplayParameters = display,
                AttributeOptions = AppSettings.DisplayAttributeOptions,
                TextUncertaintyStyle = SelectedUncertaintyStyle()
            };
        }

        FinalFigureDisplayParameters BuildDisplayParameters()
        {
            var display = FinalFigureDisplayParameters.None;

            if (modelInfoCheck.IsChecked == true)
                display |= FinalFigureDisplayParameters.Model;

            if (fitParametersCheck.IsChecked == true)
            {
                if (thermodynamicCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Thermodynamic;
                if (derivedCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Derived;
                if (offsetParameterCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Offset;
            }

            if (experimentDetailsCheck.IsChecked == true)
            {
                if (temperatureCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Temperature;
                if (concentrationsCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Concentrations;
                if (injectionDelayCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.InjectionDelay;
                if (instrumentCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Instrument;
                if (attributesCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Attributes;
            }

            return display;
        }

        EnergyUnit SelectedEnergyUnit()
        {
            return energyUnitCombo.SelectedIndex >= 0 && energyUnitCombo.SelectedIndex < EnergyUnits.Length
                ? EnergyUnits[energyUnitCombo.SelectedIndex]
                : AppSettings.EnergyUnit;
        }

        TimeUnit SelectedTimeUnit()
        {
            return timeUnitCombo.SelectedIndex >= 0 && timeUnitCombo.SelectedIndex < TimeUnits.Length
                ? TimeUnits[timeUnitCombo.SelectedIndex]
                : TimeUnit.Minute;
        }

        UncertaintyDisplayStyle SelectedUncertaintyStyle()
        {
            return uncertaintyCombo.SelectedIndex >= 0 && uncertaintyCombo.SelectedIndex < UncertaintyStyles.Length
                ? UncertaintyStyles[uncertaintyCombo.SelectedIndex]
                : AppSettings.UncertaintyDisplayStyle;
        }

        PublicationInfoBoxPlacement SelectedInfoBoxPlacement()
        {
            return infoPlacementCombo.SelectedIndex switch
            {
                1 => PublicationInfoBoxPlacement.Upper,
                2 => PublicationInfoBoxPlacement.Lower,
                _ => PublicationInfoBoxPlacement.Auto
            };
        }

        async Task ExportPdfAsync()
        {
            if (selectedItem is AnalysisResult result)
            {
                await ExportResultFiguresAsync(result);
                return;
            }

            if (figureExperiment == null)
            {
                StatusChanged?.Invoke(this, "No figure selected");
                return;
            }

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Final Figure",
                SuggestedFileName = SanitizeFileName(figureExperiment.Name) + ".pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF figure") { Patterns = new[] { "*.pdf" } },
                    FilePickerFileTypes.All
                }
            });

            var path = file == null ? null : GetLocalPath(file);
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

            ExportExperimentFigure(figureExperiment, path);
            StatusChanged?.Invoke(this, "Final figure exported");
        }

        async Task ExportResultFiguresAsync(AnalysisResult result)
        {
            DataManager.LoadResultSolutionsToExperiments(result);
            var experiments = GetResultExperiments(result).ToList();

            if (experiments.Count == 0)
            {
                StatusChanged?.Invoke(this, "Selected result has no experiment figures");
                return;
            }

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose Figure Export Folder",
                AllowMultiple = false
            });

            var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folderPath)) return;

            Directory.CreateDirectory(folderPath);

            foreach (var target in CreateFigureExportTargets(experiments, folderPath))
                ExportExperimentFigure(target.Experiment, target.Path);

            StatusChanged?.Invoke(this, $"{experiments.Count} final figure{(experiments.Count == 1 ? "" : "s")} exported");
        }

        void ExportExperimentFigure(ExperimentData experiment, string path)
        {
            var document = PublicationFigureBuilder.Build(experiment, BuildOptions());
            renderer.WritePdf(document, path);
        }

        static IEnumerable<ExperimentData> GetResultExperiments(AnalysisResult result)
        {
            return result.Solution?.Solutions?
                .Where(solution => solution?.Data != null)
                .Select(solution => solution.Data)
                .Where(experiment => experiment != null)
                .GroupBy(experiment => experiment.UniqueID)
                .Select(group => group.First())
                ?? Enumerable.Empty<ExperimentData>();
        }

        static List<FigureExportTarget> CreateFigureExportTargets(IEnumerable<ExperimentData> experiments, string folderPath)
        {
            var targets = new List<FigureExportTarget>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var experiment in experiments)
            {
                var baseName = SanitizeFileName(experiment.Name);
                var fileName = baseName + ".pdf";
                var suffix = 2;

                while (usedNames.Contains(fileName))
                {
                    fileName = $"{baseName} ({suffix}).pdf";
                    suffix++;
                }

                usedNames.Add(fileName);
                targets.Add(new FigureExportTarget(experiment, Path.Combine(folderPath, fileName)));
            }

            return targets;
        }

        static string? GetLocalPath(IStorageFile file)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) return path;

            return file.Path.IsFile ? file.Path.LocalPath : null;
        }

        static string SanitizeFileName(string name)
        {
            var cleanName = string.IsNullOrWhiteSpace(name) ? "Untitled Figure" : name.Trim();

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
                cleanName = cleanName.Replace(invalidChar, '_');

            return string.IsNullOrWhiteSpace(cleanName) ? "Untitled Figure" : cleanName;
        }

        static Bitmap ToAvaloniaBitmap(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Position = 0;

            return new Bitmap(stream);
        }

        static double ParseDouble(string? text, double fallback, double minimum, double maximum)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }

        static double? ParseOptionalDouble(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return null;
        }

        static int ParseInt(string? text, int fallback, int minimum, int maximum)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) &&
                !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }

        static TabItem Tab(string header, Control content)
        {
            return new TabItem
            {
                Header = new TextBlock
                {
                    Text = header,
                    FontSize = 11,
                    TextWrapping = TextWrapping.NoWrap
                },
                Content = content
            };
        }

        static Control Scroll(Control content)
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Solid("#D4DAE1"),
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Border
                    {
                        Padding = new Thickness(10),
                        Child = content
                    }
                }
            };
        }

        static Border Section(string title, Control[] controls)
        {
            var panel = new StackPanel { Spacing = 7 };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                Foreground = Solid("#202832")
            });
            foreach (var control in controls)
                panel.Children.Add(control);

            return new Border
            {
                BorderBrush = Solid("#E3E7EC"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 10),
                Child = panel
            };
        }

        static Border Labeled(string label, Control control)
        {
            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("94,*")
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Solid("#607080"),
                FontSize = 11
            });
            Grid.SetColumn(control, 1);
            panel.Children.Add(control);

            return new Border { Child = panel };
        }

        static StackPanel Row(params Control[] controls)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            foreach (var control in controls)
                row.Children.Add(control);
            return row;
        }

        static ComboBox Combo(string[] items, int selectedIndex, double width)
        {
            return new ComboBox
            {
                ItemsSource = items,
                SelectedIndex = selectedIndex,
                Width = width,
                MinHeight = 28,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static TextBox TextBox(string text)
        {
            return new TextBox
            {
                Text = text,
                MinHeight = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        static Button Button(string text, double width)
        {
            return new Button
            {
                Content = text,
                MinWidth = width,
                MinHeight = 28,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        static CheckBox Check(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static TextBlock Text(string text = "")
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Solid("#4D5A66"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
        }

        static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

        sealed class FigureExportTarget
        {
            public FigureExportTarget(ExperimentData experiment, string path)
            {
                Experiment = experiment;
                Path = path;
            }

            public ExperimentData Experiment { get; }
            public string Path { get; }
        }
    }
}
