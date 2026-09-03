using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AppKit;
using CoreGraphics;
using Foundation;
using PdfKit;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.UI.MacOS.Drawing;

namespace AnalysisITC
{
    sealed class AnalysisReportViewController : NSViewController
    {
        static readonly EnergyUnit?[] EnergyOverrides = { null, EnergyUnit.Joule, EnergyUnit.KiloJoule, EnergyUnit.Cal, EnergyUnit.KCal };
        static readonly Dictionary<string, HashSet<string>> SessionAdvancedSelections = new Dictionary<string, HashSet<string>>();
        static int sessionEnergyIndex;
        static int sessionTemperatureIndex;
        static int sessionUncertaintyIndex;

        readonly AnalysisResult initiallySelected;
        readonly CoreGraphicsAnalysisReportRenderer renderer = new CoreGraphicsAnalysisReportRenderer();
        readonly NSPopUpButton resultPopup = Popup();
        readonly NSTextField labelField = Field();
        readonly NSTextField titleField = Field();
        readonly NSPopUpButton energyPopup = Popup("Automatic", "J", "kJ", "cal", "kcal");
        readonly NSPopUpButton temperaturePopup = Popup("Celsius", "Kelvin");
        readonly NSPopUpButton uncertaintyPopup = Popup("Automatic", "SD", "CI", "SD + CI", "None");
        readonly NSStackView advancedStack = VerticalStack();
        readonly PdfView pdfView = new PdfView();
        readonly NSTextField placeholder = Label("Click Preview to build the printable A4 report.");
        readonly NSTextField statusLabel = Label("");
        readonly NSProgressIndicator progress = new NSProgressIndicator { Style = NSProgressIndicatorStyle.Spinning };
        readonly NSButton previewButton = Button("Preview");
        readonly NSButton exportButton = Button("Export PDF...");
        readonly Dictionary<NSButton, AnalysisReportAdvancedSectionDescriptor> advancedButtons = new Dictionary<NSButton, AnalysisReportAdvancedSectionDescriptor>();
        readonly List<AnalysisResult> results = new List<AnalysisResult>();
        AnalysisReportDocument currentDocument;
        AnalysisReportLayoutPlan currentPlan;
        NSData currentPdfData;
        PdfDocument currentPdfDocument;
        bool stale = true;
        bool busy;
        bool changingResult;

        public AnalysisReportViewController(AnalysisResult selected)
        {
            initiallySelected = selected;
            PreferredContentSize = new CGSize(1120, 720);
        }

