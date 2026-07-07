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
using AnalysisITC.Avalonia.Workspace;
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
        const double PreviewRenderScale = 4.0;

        readonly SkiaFigureRenderer renderer = new SkiaFigureRenderer();
        readonly Border previewHost = new Border();
        readonly Image image = new Image
        {
            Stretch = Stretch.Fill
        };
        readonly TextBlock statusText = Text();

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
        readonly CheckBox injectionDelayCheck = Check("Injection delay", true);
        readonly CheckBox instrumentCheck = Check("Instrument", true);
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
        readonly TextBox symbolSizeBox = TextBox("8");
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
        bool isApplyingSettingsDefaults;

        public event EventHandler<string>? StatusChanged;

        public FinalFigureWorkspaceControl()
        {
            BuildLayout();
            WireEvents();
            ApplySettingsDefaults();
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

        public void InvalidatePreview()
        {
            cacheKey = null;
            if (figureExperiment != null)
                RefreshPreview(force: true);
        }

        public void ApplySettingsDefaults()
        {
            try
            {
                isApplyingSettingsDefaults = true;

                var dimensions = AppSettings.FinalFigureDimensions;
                if (dimensions.Length > 0) widthBox.Text = dimensions[0].ToString("G6", CultureInfo.CurrentCulture);
                if (dimensions.Length > 1) heightBox.Text = dimensions[1].ToString("G6", CultureInfo.CurrentCulture);

                energyUnitCombo.SelectedIndex = Math.Max(0, Array.IndexOf(EnergyUnits, AppSettings.EnergyUnit));
                uncertaintyCombo.SelectedIndex = Math.Max(0, Array.IndexOf(UncertaintyStyles, AppSettings.UncertaintyDisplayStyle));
                experimentDetailsCheck.IsChecked = AppSettings.FinalFigureShowDetailsAsDefault;
                modelInfoCheck.IsChecked = AppSettings.FinalFigureShowModelInfoAsDefault;
                fitParametersCheck.IsChecked = AppSettings.FinalFigureShowParameterBoxAsDefault;
                residualsCheck.IsChecked = AppSettings.ShowResidualGraph;
                residualGapCheck.IsChecked = AppSettings.ShowResidualGraphGap;

                var display = AppSettings.FinalFigureParameterDisplay;
                thermodynamicCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Thermodynamic);
                derivedCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Derived);
                offsetParameterCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Offset);
                temperatureCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Temperature);
                concentrationsCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Concentrations);
                injectionDelayCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.InjectionDelay);
                instrumentCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Instrument);
                attributesCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Attributes);
            }
            finally
            {
                isApplyingSettingsDefaults = false;
            }

            cacheKey = null;
            if (figureExperiment != null)
                RefreshPreview(force: true);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (figureExperiment != null && image.Source == null)
                RefreshPreview(force: true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            ClearBitmap();
            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            image.Stretch = Stretch.Fill;
            previewHost.Background = Solid("#F1F4F7");
            previewHost.BorderBrush = Solid("#D4DAE1");
            previewHost.BorderThickness = new Thickness(1);
            previewHost.Padding = new Thickness(12);
            previewHost.Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = image
                }
            };

            var root = WorkspaceControlBuilder.WorkspaceGrid();
            Grid.SetColumn(previewHost, 0);

            var inspectorHost = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Margin = new Thickness(10, 0, 0, 0)
            };

            var inspector = WorkspaceControlBuilder.Inspector();
            inspector.Margin = new Thickness(0);
            inspector.Items.Add(Tab("General", BuildGeneralTab()));
            inspector.Items.Add(Tab("Data Graph", BuildDataGraphTab()));
            inspector.Items.Add(Tab("Fit Graph", BuildFitGraphTab()));
            Grid.SetRow(inspector, 0);

            var exportFooter = new Border
            {
                Background = WorkspaceControlBuilder.PanelBackgroundBrush,
                BorderBrush = WorkspaceControlBuilder.PanelBorderBrush,
                BorderThickness = new Thickness(1, 0, 1, 1),
                Padding = new Thickness(10, 8),
                Child = exportPdfButton
            };
            Grid.SetRow(exportFooter, 1);

            inspectorHost.Children.Add(inspector);
            inspectorHost.Children.Add(exportFooter);
            Grid.SetColumn(inspectorHost, 1);

            root.Children.Add(previewHost);
            root.Children.Add(inspectorHost);
            Content = root;
        }

        Control BuildGeneralTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();
            panel.Children.Add(Section("Page", new Control[]
            {
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
            var panel = WorkspaceControlBuilder.InspectorPanel();
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
                residualGapCheck
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

        void WireEvents()
        {
            previewHost.SizeChanged += (_, _) => RefreshPreview();
            exportPdfButton.Click += async (_, _) => await ExportPdfAsync();

            foreach (var check in AllChecks())
                check.IsCheckedChanged += (_, _) =>
                {
                    if (!isApplyingSettingsDefaults) RefreshPreview(force: true);
                };

            foreach (var combo in AllCombos())
                combo.SelectionChanged += (_, _) =>
                {
                    if (!isApplyingSettingsDefaults) RefreshPreview(force: true);
                };

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
            ClearBitmap();
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
            var solutionKey = figureExperiment.Solution == null ? "no-solution" : figureExperiment.Solution.GetHashCode().ToString();

            try
            {
                var document = PublicationFigureBuilder.Build(figureExperiment, options);
                var pageSize = renderer.GetPageSize(document);
                var pixelWidth = PreviewPixelWidth(pageSize);
                var nextKey = $"{figureExperiment.UniqueID}|{solutionKey}|{pixelWidth}|{options.CacheKey}";

                if (!force && cacheKey == nextKey) return;

                using var rendered = renderer.RenderBitmap(document, pixelWidth);
                var nextBitmap = ToAvaloniaBitmap(rendered);

                image.Width = pageSize.Width;
                image.Height = pageSize.Height;
                ReplaceBitmap(nextBitmap);
                cacheKey = nextKey;
                statusText.Text = figureExperiment.Solution == null
                    ? $"{figureExperiment.Name}: preview without fitted solution"
                    : $"{figureExperiment.Name}: publication figure";
            }
            catch (Exception ex)
            {
                ClearBitmap();
                statusText.Text = $"Could not render figure: {ex.Message}";
            }
        }

        int PreviewPixelWidth(SKSize pageSize)
        {
            return Math.Max(800, Math.Min(4096, (int)Math.Round(pageSize.Width * PreviewRenderScale)));
        }

        void ReplaceBitmap(Bitmap nextBitmap)
        {
            var oldBitmap = bitmap;
            bitmap = nextBitmap;
            image.Source = nextBitmap;
            oldBitmap?.Dispose();
        }

        void ClearBitmap()
        {
            var oldBitmap = bitmap;
            bitmap = null;
            cacheKey = null;
            image.Source = null;
            oldBitmap?.Dispose();
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
                AutoAxesIgnoresBadData = AppSettings.AutoAxesIgnoresBadData,
                IncludeResidualGraphGap = residualGapCheck.IsChecked == true,
                SanitizeTicks = sanitizeTicksCheck.IsChecked == true,
                DrawBaselineCorrected = correctedDataCheck.IsChecked == true,
                ShowZeroLine = zeroLineCheck.IsChecked == true,
                DataXTickCount = ParseInt(dataXTickBox.Text, defaults.DataXTickCount, 2, 12),
                DataYTickCount = ParseInt(dataYTickBox.Text, defaults.DataYTickCount, 2, 12),
                FitXTickCount = ParseInt(fitXTickBox.Text, defaults.FitXTickCount, 2, 12),
                FitYTickCount = ParseInt(fitYTickBox.Text, defaults.FitYTickCount, 2, 12),
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

        public async Task ExportPdfAsync()
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

        static TabItem Tab(string header, Control content) => WorkspaceControlBuilder.Tab(header, content);

        static Control Scroll(Control content) => WorkspaceControlBuilder.Scroll(content);

        static Border Section(string title, Control[] controls) => WorkspaceControlBuilder.Section(title, controls);

        static Border Labeled(string label, Control control) => WorkspaceControlBuilder.Labeled(label, control);

        static StackPanel Row(params Control[] controls) => WorkspaceControlBuilder.Row(controls);

        static ComboBox Combo(string[] items, int selectedIndex, double width) => WorkspaceControlBuilder.Combo(items, selectedIndex, width);

        static TextBox TextBox(string text) => WorkspaceControlBuilder.TextBox(text);

        static Button Button(string text, double width) => WorkspaceControlBuilder.Button(text, width);

        static CheckBox Check(string text, bool isChecked) => WorkspaceControlBuilder.Check(text, isChecked);

        static TextBlock Text(string text = "") => WorkspaceControlBuilder.Text(text);

        static IBrush Solid(string color) => WorkspaceControlBuilder.Solid(color);

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
