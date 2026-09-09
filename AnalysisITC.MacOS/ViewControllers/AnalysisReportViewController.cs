using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using AppKit;
using CoreGraphics;
using Foundation;
using PdfKit;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.UI.MacOS.Drawing;

namespace AnalysisITC
{
    sealed class AnalysisReportViewController : NSViewController
    {
        static readonly EnergyUnitFamily[] EnergyFamilies = { EnergyUnitFamily.Joules, EnergyUnitFamily.Calories };
        static readonly Dictionary<string, HashSet<string>> SessionAdvancedSelections = new Dictionary<string, HashSet<string>>();
        static int sessionEnergyIndex = AppSettings.EnergyUnitFamily == EnergyUnitFamily.Calories ? 1 : 0;
        static int sessionTemperatureIndex;
        static int sessionUncertaintyIndex = 3;
        static bool sessionIncludeInjectionTables = true;
        static bool sessionCondenseRepeatedExperiments = true;
        static readonly HttpClient InterpretationHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        readonly AnalysisResult initiallySelected;
        readonly CoreGraphicsAnalysisReportRenderer renderer = new CoreGraphicsAnalysisReportRenderer();
        readonly NSButton selectResultsButton = Button("Select report contents…");
        readonly NSPopover resultPopover = new NSPopover { Behavior = NSPopoverBehavior.Semitransient };
        readonly NSTextField resultSummaryLabel = Label("");
        readonly NSTextField labelField = Field();
        readonly NSTextField titleField = Field();
        readonly NSPopUpButton energyPopup = Popup("Joule", "Calories");
        readonly NSPopUpButton temperaturePopup = Popup("Celsius", "Kelvin");
        readonly NSPopUpButton uncertaintyPopup = Popup("Automatic", "Standard deviation (SD)", "95% CI", "SD + 95% CI", "None");
        readonly NSStackView advancedStack = VerticalStack();
        readonly NSButton injectionTablesButton = Button("Injection tables");
        readonly NSButton condenseRepeatedButton = Button("Condense repeated experiments");
        readonly NSSegmentedControl workspaceSelector = WorkspaceSelector();
        readonly ReportInterpretationTextView interpretationText = new ReportInterpretationTextView();
        readonly NSTextField interpretationWorkspaceStatus = Label("");
        readonly NSTextField interpretationSummaryLabel = Label("No interpretation");
        readonly NSButton editInterpretationButton = Button("Edit interpretation");
        readonly NSButton generateInterpretationButton = Button("Generate with AI...");
        readonly NSView previewHost = new NSView
            { TranslatesAutoresizingMaskIntoConstraints = false };
        readonly NSView interpretationHost = new NSView
            { TranslatesAutoresizingMaskIntoConstraints = false };
        readonly PdfView pdfView = new PdfView();
        readonly NSTextField placeholder = Label("No preview yet\n\nSelect Preview to build the report.");
        readonly NSTextField statusLabel = Label("");
        readonly NSProgressIndicator progress = new NSProgressIndicator { Style = NSProgressIndicatorStyle.Spinning };
        readonly NSButton previewButton = Button("Update Preview");
        readonly NSButton exportButton = Button("Export PDF...");
        readonly Dictionary<NSButton, AnalysisReportAdvancedSectionDescriptor> advancedButtons = new Dictionary<NSButton, AnalysisReportAdvancedSectionDescriptor>();
        readonly NSButton selectAllButton = Button("Select All");
        readonly NSButton clearButton = Button("Clear");
        readonly List<AnalysisResult> results = new List<AnalysisResult>();
        readonly List<AnalysisResult> selectedResults = new List<AnalysisResult>();
        readonly List<ExperimentData> experiments = new List<ExperimentData>();
        readonly List<ExperimentData> selectedSupportingExperiments = new List<ExperimentData>();
        readonly Dictionary<NSButton, ITCDataContainer> resultPickerButtons = new Dictionary<NSButton, ITCDataContainer>();
        AnalysisReportDocument currentDocument;
        AnalysisReportLayoutPlan currentPlan;
        NSData currentPdfData;
        PdfDocument currentPdfDocument;
        bool stale = true;
        bool busy;
        bool changingResult;
        bool automaticTitle = true;
        bool changingWorkspace;
        bool loadingInterpretation;
        AnalysisReport report;

        public AnalysisReportViewController(AnalysisResult selected)
        {
            initiallySelected = selected;
            PreferredContentSize = new CGSize(1120, 720);
        }

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();
            ShowInterpretationWorkspace(true);
        }

        public override void LoadView()
        {
            var root = new NSView(new CGRect(0, 0, 1120, 720));
            View = root;

            var mainHost = new NSView
                { TranslatesAutoresizingMaskIntoConstraints = false };
            var inspectorDocument = new AnalysisReportFlippedView(new CGRect(0, 0, 340, 720));
            var inspectorScroll = new NSScrollView
            {
                DocumentView = inspectorDocument,
                HasVerticalScroller = true,
                BorderType = NSBorderType.NoBorder,
                DrawsBackground = true,
                BackgroundColor = NSColor.WindowBackground,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            var footer = new NSView
                { TranslatesAutoresizingMaskIntoConstraints = false };
            root.AddSubview(mainHost); root.AddSubview(inspectorScroll); root.AddSubview(footer);

            pdfView.AutoScales = true;
            pdfView.DisplaysPageBreaks = true;
            pdfView.DisplayMode = PdfDisplayMode.SinglePageContinuous;
            pdfView.Hidden = true;
            pdfView.BackgroundColor = NSColor.UnderPageBackground;
            pdfView.TranslatesAutoresizingMaskIntoConstraints = false;
            placeholder.Alignment = NSTextAlignment.Center;
            placeholder.Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);
            placeholder.MaximumNumberOfLines = 4;
            placeholder.TextColor = NSColor.SecondaryLabel;
            placeholder.TranslatesAutoresizingMaskIntoConstraints = false;
            previewHost.AddSubview(pdfView); previewHost.AddSubview(placeholder);

            var workspaceHeader = new NSView
                { TranslatesAutoresizingMaskIntoConstraints = false };
            var workspaceSeparator = new NSBox { BoxType = NSBoxType.NSBoxSeparator, TranslatesAutoresizingMaskIntoConstraints = false };
            workspaceSelector.TranslatesAutoresizingMaskIntoConstraints = false;
            workspaceHeader.AddSubview(workspaceSelector);
            workspaceHeader.AddSubview(workspaceSeparator);
            mainHost.AddSubview(workspaceHeader);
            mainHost.AddSubview(previewHost);
            mainHost.AddSubview(interpretationHost);

            var interpretationTitle = Label("Report interpretation");
            interpretationTitle.Font = NSFont.BoldSystemFontOfSize(16);
            interpretationWorkspaceStatus.Font = NSFont.SystemFontOfSize(11);
            interpretationWorkspaceStatus.TextColor = NSColor.SecondaryLabel;
            interpretationWorkspaceStatus.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            interpretationWorkspaceStatus.MaximumNumberOfLines = 2;
            var interpretationHeading = VerticalStack(interpretationTitle, interpretationWorkspaceStatus);
            interpretationHeading.Spacing = 3;
            var interpretationScroll = FlexibleTextEditor(interpretationText);
            interpretationHost.AddSubview(interpretationHeading);
            interpretationHost.AddSubview(interpretationScroll);
            previewHost.Hidden = true;
            interpretationHost.Hidden = false;

            var inspector = new AnalysisReportFlippedStackView(new CGRect(16, 16, 308, 560))
            {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Alignment = NSLayoutAttribute.Width,
                Distribution = NSStackViewDistribution.Fill,
                Spacing = 10,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            inspectorDocument.AddSubview(inspector);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                inspector.LeadingAnchor.ConstraintEqualToAnchor(inspectorDocument.LeadingAnchor, 16),
                inspector.TrailingAnchor.ConstraintEqualToAnchor(inspectorDocument.TrailingAnchor, -16),
                inspector.TopAnchor.ConstraintEqualToAnchor(inspectorDocument.TopAnchor, 16),
            });