        public override void LoadView()
        {
            var root = new NSView(new CGRect(0, 0, 1120, 720));
            View = root;

            var previewHost = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            var inspectorScroll = new NSScrollView { HasVerticalScroller = true, BorderType = NSBorderType.BezelBorder, TranslatesAutoresizingMaskIntoConstraints = false };
            var footer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            root.AddSubview(previewHost); root.AddSubview(inspectorScroll); root.AddSubview(footer);

            pdfView.AutoScales = true;
            pdfView.DisplaysPageBreaks = true;
            pdfView.DisplayMode = PdfDisplayMode.SinglePageContinuous;
            pdfView.Hidden = true;
            pdfView.BackgroundColor = NSColor.UnderPageBackground;
            pdfView.TranslatesAutoresizingMaskIntoConstraints = false;
            placeholder.Alignment = NSTextAlignment.Center;
            placeholder.TextColor = NSColor.SecondaryLabel;
            placeholder.TranslatesAutoresizingMaskIntoConstraints = false;
            previewHost.AddSubview(pdfView); previewHost.AddSubview(placeholder);

            var inspector = VerticalStack();
            inspector.EdgeInsets = new NSEdgeInsets(12, 12, 12, 12);
            inspectorScroll.DocumentView = inspector;
            inspector.TranslatesAutoresizingMaskIntoConstraints = false;
            inspector.WidthAnchor.ConstraintEqualToConstant(315).Active = true;

            inspector.AddArrangedSubview(Section("Analysis result", resultPopup));
            inspector.AddArrangedSubview(Section("Document", Row("Label", labelField), Row("Title", titleField)));
            inspector.AddArrangedSubview(Section("Presentation", Row("Energy", energyPopup), Row("Temperature", temperaturePopup), Row("Uncertainty", uncertaintyPopup)));
            var selectAll = Button("Select all"); var clear = Button("Clear");
            selectAll.Activated += (sender, e) => SetAllAdvanced(true);
            clear.Activated += (sender, e) => SetAllAdvanced(false);
            inspector.AddArrangedSubview(Section("Additional analyses", HorizontalStack(selectAll, clear), advancedStack));

            var cancel = Button("Cancel");
            cancel.Activated += (sender, e) => PresentingViewController?.DismissViewController(this);
            previewButton.Activated += async (sender, e) => await BuildAsync(true);
            exportButton.Activated += async (sender, e) => await ExportAsync();
            progress.Hidden = true; progress.ControlSize = NSControlSize.Small; progress.TranslatesAutoresizingMaskIntoConstraints = false;
            statusLabel.LineBreakMode = NSLineBreakMode.ByWordWrapping; statusLabel.MaximumNumberOfLines = 2;
            var actions = HorizontalStack(cancel, previewButton, exportButton);
            footer.AddSubview(progress); footer.AddSubview(statusLabel); footer.AddSubview(actions);
            statusLabel.TranslatesAutoresizingMaskIntoConstraints = false; actions.TranslatesAutoresizingMaskIntoConstraints = false;

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                previewHost.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                previewHost.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                previewHost.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                previewHost.TrailingAnchor.ConstraintEqualToAnchor(inspectorScroll.LeadingAnchor),
                inspectorScroll.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                inspectorScroll.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                inspectorScroll.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                inspectorScroll.WidthAnchor.ConstraintEqualToConstant(340),
                footer.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                footer.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                footer.BottomAnchor.ConstraintEqualToAnchor(root.BottomAnchor),
                footer.HeightAnchor.ConstraintEqualToConstant(58),
                pdfView.LeadingAnchor.ConstraintEqualToAnchor(previewHost.LeadingAnchor),
                pdfView.TrailingAnchor.ConstraintEqualToAnchor(previewHost.TrailingAnchor),
                pdfView.TopAnchor.ConstraintEqualToAnchor(previewHost.TopAnchor),
                pdfView.BottomAnchor.ConstraintEqualToAnchor(previewHost.BottomAnchor),
                placeholder.CenterXAnchor.ConstraintEqualToAnchor(previewHost.CenterXAnchor),
                placeholder.CenterYAnchor.ConstraintEqualToAnchor(previewHost.CenterYAnchor),
                statusLabel.LeadingAnchor.ConstraintEqualToAnchor(footer.LeadingAnchor, 16),
                statusLabel.CenterYAnchor.ConstraintEqualToAnchor(footer.CenterYAnchor),
                statusLabel.TrailingAnchor.ConstraintLessThanOrEqualToAnchor(progress.LeadingAnchor, -10),
                progress.CenterYAnchor.ConstraintEqualToAnchor(footer.CenterYAnchor),
                progress.TrailingAnchor.ConstraintEqualToAnchor(actions.LeadingAnchor, -12),
                actions.TrailingAnchor.ConstraintEqualToAnchor(footer.TrailingAnchor, -16),
                actions.CenterYAnchor.ConstraintEqualToAnchor(footer.CenterYAnchor),
            });

            energyPopup.SelectItem(sessionEnergyIndex); temperaturePopup.SelectItem(sessionTemperatureIndex); uncertaintyPopup.SelectItem(sessionUncertaintyIndex);
            resultPopup.Activated += (sender, e) => ResultChanged();
            labelField.Changed += (sender, e) => MarkStale(); titleField.Changed += (sender, e) => MarkStale();
            energyPopup.Activated += (sender, e) => { sessionEnergyIndex = (int)energyPopup.IndexOfSelectedItem; MarkStale(); };
            temperaturePopup.Activated += (sender, e) => { sessionTemperatureIndex = (int)temperaturePopup.IndexOfSelectedItem; MarkStale(); };
            uncertaintyPopup.Activated += (sender, e) => { sessionUncertaintyIndex = (int)uncertaintyPopup.IndexOfSelectedItem; MarkStale(); };
            PopulateResults();
        }

        void PopulateResults()
        {
            results.Clear(); results.AddRange(DataManager.Results);
            resultPopup.RemoveAllItems();
            resultPopup.AddItems(results.Select(ResultTitle).ToArray());
            var selected = initiallySelected != null && results.Contains(initiallySelected) ? initiallySelected : DataManager.SelectedResult;
            var index = selected == null ? 0 : Math.Max(0, results.IndexOf(selected));
            if (results.Count > 0) resultPopup.SelectItem(index);
            ResultChanged();
        }

        void ResultChanged()
        {
            if (changingResult) return;
            changingResult = true;
            try
            {
                titleField.StringValue = SelectedResult?.Name ?? "";
                RebuildAdvanced();
                ValidateSelection();
            }
            finally { changingResult = false; }
            MarkStale();
        }

        void RebuildAdvanced()
        {
            foreach (var view in advancedStack.ArrangedSubviews.ToArray()) { advancedStack.RemoveArrangedSubview(view); view.RemoveFromSuperview(); view.Dispose(); }
            advancedButtons.Clear(); var result = SelectedResult; if (result == null) return;
            SessionAdvancedSelections.TryGetValue(ResultKey(result), out var selected); selected = selected ?? new HashSet<string>();
            foreach (var descriptor in AnalysisReportBuilder.GetAvailableAdvancedSections(result))
            {
                var button = new NSButton { Title = descriptor.Title, ToolTip = descriptor.Description };
                button.SetButtonType(NSButtonType.Switch); button.State = selected.Contains(descriptor.Request.Key) ? NSCellStateValue.On : NSCellStateValue.Off;
                button.Activated += (sender, e) => { SaveAdvanced(); MarkStale(); };
                advancedButtons.Add(button, descriptor); advancedStack.AddArrangedSubview(button);
            }
            if (advancedButtons.Count == 0) advancedStack.AddArrangedSubview(Label("No saved advanced analyses are available."));
        }

        void SetAllAdvanced(bool selected)
        {
            foreach (var button in advancedButtons.Keys) button.State = selected ? NSCellStateValue.On : NSCellStateValue.Off;
            SaveAdvanced(); MarkStale();
        }

        void SaveAdvanced()
        {
            var result = SelectedResult; if (result == null) return;
            SessionAdvancedSelections[ResultKey(result)] = advancedButtons.Where(item => item.Key.State == NSCellStateValue.On).Select(item => item.Value.Request.Key).ToHashSet();
        }

        AnalysisReportOptions Options()
        {
            var options = new AnalysisReportOptions
            {
                DocumentLabel = labelField.StringValue,
                Title = titleField.StringValue,
                EnergyUnitFamily = AppSettings.EnergyUnitFamily,
                EnergyUnitOverride = EnergyOverrides[Math.Max(0, Math.Min(EnergyOverrides.Length - 1, (int)energyPopup.IndexOfSelectedItem))],
                UseKelvin = temperaturePopup.IndexOfSelectedItem == 1,
                UncertaintyDisplayStyle = uncertaintyPopup.IndexOfSelectedItem == 1 ? UncertaintyDisplayStyle.StandardDeviation
                    : uncertaintyPopup.IndexOfSelectedItem == 2 ? UncertaintyDisplayStyle.ConfidenceInterval
                    : uncertaintyPopup.IndexOfSelectedItem == 3 ? UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval
                    : uncertaintyPopup.IndexOfSelectedItem == 4 ? UncertaintyDisplayStyle.None : UncertaintyDisplayStyle.Automatic
            };
            foreach (var descriptor in advancedButtons.Where(item => item.Key.State == NSCellStateValue.On).Select(item => item.Value))
                options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(descriptor.Request.Kind, descriptor.Request.CorrelationMemberIndex));
            return options;
        }

        async Task<bool> BuildAsync(bool showPreview)
        {
            var result = SelectedResult; if (busy || result == null) return false;
            var validation = AnalysisReportBuilder.Validate(result);
            if (!validation.IsValid) { SetStatus(validation.Errors.FirstOrDefault() ?? "This result cannot be reported.", true); return false; }
            SetBusy(true, showPreview ? "Building preview..." : "Preparing report...");
            try
            {
                var options = Options();
                var output = await Task.Run(() =>
                {
                    var document = AnalysisReportBuilder.Build(result, options);
                    var plan = renderer.CreatePlan(document);
                    var data = renderer.CreatePdfData(document, plan);
                    return Tuple.Create(document, plan, data);
                });
                ReplaceReport(output.Item1, output.Item2, output.Item3, showPreview || pdfView.Document != null);
                stale = false;
                SetStatus(output.Item1.Warnings.FirstOrDefault() ?? output.Item2.Pages.Count + " A4 pages ready.", false, output.Item1.Warnings.Count > 0);
                return true;
            }
            catch (Exception ex) { SetStatus("Could not build report: " + ex.Message, true); return false; }
            finally { SetBusy(false, null); }
        }

        void ReplaceReport(AnalysisReportDocument document, AnalysisReportLayoutPlan plan, NSData data, bool showPreview)
        {
            var oldData = currentPdfData; var oldDocument = currentPdfDocument;
            currentDocument = document; currentPlan = plan; currentPdfData = data;
            if (showPreview)
            {
                currentPdfDocument = new PdfDocument(data); pdfView.Document = currentPdfDocument; pdfView.Hidden = false; placeholder.Hidden = true;
            }
            else currentPdfDocument = null;
            oldDocument?.Dispose(); oldData?.Dispose();
        }

        async Task ExportAsync()
        {
            if (busy || SelectedResult == null) return;
            if (stale || currentPdfData == null) if (!await BuildAsync(false)) return;
            var panel = NSSavePanel.SavePanel; panel.Title = "Export Analysis Report"; panel.NameFieldStringValue = Sanitize(SelectedResult.Name) + "-analysis-report.pdf"; panel.AllowedFileTypes = new[] { "pdf" }; panel.CanCreateDirectories = true;
            panel.BeginSheet(View.Window, async response =>
            {
                if (response != (int)NSModalResponse.OK || panel.Url == null) return;
                SetBusy(true, "Exporting PDF...");
                try
                {
                    var path = panel.Url.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? panel.Url.Path : panel.Url.Path + ".pdf";
                    var directory = Path.GetDirectoryName(path); var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                    var bytes = currentPdfData.ToArray();
                    await Task.Run(() =>
                    {
                        try { File.WriteAllBytes(temporary, bytes); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    });
                    SetStatus("Analysis report PDF exported."); StatusBarManager.SetStatus("Analysis report PDF exported", 3000);
                }
                catch (Exception ex) { AppEventHandler.DisplayHandledException(ex); }
                finally { SetBusy(false, null); }
            });
        }

        void MarkStale()
        {
            if (changingResult) return; stale = true;
            if (pdfView.Document != null && AnalysisReportBuilder.Validate(SelectedResult).IsValid)
                SetStatus("Preview is out of date. Click Preview to refresh.", false, true);
        }

        void ValidateSelection()
        {
            var validation = AnalysisReportBuilder.Validate(SelectedResult); previewButton.Enabled = validation.IsValid; exportButton.Enabled = validation.IsValid;
            if (!validation.IsValid) SetStatus(validation.Errors.FirstOrDefault() ?? "This result cannot be reported.", true);
            else if (SelectedResult.Health != AnalysisResultHealth.Valid) SetStatus("This result has warnings or may be stale. The report will include a notice.", false, true);
            else SetStatus("");
        }

        void SetBusy(bool value, string message)
        {
            busy = value; progress.Hidden = !value; if (value) progress.StartAnimation(this); else progress.StopAnimation(this);
            resultPopup.Enabled = labelField.Enabled = titleField.Enabled = energyPopup.Enabled = temperaturePopup.Enabled = uncertaintyPopup.Enabled = !value;
            foreach (var button in advancedButtons.Keys) button.Enabled = !value;
            previewButton.Enabled = exportButton.Enabled = !value && AnalysisReportBuilder.Validate(SelectedResult).IsValid;
            if (!string.IsNullOrWhiteSpace(message)) SetStatus(message);
        }

        void SetStatus(string message, bool error = false, bool warning = false)
        {
            statusLabel.StringValue = message ?? ""; statusLabel.TextColor = error ? NSColor.SystemRed : warning ? NSColor.SystemOrange : NSColor.SecondaryLabel;
        }

        AnalysisResult SelectedResult => resultPopup.IndexOfSelectedItem >= 0 && resultPopup.IndexOfSelectedItem < results.Count ? results[(int)resultPopup.IndexOfSelectedItem] : null;
        static string ResultKey(AnalysisResult result) => string.IsNullOrWhiteSpace(result.UniqueID) ? result.Name + "|" + result.Date.Ticks : result.UniqueID;
        static string ResultTitle(AnalysisResult result) { var model = result.Model?.ModelType.GetProperties().Name ?? "Unknown model"; var count = result.Solution?.Solutions?.Count ?? 0; return result.Name + " - " + result.Date.ToString("g") + " - " + model + " - " + count + " exp. - " + result.Health; }
        static string Sanitize(string value) { var invalid = Path.GetInvalidFileNameChars().ToHashSet(); var cleaned = new string((value ?? "analysis").Select(character => invalid.Contains(character) || character == '/' || character == '\\' ? '-' : character).ToArray()).Trim(' ', '.', '-'); return string.IsNullOrWhiteSpace(cleaned) ? "analysis" : cleaned; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { pdfView.Document = null; currentPdfDocument?.Dispose(); currentPdfData?.Dispose(); }
            base.Dispose(disposing);
        }

        static NSPopUpButton Popup(params string[] items) { var popup = new NSPopUpButton { TranslatesAutoresizingMaskIntoConstraints = false }; if (items.Length > 0) popup.AddItems(items); return popup; }
        static NSTextField Field() => new NSTextField { TranslatesAutoresizingMaskIntoConstraints = false };
        static NSTextField Label(string text) => new NSTextField { StringValue = text ?? "", Editable = false, Bordered = false, DrawsBackground = false, TranslatesAutoresizingMaskIntoConstraints = false };
        static NSButton Button(string title) => new NSButton { Title = title, BezelStyle = NSBezelStyle.Rounded, TranslatesAutoresizingMaskIntoConstraints = false };
        static NSStackView VerticalStack(params NSView[] views) { var stack = new NSStackView(new CGRect(0, 0, 100, 100)) { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false }; foreach (var view in views) stack.AddArrangedSubview(view); return stack; }
        static NSStackView HorizontalStack(params NSView[] views) { var stack = new NSStackView(new CGRect(0, 0, 100, 28)) { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.CenterY, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false }; foreach (var view in views) stack.AddArrangedSubview(view); return stack; }
        static NSView Row(string title, NSView control) { var label = Label(title); label.WidthAnchor.ConstraintEqualToConstant(90).Active = true; control.WidthAnchor.ConstraintGreaterThanOrEqualToConstant(180).Active = true; return HorizontalStack(label, control); }
        static NSView Section(string title, params NSView[] views) { var heading = Label(title); heading.Font = NSFont.BoldSystemFontOfSize(NSFont.SystemFontSize); var stack = VerticalStack(new[] { heading }.Concat(views).ToArray()); stack.EdgeInsets = new NSEdgeInsets(4, 0, 8, 0); return stack; }
    }
}