            var inspectorTitle = Label("Build Analysis Report");
            inspectorTitle.Font = NSFont.BoldSystemFontOfSize(16);
            var inspectorHelp = Label("Choose the result and content to include in the PDF.");
            inspectorHelp.TextColor = NSColor.SecondaryLabel;
            inspectorHelp.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            inspectorHelp.MaximumNumberOfLines = 2;
            selectResultsButton.ControlSize = NSControlSize.Large;
            selectResultsButton.Alignment = NSTextAlignment.Left;
            resultSummaryLabel.Font = NSFont.SystemFontOfSize(11);
            resultSummaryLabel.TextColor = NSColor.SecondaryLabel;
            resultSummaryLabel.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            resultSummaryLabel.MaximumNumberOfLines = 2;
            inspector.AddArrangedSubview(VerticalStack(inspectorTitle, inspectorHelp));
            inspector.AddArrangedSubview(Section("Report contents", selectResultsButton, resultSummaryLabel));
            inspector.AddArrangedSubview(Section("Document", Row("Title", titleField), Row("Subtitle", labelField)));
            inspector.AddArrangedSubview(Section("Presentation", Row("Energy", energyPopup), Row("Temperature", temperaturePopup), Row("Uncertainties", uncertaintyPopup)));
            selectAllButton.Activated += (sender, e) => SetAllAdvanced(true);
            clearButton.Activated += (sender, e) => SetAllAdvanced(false);
            var advancedActions = HorizontalStack(selectAllButton, clearButton);
            advancedActions.Distribution = NSStackViewDistribution.FillEqually;
            injectionTablesButton.SetButtonType(NSButtonType.Switch);
            injectionTablesButton.State = sessionIncludeInjectionTables ? NSCellStateValue.On : NSCellStateValue.Off;
            condenseRepeatedButton.SetButtonType(NSButtonType.Switch);
            condenseRepeatedButton.State = sessionCondenseRepeatedExperiments ? NSCellStateValue.On : NSCellStateValue.Off;
            inspector.AddArrangedSubview(Section("Optional content", injectionTablesButton,
                condenseRepeatedButton, advancedStack, advancedActions));
            interpretationSummaryLabel.Font = NSFont.SystemFontOfSize(11);
            interpretationSummaryLabel.TextColor = NSColor.SecondaryLabel;
            interpretationSummaryLabel.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            interpretationSummaryLabel.MaximumNumberOfLines = 2;
            var interpretationActions = HorizontalStack(editInterpretationButton, generateInterpretationButton);
            interpretationActions.Distribution = NSStackViewDistribution.FillEqually;
            inspector.AddArrangedSubview(Section("Interpretation", interpretationSummaryLabel, interpretationActions));
            foreach (var arranged in inspector.ArrangedSubviews)
            {
                arranged.WidthAnchor.ConstraintEqualToAnchor(inspector.WidthAnchor).Active = true;
                arranged.SetContentHuggingPriorityForOrientation(999, NSLayoutConstraintOrientation.Vertical);
                arranged.SetContentCompressionResistancePriority(999, NSLayoutConstraintOrientation.Vertical);
            }
            var cancel = Button("Cancel");
            cancel.Activated += (sender, e) =>
            {
                CommitInterpretationEditor();
                PresentingViewController?.DismissViewController(this);
            };
            previewButton.Activated += async (sender, e) => await PreviewAsync();
            exportButton.Activated += async (sender, e) => await ExportAsync();
            progress.Hidden = true; progress.ControlSize = NSControlSize.Small; progress.TranslatesAutoresizingMaskIntoConstraints = false;
            statusLabel.LineBreakMode = NSLineBreakMode.ByWordWrapping; statusLabel.MaximumNumberOfLines = 2;
            var actions = HorizontalStack(cancel, previewButton, exportButton);
            var footerSeparator = new NSBox
                { BoxType = NSBoxType.NSBoxSeparator, TranslatesAutoresizingMaskIntoConstraints = false };
            footer.AddSubview(footerSeparator); footer.AddSubview(progress); footer.AddSubview(statusLabel); footer.AddSubview(actions);
            statusLabel.TranslatesAutoresizingMaskIntoConstraints = false; actions.TranslatesAutoresizingMaskIntoConstraints = false;

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                mainHost.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                mainHost.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                mainHost.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                mainHost.TrailingAnchor.ConstraintEqualToAnchor(inspectorScroll.LeadingAnchor),
                inspectorScroll.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                inspectorScroll.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                inspectorScroll.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                inspectorScroll.WidthAnchor.ConstraintEqualToConstant(340),
                footer.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                footer.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                footer.BottomAnchor.ConstraintEqualToAnchor(root.BottomAnchor),
                footer.HeightAnchor.ConstraintEqualToConstant(64),
                footerSeparator.LeadingAnchor.ConstraintEqualToAnchor(footer.LeadingAnchor),
                footerSeparator.TrailingAnchor.ConstraintEqualToAnchor(footer.TrailingAnchor),
                footerSeparator.TopAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                footerSeparator.HeightAnchor.ConstraintEqualToConstant(1),
                workspaceHeader.LeadingAnchor.ConstraintEqualToAnchor(mainHost.LeadingAnchor),
                workspaceHeader.TrailingAnchor.ConstraintEqualToAnchor(mainHost.TrailingAnchor),
                workspaceHeader.TopAnchor.ConstraintEqualToAnchor(mainHost.TopAnchor),
                workspaceHeader.HeightAnchor.ConstraintEqualToConstant(44),
                workspaceSelector.CenterXAnchor.ConstraintEqualToAnchor(workspaceHeader.CenterXAnchor),
                workspaceSelector.CenterYAnchor.ConstraintEqualToAnchor(workspaceHeader.CenterYAnchor),
                workspaceSelector.WidthAnchor.ConstraintEqualToConstant(248),
                workspaceSeparator.LeadingAnchor.ConstraintEqualToAnchor(workspaceHeader.LeadingAnchor),
                workspaceSeparator.TrailingAnchor.ConstraintEqualToAnchor(workspaceHeader.TrailingAnchor),
                workspaceSeparator.BottomAnchor.ConstraintEqualToAnchor(workspaceHeader.BottomAnchor),
                workspaceSeparator.HeightAnchor.ConstraintEqualToConstant(1),
                previewHost.LeadingAnchor.ConstraintEqualToAnchor(mainHost.LeadingAnchor),
                previewHost.TrailingAnchor.ConstraintEqualToAnchor(mainHost.TrailingAnchor),
                previewHost.TopAnchor.ConstraintEqualToAnchor(workspaceHeader.BottomAnchor),
                previewHost.BottomAnchor.ConstraintEqualToAnchor(mainHost.BottomAnchor),
                interpretationHost.LeadingAnchor.ConstraintEqualToAnchor(mainHost.LeadingAnchor),
                interpretationHost.TrailingAnchor.ConstraintEqualToAnchor(mainHost.TrailingAnchor),
                interpretationHost.TopAnchor.ConstraintEqualToAnchor(workspaceHeader.BottomAnchor),
                interpretationHost.BottomAnchor.ConstraintEqualToAnchor(mainHost.BottomAnchor),
                interpretationHeading.LeadingAnchor.ConstraintEqualToAnchor(interpretationHost.LeadingAnchor, 24),
                interpretationHeading.TrailingAnchor.ConstraintEqualToAnchor(interpretationHost.TrailingAnchor, -24),
                interpretationHeading.TopAnchor.ConstraintEqualToAnchor(interpretationHost.TopAnchor, 22),
                interpretationScroll.LeadingAnchor.ConstraintEqualToAnchor(interpretationHost.LeadingAnchor, 24),
                interpretationScroll.TrailingAnchor.ConstraintEqualToAnchor(interpretationHost.TrailingAnchor, -24),
                interpretationScroll.TopAnchor.ConstraintEqualToAnchor(interpretationHeading.BottomAnchor, 14),
                interpretationScroll.BottomAnchor.ConstraintEqualToAnchor(interpretationHost.BottomAnchor, -24),
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
            SetAccessibilityLabel(selectResultsButton, "Select report contents");
            SetAccessibilityLabel(resultSummaryLabel, "Selected report contents details");
            SetAccessibilityLabel(labelField, "Report subtitle");
            SetAccessibilityLabel(titleField, "Report title");
            SetAccessibilityLabel(energyPopup, "Energy units");
            SetAccessibilityLabel(temperaturePopup, "Temperature units");
            SetAccessibilityLabel(uncertaintyPopup, "Uncertainties");
            SetAccessibilityLabel(injectionTablesButton, "Include injection tables");
            SetAccessibilityLabel(condenseRepeatedButton, "Condense repeated experiments");
            SetAccessibilityLabel(workspaceSelector, "Report workspace view");
            SetAccessibilityLabel(interpretationHost, "Interpretation workspace");
            SetAccessibilityLabel(previewHost, "Report preview workspace");
            SetAccessibilityLabel(interpretationText, "Report interpretation editor");
            SetAccessibilityLabel(interpretationSummaryLabel, "Interpretation status");
            SetAccessibilityLabel(editInterpretationButton, "Edit report interpretation");
            SetAccessibilityLabel(generateInterpretationButton, "Generate interpretation with AI");
            SetAccessibilityLabel(previewButton, "Update report preview");
            SetAccessibilityLabel(exportButton, "Export analysis report as PDF");
            SetAccessibilityLabel(statusLabel, "Report status");
            selectResultsButton.Activated += (sender, e) => OpenResultPopover();
            labelField.Changed += (sender, e) => MarkStale(); titleField.Changed += (sender, e) => { if (!changingResult) automaticTitle = false; MarkStale(); };
            energyPopup.Activated += (sender, e) => { sessionEnergyIndex = (int)energyPopup.IndexOfSelectedItem; MarkStale(); };
            temperaturePopup.Activated += (sender, e) => { sessionTemperatureIndex = (int)temperaturePopup.IndexOfSelectedItem; MarkStale(); };
            uncertaintyPopup.Activated += (sender, e) => { sessionUncertaintyIndex = (int)uncertaintyPopup.IndexOfSelectedItem; MarkStale(); };
            injectionTablesButton.Activated += (sender, e) =>
            {
                sessionIncludeInjectionTables = injectionTablesButton.State == NSCellStateValue.On;
                MarkStale();
            };
            condenseRepeatedButton.Activated += (sender, e) =>
            {
                sessionCondenseRepeatedExperiments = condenseRepeatedButton.State == NSCellStateValue.On;
                MarkStale();
            };
            workspaceSelector.Activated += async (sender, e) => await WorkspaceSelectionChangedAsync();
            interpretationText.Changed = () => { if (!loadingInterpretation) MarkStale(); };
            interpretationText.EditingEnded = () => CommitInterpretationEditor();
            editInterpretationButton.Activated += (sender, e) => ShowInterpretationWorkspace(true);
            generateInterpretationButton.Activated += (sender, e) => GenerateInterpretation();
            BuildResultPopover();
            PopulateResults();
        }

        void PopulateResults()
        {
            results.Clear(); results.AddRange(DataManager.Results);
            experiments.Clear(); experiments.AddRange(DataManager.Data);
            var selected = initiallySelected != null && results.Contains(initiallySelected) ? initiallySelected : DataManager.SelectedResult;
            if (selected == null) selected = results.FirstOrDefault();
            var initialResults = selected == null ? Array.Empty<AnalysisResult>() : new[] { selected };
            ApplyResultSelection(initialResults, false, AppSettings.AutoSelectReportReferenceExperiments
                ? ReferenceExperimentsFor(initialResults) : Enumerable.Empty<ExperimentData>());
        }

        void BuildResultPopover()
        {
            // Content is rebuilt whenever the popover opens so its check state always
            // reflects the currently applied report selection.
        }

        void OpenResultPopover()
        {
            resultPickerButtons.Clear();
            var view = new NSView(new CGRect(0, 0, 430, 420));
            const double rowHeight = 52;
            const double rowSpacing = 6;
            const double separatorHeight = 1;
            var hasCategorySeparator = results.Count > 0 && experiments.Count > 0;
            var arrangedItemCount = results.Count + experiments.Count + (hasCategorySeparator ? 1 : 0);
            var stackHeight = Math.Max(rowHeight,
                (results.Count + experiments.Count) * rowHeight
                + (hasCategorySeparator ? separatorHeight : 0)
                + Math.Max(0, arrangedItemCount - 1) * rowSpacing);
            var documentHeight = stackHeight + 24;
            var document = new AnalysisReportFlippedView(new CGRect(0, 0, 406, documentHeight));
            var stack = new AnalysisReportFlippedStackView(new CGRect(12, 12, 382, stackHeight))
            {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Alignment = NSLayoutAttribute.Width,
                Distribution = NSStackViewDistribution.Fill,
                Spacing = (nfloat)rowSpacing,
            }
            ;
            document.AddSubview(stack);
            Action<ITCDataContainer> addPickerRow = item =>
            {
                var result = item as AnalysisResult;
                var experiment = item as ExperimentData;
                var details = result != null ? ResultPickerDetails(result) : ExperimentPickerDetails(experiment);
                var button = new NSButton
                {
                    Title = (result != null ? "Result · " : "Experiment · ") + item.Name + "\n" + details,
                    ToolTip = details,
                    Alignment = NSTextAlignment.Left,
                };
                button.SetButtonType(NSButtonType.Switch);
                button.HeightAnchor.ConstraintEqualToConstant((nfloat)rowHeight).Active = true;
                button.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
                button.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
                button.State = result != null
                    ? (selectedResults.Contains(result) ? NSCellStateValue.On : NSCellStateValue.Off)
                    : (selectedSupportingExperiments.Contains(experiment) ? NSCellStateValue.On : NSCellStateValue.Off);
                SetAccessibilityLabel(button, "Include " + (result != null ? "result " : "supporting experiment ")
                    + item.Name + ". " + details);
                resultPickerButtons.Add(button, item); stack.AddArrangedSubview(button);
                button.WidthAnchor.ConstraintEqualToAnchor(stack.WidthAnchor).Active = true;
            };
            foreach (var result in results) addPickerRow(result);
            if (hasCategorySeparator)
            {
                var separator = new NSBox
                {
                    BoxType = NSBoxType.NSBoxSeparator,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                separator.HeightAnchor.ConstraintEqualToConstant((nfloat)separatorHeight).Active = true;
                stack.AddArrangedSubview(separator);
                separator.WidthAnchor.ConstraintEqualToAnchor(stack.WidthAnchor).Active = true;
            }
            foreach (var experiment in experiments) addPickerRow(experiment);
            var scroll = new NSScrollView
            {
                DocumentView = document, HasVerticalScroller = true,
                BorderType = NSBorderType.NoBorder, TranslatesAutoresizingMaskIntoConstraints = false,
            };
            var selectAll = Button("Select All"); var clear = Button("Clear");
            var cancel = Button("Cancel"); var apply = Button("Apply");
            Action updateApply = () => apply.Enabled = resultPickerButtons.Any(item =>
                item.Key.State == NSCellStateValue.On && item.Value is AnalysisResult);
            Action updateExperimentAvailability = () =>
            {
                var coveredIds = resultPickerButtons
                    .Where(item => item.Value is AnalysisResult && item.Key.State == NSCellStateValue.On)
                    .SelectMany(item => ((AnalysisResult)item.Value).Solution?.Solutions ?? new List<SolutionInterface>())
                    .Select(solution => solution?.Data?.UniqueID)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var item in resultPickerButtons.Where(item => item.Value is ExperimentData))
                    item.Key.Enabled = !coveredIds.Contains(item.Value.UniqueID);
            };
            foreach (var item in resultPickerButtons)
                item.Key.Activated += (sender, e) =>
                {
                    if (AppSettings.AutoSelectReportReferenceExperiments
                        && item.Value is AnalysisResult result
                        && item.Key.State == NSCellStateValue.On)
                    {
                        var references = ReferenceExperimentsFor(new[] { result }).ToHashSet();
                        foreach (var referenceButton in resultPickerButtons.Where(candidate =>
                            candidate.Value is ExperimentData experiment && references.Contains(experiment)))
                            referenceButton.Key.State = NSCellStateValue.On;
                    }
                    updateExperimentAvailability();
                    updateApply();
                };
            selectAll.Activated += (sender, e) =>
            {
                foreach (var item in resultPickerButtons.Where(item => item.Value is AnalysisResult))
                    item.Key.State = NSCellStateValue.On;
                updateExperimentAvailability();
                foreach (var item in resultPickerButtons.Where(item => item.Value is ExperimentData && item.Key.Enabled))
                    item.Key.State = NSCellStateValue.On;
                updateApply();
            };
            clear.Activated += (sender, e) =>
            {
                foreach (var button in resultPickerButtons.Keys) button.State = NSCellStateValue.Off;
                updateExperimentAvailability(); updateApply();
            };
            cancel.Activated += (sender, e) => resultPopover.Close();
            apply.Activated += (sender, e) =>
            {
                if (!CommitInterpretationEditor()) return;
                var chosen = results.Where(result => resultPickerButtons.Any(item =>
                    ReferenceEquals(item.Value, result) && item.Key.State == NSCellStateValue.On)).ToList();
                var chosenExperiments = experiments.Where(experiment => resultPickerButtons.Any(item =>
                    ReferenceEquals(item.Value, experiment) && item.Key.State == NSCellStateValue.On)).ToList();
                if (chosen.Count == 0) return;
                ApplyResultSelection(chosen, true, chosenExperiments); resultPopover.Close();
            };
            var left = HorizontalStack(selectAll, clear); var right = HorizontalStack(cancel, apply);
            view.AddSubview(scroll); view.AddSubview(left); view.AddSubview(right);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                scroll.LeadingAnchor.ConstraintEqualToAnchor(view.LeadingAnchor, 12),
                scroll.TrailingAnchor.ConstraintEqualToAnchor(view.TrailingAnchor, -12),
                scroll.TopAnchor.ConstraintEqualToAnchor(view.TopAnchor, 12),
                scroll.BottomAnchor.ConstraintEqualToAnchor(view.BottomAnchor, -54),
                left.LeadingAnchor.ConstraintEqualToAnchor(view.LeadingAnchor, 12),
                left.BottomAnchor.ConstraintEqualToAnchor(view.BottomAnchor, -12),
                right.TrailingAnchor.ConstraintEqualToAnchor(view.TrailingAnchor, -12),
                right.BottomAnchor.ConstraintEqualToAnchor(view.BottomAnchor, -12),
            });
            resultPopover.ContentViewController = new NSViewController { View = view };
            resultPopover.ContentSize = view.Frame.Size; updateExperimentAvailability(); updateApply();
            resultPopover.Show(selectResultsButton.Bounds, selectResultsButton, NSRectEdge.MaxYEdge);
        }

        void ApplyResultSelection(IEnumerable<AnalysisResult> selection, bool preserveCustomTitle,
            IEnumerable<ExperimentData> supportingSelection = null)
        {
            changingResult = true;
            try
            {
                selectedResults.Clear();
                selectedResults.AddRange(results.Where(result => selection.Contains(result)));
                condenseRepeatedButton.Enabled = !busy
                    && AnalysisReportBuilder.HasRepeatedExperiments(selectedResults);
                selectedSupportingExperiments.Clear();
                var requestedSupporting = supportingSelection ?? Enumerable.Empty<ExperimentData>();
                selectedSupportingExperiments.AddRange(experiments.Where(requestedSupporting.Contains));
                var effectiveCount = EffectiveSupportingExperiments().Count;
                selectResultsButton.Title = selectedResults.Count == 0 ? "Select report contents…"
                    : selectedResults.Count + " result" + (selectedResults.Count == 1 ? "" : "s")
                    + (effectiveCount > 0 ? " · " + effectiveCount + " supporting" : "");
                resultSummaryLabel.StringValue = selectedResults.Count == 0 ? "No result selected."
                    : ResultSummary(selectedResults) + " · " + effectiveCount
                        + " supporting experiment" + (effectiveCount == 1 ? "" : "s") + (selectedResults.Count > 1
                        ? "\n" + string.Join(" · ", selectedResults.Select(result => result.Name)) : "");
                if (!preserveCustomTitle || automaticTitle)
                {
                    automaticTitle = true;
                    titleField.StringValue = selectedResults.Count == 1 ? selectedResults[0].Name
                        : selectedResults.Count > 1 ? "Analysis report" : "";
                }
                RebuildAdvanced();
                LoadReport(selectedResults, selectedSupportingExperiments);
                if (selectedSupportingExperiments.Count > 0) EnsureReportRegistered();
                if (selectedResults.Count > 0) ValidateSelection();
                else
                {
                    previewButton.Enabled = exportButton.Enabled = false;
                    SetStatus("");
                }
            }
            finally { changingResult = false; }
            MarkStale();
        }

        void LoadReport(IReadOnlyList<AnalysisResult> selection, IReadOnlyList<ExperimentData> supporting)
        {
            var ids = selection.Select(result => result.UniqueID).ToList();
            report = ids.Count == 0 ? null : DataManager.Reports.FirstOrDefault(item =>
                item.ResultIds.SequenceEqual(ids, StringComparer.Ordinal)
                && item.SupportingExperimentIds.SequenceEqual(
                    supporting.Select(experiment => experiment.UniqueID), StringComparer.Ordinal));
            if (report == null && ids.Count > 0)
            {
                report = new AnalysisReport { Name = ids.Count == 1 ? selection[0].Name + " report" : "Analysis report" };
                report.SetResultIds(ids);
                report.SetSupportingExperimentIds(supporting.Select(experiment => experiment.UniqueID));
            }
            loadingInterpretation = true;
            SetText(interpretationText, report?.ApprovedInterpretation?.InterpretationMarkdown ?? "");
            loadingInterpretation = false;
            UpdateInterpretationStatus();
        }

        List<ExperimentData> EffectiveSupportingExperiments()
        {
            var memberIds = selectedResults.SelectMany(result => result.Solution?.Solutions ?? new List<SolutionInterface>())
                .Select(solution => solution?.Data?.UniqueID).Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            return selectedSupportingExperiments.Where(experiment => !memberIds.Contains(experiment.UniqueID)).ToList();
        }

        IEnumerable<ExperimentData> ReferenceExperimentsFor(IEnumerable<AnalysisResult> selection)
        {
            var referenceIds = selection
                .SelectMany(result => result.Solution?.Solutions ?? new List<SolutionInterface>())
                .Select(solution => solution?.Data?.BufferSubtractionSettings?.ReferenceExperimentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            return experiments.Where(experiment => referenceIds.Contains(experiment.UniqueID));
        }

        string ExperimentPickerDetails(ExperimentData experiment)
        {
            if (experiment == null) return "Unavailable experiment";
            var processing = experiment.HasThermogram
                ? experiment.Processor?.BaselineCompleted == true ? "processed thermogram" : "thermogram; baseline incomplete"
                : experiment.Injections?.Any(injection => injection.IsIntegrated) == true ? "integrated heats only" : "processing incomplete";
            var coveringIndex = selectedResults.FindIndex(result =>
                (result.Solution?.Solutions ?? new List<SolutionInterface>())
                .Any(solution => solution?.Data?.UniqueID == experiment.UniqueID));
            var relation = coveringIndex >= 0 ? " · Included through Result " + (coveringIndex + 1) : "";
            if (coveringIndex < 0)
            {
                for (var resultIndex = 0; resultIndex < selectedResults.Count && relation.Length == 0; resultIndex++)
                for (var memberIndex = 0; memberIndex < (selectedResults[resultIndex].Solution?.Solutions?.Count ?? 0); memberIndex++)
                    if (selectedResults[resultIndex].Solution.Solutions[memberIndex]?.Data?.BufferSubtractionSettings?.ReferenceExperimentId == experiment.UniqueID)
                        relation = " · Subtraction reference for "
                            + AnalysisReportReferenceLabels.Experiment(resultIndex, memberIndex);
            }
            var date = experiment.DateSource == ExperimentDateSource.DataFile
                || experiment.DateSource == ExperimentDateSource.UserModified
                ? experiment.Date.ToString("d") + " · " : "";
            return date + processing + relation;
        }

        static string ResultPickerDetails(AnalysisResult result)
        {
            var model = result.Model?.ModelType.GetProperties().Name ?? "Unknown model";
            var count = result.Solution?.Solutions?.Count ?? 0;
            return result.Date.ToString("g") + " · " + model + " · " + count +
                " experiment" + (count == 1 ? "" : "s");
        }

        void EnsureReportRegistered()
        {
            if (report != null && !DataManager.Reports.Contains(report)) DataManager.AddReport(report);
        }

        bool CommitInterpretationEditor()
        {
            if (loadingInterpretation || report == null) return true;
            var current = report.ApprovedInterpretation?.InterpretationMarkdown ?? "";
            var edited = interpretationText.String ?? "";
            if (string.Equals(current.Trim(), edited.Trim(), StringComparison.Ordinal)) return true;
            try
            {
                report.UpdateApprovedInterpretationText(edited);
                EnsureReportRegistered();
                UpdateInterpretationStatus();
                return true;
            }
            catch (AnalysisInterpretationValidationException ex)
            {
                SetStatus(ex.Errors.FirstOrDefault() ?? ex.Message, true);
                return false;
            }
        }

        void GenerateInterpretation()
        {
            if (busy || report == null || selectedResults.Count == 0) return;
            AnalysisInterpretationViewController sheet = null;
            sheet = new AnalysisInterpretationViewController(report,
                id => results.FirstOrDefault(result => result.UniqueID == id),
                id => experiments.FirstOrDefault(experiment => experiment.UniqueID == id), InterpretationHttpClient,
                () => { EnsureReportRegistered(); MarkStale(); UpdateInterpretationStatus(); }, draft =>
                {
                    if (draft == null) return;
                    report.ApproveInterpretation(draft);
                    EnsureReportRegistered();
                    loadingInterpretation = true;
                    SetText(interpretationText, report.ApprovedInterpretation?.InterpretationMarkdown ?? "");
                    loadingInterpretation = false;
                    MarkStale();
                    UpdateInterpretationStatus();
                    ShowInterpretationWorkspace(true);
                });
            PresentViewControllerAsSheet(sheet);
        }

        void RebuildAdvanced()
        {
            foreach (var view in advancedStack.ArrangedSubviews.ToArray()) { advancedStack.RemoveArrangedSubview(view); view.RemoveFromSuperview(); view.Dispose(); }
            advancedButtons.Clear();
            if (selectedResults.Count == 0)
            {
                selectAllButton.Hidden = true;
                clearButton.Hidden = true;
                return;
            }
            var descriptors = selectedResults.SelectMany(AnalysisReportBuilder.GetAvailableAdvancedSections)
                .GroupBy(item => item.Request.Kind).Select(group => group.First()).ToList();
            SessionAdvancedSelections.TryGetValue(ResultKey(selectedResults), out var selected);
            selected = selected ?? descriptors.Select(item => item.Request.Kind.ToString()).ToHashSet();
            foreach (var descriptor in descriptors)
            {
                var button = new NSButton { Title = descriptor.Title, ToolTip = descriptor.Description };
                button.SetButtonType(NSButtonType.Switch); button.State = selected.Contains(descriptor.Request.Kind.ToString()) ? NSCellStateValue.On : NSCellStateValue.Off;
                SetAccessibilityLabel(button, "Include " + descriptor.Title);
                button.Activated += (sender, e) => { SaveAdvanced(); MarkStale(); };
                advancedButtons.Add(button, descriptor); advancedStack.AddArrangedSubview(button);
            }
            if (advancedButtons.Count == 0) advancedStack.AddArrangedSubview(Label("No saved advanced analyses are available."));
            var showBulkActions = advancedButtons.Count > 1;
            selectAllButton.Hidden = !showBulkActions;
            clearButton.Hidden = !showBulkActions;
        }

        void SetAllAdvanced(bool selected)
        {
            foreach (var button in advancedButtons.Keys) button.State = selected ? NSCellStateValue.On : NSCellStateValue.Off;
            SaveAdvanced(); MarkStale();
        }

        void SaveAdvanced()
        {
            if (selectedResults.Count == 0) return;
            SessionAdvancedSelections[ResultKey(selectedResults)] = advancedButtons.Where(item => item.Key.State == NSCellStateValue.On).Select(item => item.Value.Request.Kind.ToString()).ToHashSet();
        }

        AnalysisReportOptions Options()
        {
            var options = new AnalysisReportOptions
            {
                DocumentLabel = labelField.StringValue,
                Title = titleField.StringValue,
                EnergyUnitFamily = EnergyFamilies[Math.Max(0, Math.Min(EnergyFamilies.Length - 1, (int)energyPopup.IndexOfSelectedItem))],
                EnergyUnitOverride = null,
                UseKelvin = temperaturePopup.IndexOfSelectedItem == 1,
                IncludeInjectionTables = injectionTablesButton.State == NSCellStateValue.On,
                CondenseRepeatedExperiments = condenseRepeatedButton.State == NSCellStateValue.On,
                UncertaintyDisplayStyle = uncertaintyPopup.IndexOfSelectedItem == 1 ? UncertaintyDisplayStyle.StandardDeviation
                    : uncertaintyPopup.IndexOfSelectedItem == 2 ? UncertaintyDisplayStyle.ConfidenceInterval
                    : uncertaintyPopup.IndexOfSelectedItem == 3 ? UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval
                    : uncertaintyPopup.IndexOfSelectedItem == 4 ? UncertaintyDisplayStyle.None : UncertaintyDisplayStyle.Automatic
            };
            foreach (var descriptor in advancedButtons.Where(item => item.Key.State == NSCellStateValue.On).Select(item => item.Value))
                options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(descriptor.Request.Kind, descriptor.Request.CorrelationMemberIndex));
            return options;
        }

        async Task PreviewAsync()
        {
            if (busy || selectedResults.Count == 0) return;
            if (await BuildAsync(true)) ShowPreviewWorkspace();
            else ShowInterpretationWorkspace(true);
        }

        async Task WorkspaceSelectionChangedAsync()
        {
            if (changingWorkspace || busy) return;
            if (workspaceSelector.SelectedSegment == 0)
            {
                ShowInterpretationWorkspace(true);
                return;
            }
            if (stale || currentPdfData == null)
            {
                if (!await BuildAsync(true)) ShowInterpretationWorkspace(true);
                else ShowPreviewWorkspace();
                return;
            }
            ShowPreviewWorkspace();
        }

        void ShowInterpretationWorkspace(bool focusEditor)
        {
            SetWorkspaceSelection(0);
            interpretationHost.Hidden = false;
            previewHost.Hidden = true;
            if (focusEditor && interpretationText.Editable)
                View.Window?.MakeFirstResponder(interpretationText);
        }

        void ShowPreviewWorkspace()
        {
            if (currentPdfDocument == null && currentPdfData != null)
            {
                currentPdfDocument = new PdfDocument(currentPdfData);
                pdfView.Document = currentPdfDocument;
                pdfView.Hidden = false;
                placeholder.Hidden = true;
            }
            SetWorkspaceSelection(1);
            interpretationHost.Hidden = true;
            previewHost.Hidden = false;
        }

        void SetWorkspaceSelection(int index)
        {
            changingWorkspace = true;
            workspaceSelector.SelectedSegment = index;
            changingWorkspace = false;
        }

        async Task<bool> BuildAsync(bool showPreview)
        {
            if (busy || selectedResults.Count == 0) return false;
            if (!CommitInterpretationEditor()) return false;
            var validation = CurrentValidation();
            if (!validation.IsValid) { SetStatus(validation.Errors.FirstOrDefault() ?? "This result cannot be reported.", true); return false; }
            SetBusy(true, showPreview ? "Building preview..." : "Preparing report...");
            try
            {
                var options = Options();
                var output = await Task.Run(() =>
                {
                    var document = report == null
                        ? AnalysisReportBuilder.Build(selectedResults, options)
                        : AnalysisReportBuilder.Build(report,
                            id => results.FirstOrDefault(result => result.UniqueID == id),
                            id => experiments.FirstOrDefault(experiment => experiment.UniqueID == id),
                            options);
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
            if (busy || selectedResults.Count == 0) return;
            if (stale || currentPdfData == null) if (!await BuildAsync(false)) return;
            var panel = NSSavePanel.SavePanel; panel.Title = "Export Analysis Report"; panel.NameFieldStringValue = Sanitize(selectedResults.Count == 1 ? selectedResults[0].Name : titleField.StringValue) + "-analysis-report.pdf"; panel.AllowedFileTypes = new[] { "pdf" }; panel.CanCreateDirectories = true;
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
            if (pdfView.Document != null && CurrentValidation().IsValid)
                SetStatus("Preview is out of date. Select Update Preview to refresh.", false, true);
            UpdateInterpretationStatus();
        }

        void ValidateSelection()
        {
            var validation = CurrentValidation(); previewButton.Enabled = validation.IsValid; exportButton.Enabled = validation.IsValid;
            if (!validation.IsValid) SetStatus(validation.Errors.FirstOrDefault() ?? "This result cannot be reported.", true);
            else if (selectedResults.Any(result => result.Health != AnalysisResultHealth.Valid)) SetStatus("One or more selected results have warnings or may be stale. The report will identify them.", false, true);
            else SetStatus("");
        }

        void SetBusy(bool value, string message)
        {
            busy = value; progress.Hidden = !value; if (value) progress.StartAnimation(this); else progress.StopAnimation(this);
            selectResultsButton.Enabled = labelField.Enabled = titleField.Enabled = energyPopup.Enabled = temperaturePopup.Enabled = uncertaintyPopup.Enabled = !value;
            injectionTablesButton.Enabled = !value && selectedResults.Count > 0;
            condenseRepeatedButton.Enabled = !value
                && AnalysisReportBuilder.HasRepeatedExperiments(selectedResults);
            interpretationText.Editable = !value && selectedResults.Count > 0;
            workspaceSelector.Enabled = !value;
            editInterpretationButton.Enabled = !value && selectedResults.Count > 0;
            generateInterpretationButton.Enabled = !value && selectedResults.Count > 0;
            foreach (var button in advancedButtons.Keys) button.Enabled = !value;
            selectAllButton.Enabled = clearButton.Enabled = !value && advancedButtons.Count > 0;
            previewButton.Enabled = exportButton.Enabled = !value && CurrentValidation().IsValid;
            if (!string.IsNullOrWhiteSpace(message)) SetStatus(message);
        }

        AnalysisReportValidationResult CurrentValidation() => report == null
            ? AnalysisReportBuilder.Validate(selectedResults)
            : AnalysisReportBuilder.Validate(report,
                id => results.FirstOrDefault(result => result.UniqueID == id),
                id => experiments.FirstOrDefault(experiment => experiment.UniqueID == id));

        void UpdateInterpretationStatus()
        {
            var record = report?.ApprovedInterpretation;
            string summary;
            if (record == null) summary = "No interpretation";
            else if (record.Origin == AnalysisInterpretationOrigin.Manual) summary = "Manual";
            else
            {
                summary = record.UserEdited ? "AI-generated, edited" : "AI-generated";
                var freshness = AnalysisInterpretationService.EvaluateFreshness(report,
                    id => results.FirstOrDefault(result => result.UniqueID == id),
                    id => experiments.FirstOrDefault(experiment => experiment.UniqueID == id)).Status;
                summary += freshness == AnalysisInterpretationFreshness.Current ? " • Current"
                    : freshness == AnalysisInterpretationFreshness.Stale ? " • Out of date" : " • Cannot verify";
            }
            interpretationSummaryLabel.StringValue = summary;
            interpretationWorkspaceStatus.StringValue = summary == "No interpretation"
                ? "Write remarks or a scientific interpretation to include in the report."
                : summary;
            interpretationText.Editable = !busy && selectedResults.Count > 0;
            editInterpretationButton.Enabled = !busy && selectedResults.Count > 0;
            generateInterpretationButton.Enabled = !busy && selectedResults.Count > 0;
        }

        void SetStatus(string message, bool error = false, bool warning = false)
        {
            statusLabel.StringValue = message ?? ""; statusLabel.TextColor = error ? NSColor.SystemRed : warning ? NSColor.SystemOrange : NSColor.SecondaryLabel;
        }

        AnalysisResult SelectedResult => selectedResults.Count == 1 ? selectedResults[0] : null;
        static string ResultKey(IEnumerable<AnalysisResult> selection) => string.Join("|", selection.Select(result => string.IsNullOrWhiteSpace(result.UniqueID) ? result.Name + ":" + result.Date.Ticks : result.UniqueID));
        static string ResultTitle(AnalysisResult result) => result.Name + " — " + result.Date.ToString("g");
        static string ResultSummary(IEnumerable<AnalysisResult> selection)
        {
            var selected = (selection ?? Enumerable.Empty<AnalysisResult>()).Where(result => result != null).ToList();
            if (selected.Count == 0) return "No result selected.";
            if (selected.Count > 1) return selected.Count + " results selected • "
                + AnalysisReportBuilder.CountDistinctExperiments(selected) + " distinct experiments";
            var result = selected[0];
            var model = result.Model?.ModelType.GetProperties().Name ?? "Unknown model";
            var count = result.Solution?.Solutions?.Count ?? 0;
            return model + " • " + count + " experiment" + (count == 1 ? "" : "s") + " • " + result.Health;
        }
        static string Sanitize(string value) { var invalid = Path.GetInvalidFileNameChars().ToHashSet(); var cleaned = new string((value ?? "analysis").Select(character => invalid.Contains(character) || character == '/' || character == '\\' ? '-' : character).ToArray()).Trim(' ', '.', '-'); return string.IsNullOrWhiteSpace(cleaned) ? "analysis" : cleaned; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { CommitInterpretationEditor(); pdfView.Document = null; currentPdfDocument?.Dispose(); currentPdfData?.Dispose(); }
            base.Dispose(disposing);
        }

        static NSPopUpButton Popup(params string[] items) { var popup = new NSPopUpButton { TranslatesAutoresizingMaskIntoConstraints = false }; if (items.Length > 0) popup.AddItems(items); return popup; }
        static NSTextField Field()
        {
            var field = new NSTextField { TranslatesAutoresizingMaskIntoConstraints = false, ControlSize = NSControlSize.Regular };
            field.HeightAnchor.ConstraintEqualToConstant(24).Active = true;
            return field;
        }
        static NSTextField Label(string text) => new NSTextField { StringValue = text ?? "", Editable = false, Bordered = false, DrawsBackground = false, TranslatesAutoresizingMaskIntoConstraints = false };
        static NSButton Button(string title) => new NSButton { Title = title, BezelStyle = NSBezelStyle.Rounded, TranslatesAutoresizingMaskIntoConstraints = false };
        static void SetAccessibilityLabel(NSObject control, string label) => control.SetValueForKey(new NSString(label ?? ""), new NSString("accessibilityLabel"));
        static NSStackView VerticalStack(params NSView[] views) { var stack = new NSStackView(new CGRect(0, 0, 100, 100)) { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Distribution = NSStackViewDistribution.Fill, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false }; foreach (var view in views) { stack.AddArrangedSubview(view); view.WidthAnchor.ConstraintEqualToAnchor(stack.WidthAnchor).Active = true; view.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical); view.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical); } return stack; }
        static NSStackView HorizontalStack(params NSView[] views) { var stack = new NSStackView(new CGRect(0, 0, 100, 28)) { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.CenterY, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false }; foreach (var view in views) stack.AddArrangedSubview(view); return stack; }
        static NSView Row(string title, NSView control) { var label = Label(title); label.WidthAnchor.ConstraintEqualToConstant(112).Active = true; control.WidthAnchor.ConstraintGreaterThanOrEqualToConstant(154).Active = true; control.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal); control.SetContentCompressionResistancePriority(999, NSLayoutConstraintOrientation.Horizontal); var row = HorizontalStack(label, control); row.Distribution = NSStackViewDistribution.Fill; row.SetContentHuggingPriorityForOrientation(999, NSLayoutConstraintOrientation.Vertical); return row; }
        static NSSegmentedControl WorkspaceSelector()
        {
            var selector = new NSSegmentedControl
            {
                SegmentCount = 2,
                ControlSize = NSControlSize.Regular,
                SelectedSegment = 0,
            };
            selector.SetLabel("Interpretation", 0);
            selector.SetLabel("Preview", 1);
            selector.SetWidth(122, 0);
            selector.SetWidth(122, 1);
            return selector;
        }
        static NSScrollView FlexibleTextEditor(NSTextView textView)
        {
            ConfigureTextEditor(textView, 400);
            textView.MinSize = new CGSize(0, 0);
            return new NSScrollView
            {
                DocumentView = textView,
                HasVerticalScroller = true,
                HasHorizontalScroller = false,
                AutohidesScrollers = true,
                BorderType = NSBorderType.BezelBorder,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
        }
        static NSScrollView TextEditor(NSTextView textView, double height)
        {
            ConfigureTextEditor(textView, height);
            var scroll = new NSScrollView
            {
                DocumentView = textView,
                HasVerticalScroller = true,
                HasHorizontalScroller = false,
                AutohidesScrollers = true,
                BorderType = NSBorderType.BezelBorder,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            scroll.HeightAnchor.ConstraintEqualToConstant((nfloat)height).Active = true;
            return scroll;
        }
        static void ConfigureTextEditor(NSTextView textView, double height)
        {
            var font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);
            textView.Frame = new CGRect(0, 0, 280, height);
            textView.MinSize = new CGSize(0, height);
            textView.MaxSize = new CGSize(10000000, 10000000);
            textView.AutoresizingMask = NSViewResizingMask.WidthSizable;
            textView.RichText = false; textView.ImportsGraphics = false; textView.AllowsUndo = true;
            textView.Editable = true; textView.Selectable = true;
            textView.DrawsBackground = true; textView.BackgroundColor = NSColor.TextBackground;
            var foreground = AnalysisReportTextView.ForegroundFor(textView);
            textView.TextColor = foreground; textView.InsertionPointColor = foreground;
            textView.Font = font; textView.TextContainerInset = new CGSize(7, 5);
            textView.VerticallyResizable = true; textView.HorizontallyResizable = false;
            textView.DefaultParagraphStyle = new NSMutableParagraphStyle
            {
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
            };
            if (textView.TextContainer != null)
            {
                textView.TextContainer.ContainerSize = new CGSize(280, 10000000);
                textView.TextContainer.WidthTracksTextView = true;
                textView.TextContainer.LineFragmentPadding = 0;
            }
        }
        static void SetText(NSTextView textView, string value)
        {
            using (var attributed = new NSAttributedString(value ?? "", new NSStringAttributes
            {
                Font = textView.Font ?? NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                ForegroundColor = AnalysisReportTextView.ForegroundFor(textView),
            }))
                textView.TextStorage.SetString(attributed);
            (textView as AnalysisReportTextView)?.RefreshForeground();
            textView.NeedsDisplay = true;
        }
        static NSView Section(string title, params NSView[] views)
        {
            var heading = Label(title);
            heading.Font = NSFont.BoldSystemFontOfSize(12);
            var separator = new NSBox
            {
                BoxType = NSBoxType.NSBoxSeparator,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            separator.HeightAnchor.ConstraintEqualToConstant(1).Active = true;
            var stack = VerticalStack(new[] { heading }.Concat(views).Concat(new NSView[] { separator }).ToArray());
            stack.Spacing = 7;
            stack.SetCustomSpacing(11, views.LastOrDefault() ?? heading);
            stack.EdgeInsets = new NSEdgeInsets(2, 0, 2, 0);
            return stack;
        }
    }

    class AnalysisReportTextView : NSTextView
    {
        public AnalysisReportTextView(CGRect frame) : base(frame) { }

        public static NSColor ForegroundFor(NSTextView textView)
            => NSColor.Text;

        public void RefreshForeground()
        {
            BackgroundColor = NSColor.TextBackground;
            ApplyForeground(ForegroundFor(this));
            NeedsDisplay = true;
        }

        void ApplyForeground(NSColor foreground)
        {
            TextColor = foreground;
            InsertionPointColor = foreground;
            if (TextStorage?.Length > 0)
                TextStorage.AddAttribute(NSStringAttributeKey.ForegroundColor, foreground, new NSRange(0, TextStorage.Length));
        }

        public override void ViewDidMoveToWindow()
        {
            base.ViewDidMoveToWindow();
            RefreshForeground();
        }

        public override void ViewDidChangeEffectiveAppearance()
        {
            base.ViewDidChangeEffectiveAppearance();
            RefreshForeground();
        }
    }

    sealed class ReportInterpretationTextView : AnalysisReportTextView
    {
        public ReportInterpretationTextView() : base(new CGRect(0, 0, 280, 118)) { }

        public Action Changed { get; set; }
        public Action EditingEnded { get; set; }
        public override void DidChangeText() { base.DidChangeText(); Changed?.Invoke(); }
        public override bool ResignFirstResponder()
        {
            var resigned = base.ResignFirstResponder();
            if (resigned) EditingEnded?.Invoke();
            return resigned;
        }
    }

    sealed class AnalysisInterpretationViewController : NSViewController
    {
        readonly AnalysisReport report;
        readonly Func<string, AnalysisResult> resultResolver;
        readonly Func<string, ExperimentData> experimentResolver;
        readonly HttpClient httpClient;
        readonly Action ensureRegistered;
        readonly Action<AnalysisInterpretationRecord> completion;
        readonly AnalysisReportTextView question = new AnalysisReportTextView(new CGRect(0, 0, 560, 66));
        readonly AnalysisReportTextView context = new AnalysisReportTextView(new CGRect(0, 0, 560, 120));
        readonly AnalysisReportTextView draft = new AnalysisReportTextView(new CGRect(0, 0, 560, 170));
        readonly NSTextField status = Label("");
        readonly NSProgressIndicator progress = new NSProgressIndicator { Style = NSProgressIndicatorStyle.Spinning, ControlSize = NSControlSize.Small };
        readonly NSButton includeThermograms = Button("Include compressed thermograms");
        readonly NSButton savePackage = Button("Save AI package…");
        readonly NSButton generate = Button("Generate");
        readonly NSButton use = Button("Use in report");
        readonly NSButton cancel = Button("Cancel");
        CancellationTokenSource cancellation;
        AnalysisInterpretationRecord generated;

        public AnalysisInterpretationViewController(AnalysisReport report, Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver, HttpClient httpClient,
            Action ensureRegistered, Action<AnalysisInterpretationRecord> completion)
        {
            this.report = report; this.resultResolver = resultResolver; this.experimentResolver = experimentResolver; this.httpClient = httpClient;
            includeThermograms.SetButtonType(NSButtonType.Switch);
            includeThermograms.State = report.InterpretationSettings.IncludeThermograms ? NSCellStateValue.On : NSCellStateValue.Off;
            this.ensureRegistered = ensureRegistered; this.completion = completion;
            PreferredContentSize = new CGSize(620, 530);
        }

        public override void LoadView()
        {
            View = new NSView(new CGRect(0, 0, 620, 530));
            var content = VerticalStack(
                Heading("Generate Interpretation"),
                Heading("Main question"), TextEditor(question, 66),
                Heading("Additional context"), Hint("Describe the system, cell and syringe contents, expected outcomes, controls, limitations, or caveats."), TextEditor(context, 120),
                includeThermograms, Hint("Raw signal helps assess acquisition and processing. Omitting it reduces the available evidence."),
                progress, status, TextEditor(draft, 170), HorizontalStack(savePackage, cancel, generate, use));
            content.Alignment = NSLayoutAttribute.Width;
            View.AddSubview(content);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                content.LeadingAnchor.ConstraintEqualToAnchor(View.LeadingAnchor, 20),
                content.TrailingAnchor.ConstraintEqualToAnchor(View.TrailingAnchor, -20),
                content.TopAnchor.ConstraintEqualToAnchor(View.TopAnchor, 20),
                content.BottomAnchor.ConstraintLessThanOrEqualToAnchor(View.BottomAnchor, -20),
            });
            var studyContext = report.StudyContext;
            SetText(question, studyContext.ScientificQuestion);
            SetText(context, string.Join("\n\n", new[] { studyContext.SystemDescription, studyContext.AdditionalNotes }
                .Where(value => !string.IsNullOrWhiteSpace(value))));
            progress.Hidden = true; draft.EnclosingScrollView.Hidden = true; use.Hidden = true;
            status.TextColor = NSColor.SecondaryLabel; status.LineBreakMode = NSLineBreakMode.ByWordWrapping; status.MaximumNumberOfLines = 2;
            cancel.Activated += (sender, e) => { if (cancellation != null) cancellation.Cancel(); else Close(null); };
            savePackage.Activated += (sender, e) => SavePackage();
            SetAccessibilityLabel(savePackage, "Save AI package locally without generation");
            generate.Activated += async (sender, e) => await GenerateAsync();
            use.Activated += (sender, e) => UseDraft();
            SetAccessibilityLabel(question, "Main question");
            SetAccessibilityLabel(context, "Additional context");
            SetAccessibilityLabel(draft, "Generated interpretation draft");
            SetAccessibilityLabel(use, "Use generated interpretation in report");
        }

        void SaveInputs()
        {
            var settings = report.InterpretationSettings;
            settings.IncludeThermograms = includeThermograms.State == NSCellStateValue.On;
            report.UpdateInterpretationSettings(settings);
            var studyContext = report.StudyContext;
            studyContext.ScientificQuestion = question.String ?? "";
            studyContext.SystemDescription = "";
            studyContext.AdditionalNotes = context.String ?? "";
            report.UpdateStudyContext(studyContext); ensureRegistered();
        }

        void SavePackage()
        {
            var panel = NSSavePanel.SavePanel;
            panel.Title = "Save AI package";
            panel.NameFieldStringValue = "ftitc-ai-package.zip";
            panel.AllowedFileTypes = new[] { "zip" };
            panel.CanCreateDirectories = true;
            panel.BeginSheet(View.Window, async response =>
            {
                if (response != (int)NSModalResponse.OK || panel.Url == null) return;
                SetBusy(true);
                SetStatus("Building local AI package…");
                try
                {
                    SaveInputs();
                    await Task.Yield();
                    var package = AnalysisInterpretationPackageBuilder.Build(report, resultResolver, experimentResolver, report.InterpretationSettings);
                    var bytes = AnalysisInterpretationDebugExport.CreateArchive(package);
                    File.WriteAllBytes(panel.Url.Path, bytes);
                    SetStatus("AI package saved locally. Nothing was sent to the server.");
                }
                catch (Exception ex) { status.TextColor = NSColor.SystemRed; status.StringValue = "Could not save AI package. " + ex.Message; }
                finally { SetBusy(false); }
            });
        }

        async Task GenerateAsync()
        {
            if (cancellation != null) return;
            var warnings = AnalysisInterpretationService.GetGenerationWarnings(report, resultResolver, experimentResolver);
            if (warnings.Count > 0)
            {
                using var alert = new NSAlert { AlertStyle = NSAlertStyle.Warning, MessageText = "Review analysis warnings",
                    InformativeText = string.Join("\n\n", warnings) + "\n\nThe interpretation will distinguish historical fit evidence from current observations." };
                alert.AddButton("Generate anyway"); alert.AddButton("Cancel");
                if (alert.RunModal() != (int)NSAlertButtonReturn.First) return;
            }
            SaveInputs();
            var generationCancellation = new CancellationTokenSource();
            cancellation = generationCancellation;
            var generationToken = generationCancellation.Token;
            SetBusy(true);
            SetStatus("Building the analysis package…");
            await Task.Yield();
            try
            {
                var provider = new FtItcInterpretationClient(httpClient, new Uri("https://app.ft-itc.org"));
                var generationProgress = new Progress<AnalysisInterpretationProgressUpdate>(update =>
                {
                    if (ReferenceEquals(cancellation, generationCancellation) && !generationToken.IsCancellationRequested)
                        SetStatus(update.Message);
                });
                var output = await new AnalysisInterpretationService(provider).GenerateAsync(
                    report, resultResolver, experimentResolver, report.InterpretationSettings, generationToken, generationProgress);
                generationToken.ThrowIfCancellationRequested();
                generated = output.Interpretation; SetText(draft, generated.InterpretationMarkdown);
                draft.EnclosingScrollView.Hidden = false; use.Hidden = false;
                PreferredContentSize = new CGSize(620, 740);
                SetStatus("Finished — interpretation ready. Review the draft before adding it to the report.");
            }
            catch (AnalysisInterpretationProviderException ex) when (ex.Kind == AnalysisInterpretationFailureKind.Cancelled)
            { SetStatus("Finished — generation cancelled."); }
            catch (OperationCanceledException) { SetStatus("Finished — generation cancelled."); }
            catch (Exception ex) { status.TextColor = NSColor.SystemRed; status.StringValue = "Finished — generation failed. " + ex.Message; }
            finally
            {
                generationCancellation.Dispose();
                if (ReferenceEquals(cancellation, generationCancellation)) cancellation = null;
                SetBusy(false);
            }
        }

        void UseDraft()
        {
            if (generated == null) return;
            try
            {
                var original = generated.InterpretationMarkdown;
                var normalized = AnalysisInterpretationResponseParser.ParseForApproval(draft.String ?? "");
                var candidate = generated.Copy();
                candidate.InterpretationMarkdown = normalized;
                candidate.UserEdited = !string.Equals(original.Trim(), normalized.Trim(), StringComparison.Ordinal);
                candidate.ApprovedAtUtc = DateTime.UtcNow;
                if (report.ApprovedInterpretation != null)
                {
                    var alert = new NSAlert
                    {
                        AlertStyle = NSAlertStyle.Warning,
                        MessageText = "Replace interpretation?",
                        InformativeText = "The report already contains an interpretation. Replace it with this draft?",
                    };
                    alert.AddButton("Replace"); alert.AddButton("Keep Editing");
                    if (alert.RunModal() != (int)NSAlertButtonReturn.First) return;
                }
                Close(candidate);
            }
            catch (AnalysisInterpretationValidationException ex)
            { status.TextColor = NSColor.SystemRed; status.StringValue = ex.Errors.FirstOrDefault() ?? ex.Message; }
        }

        void SetBusy(bool value)
        {
            progress.Hidden = !value; if (value) progress.StartAnimation(this); else progress.StopAnimation(this);
            question.Editable = context.Editable = includeThermograms.Enabled = savePackage.Enabled = generate.Enabled = use.Enabled = !value;
            cancel.Title = value ? "Cancel generation" : "Cancel";
        }

        void SetStatus(string message)
        {
            status.TextColor = NSColor.SecondaryLabel;
            status.StringValue = message ?? "";
        }

        void Close(AnalysisInterpretationRecord value)
        {
            cancellation?.Cancel(); PresentingViewController?.DismissViewController(this); completion(value);
        }

        public override void ViewWillDisappear()
        {
            cancellation?.Cancel(); base.ViewWillDisappear();
        }

        static NSTextField Heading(string text) { var label = Label(text); label.Font = NSFont.BoldSystemFontOfSize(12); return label; }
        static NSTextField Hint(string text) { var label = Label(text); label.Font = NSFont.SystemFontOfSize(11); label.TextColor = NSColor.SecondaryLabel; label.LineBreakMode = NSLineBreakMode.ByWordWrapping; label.MaximumNumberOfLines = 2; return label; }
        static NSScrollView TextEditor(NSTextView textView, double height)
        {
            var font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);
            textView.Frame = new CGRect(0, 0, 560, height);
            textView.MinSize = new CGSize(0, height);
            textView.MaxSize = new CGSize(10000000, 10000000);
            textView.AutoresizingMask = NSViewResizingMask.WidthSizable;
            textView.RichText = false; textView.ImportsGraphics = false; textView.AllowsUndo = true;
            textView.Editable = true; textView.Selectable = true;
            textView.DrawsBackground = true; textView.BackgroundColor = NSColor.TextBackground;
            var foreground = AnalysisReportTextView.ForegroundFor(textView);
            textView.TextColor = foreground; textView.InsertionPointColor = foreground;
            textView.Font = font; textView.TextContainerInset = new CGSize(7, 5);
            textView.VerticallyResizable = true; textView.HorizontallyResizable = false;
            textView.DefaultParagraphStyle = new NSMutableParagraphStyle
            {
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
            };
            if (textView.TextContainer != null)
            {
                textView.TextContainer.ContainerSize = new CGSize(560, 10000000);
                textView.TextContainer.WidthTracksTextView = true;
                textView.TextContainer.LineFragmentPadding = 0;
            }
            var scroll = new NSScrollView
            {
                DocumentView = textView,
                HasVerticalScroller = true,
                HasHorizontalScroller = false,
                AutohidesScrollers = true,
                BorderType = NSBorderType.BezelBorder,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            scroll.HeightAnchor.ConstraintEqualToConstant((nfloat)height).Active = true; return scroll;
        }
        static void SetText(NSTextView textView, string value)
        {
            using (var attributed = new NSAttributedString(value ?? "", new NSStringAttributes
            {
                Font = textView.Font ?? NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                ForegroundColor = AnalysisReportTextView.ForegroundFor(textView),
            }))
                textView.TextStorage.SetString(attributed);
            (textView as AnalysisReportTextView)?.RefreshForeground();
            textView.NeedsDisplay = true;
        }
        static NSTextField Label(string text) => new NSTextField { StringValue = text ?? "", Editable = false, Bordered = false, DrawsBackground = false, TranslatesAutoresizingMaskIntoConstraints = false };
        static NSButton Button(string title) => new NSButton { Title = title, BezelStyle = NSBezelStyle.Rounded, TranslatesAutoresizingMaskIntoConstraints = false };
        static NSStackView VerticalStack(params NSView[] views) { var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false }; foreach (var view in views) { stack.AddArrangedSubview(view); view.WidthAnchor.ConstraintEqualToAnchor(stack.WidthAnchor).Active = true; } return stack; }
        static NSStackView HorizontalStack(params NSView[] views) { var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.CenterY, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false }; foreach (var view in views) stack.AddArrangedSubview(view); return stack; }
        static void SetAccessibilityLabel(NSObject control, string label) => control.SetValueForKey(new NSString(label ?? ""), new NSString("accessibilityLabel"));
    }

    sealed class AnalysisReportFlippedView : NSView
    {
        public AnalysisReportFlippedView(CGRect frame) : base(frame) { }
        public override bool IsFlipped => true;
    }

    sealed class AnalysisReportFlippedStackView : NSStackView
    {
        public AnalysisReportFlippedStackView(CGRect frame) : base(frame) { }
        public override bool IsFlipped => true;
    }
}
