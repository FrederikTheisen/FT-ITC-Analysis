using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using AppKit;
using CoreGraphics;
using Foundation;
using PdfKit;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Utilities;
using AnalysisITC.UI.MacOS.Drawing;

namespace AnalysisITC
{
    sealed class SupportingFigureCanvasWindowController : NSWindowController
    {
        const string CompositionPasteboardType = "com.frederiktheisen.ftitc.supporting-figure-row";

        readonly PublicationFigureOptions figureOptions;
        readonly PublicationFigureCanvasOptions defaults = new PublicationFigureCanvasOptions();
        readonly CoreGraphicsFigureCanvasRenderer renderer = new CoreGraphicsFigureCanvasRenderer();
        readonly List<ITCDataContainer> composition = new List<ITCDataContainer>();
        readonly List<ITCDataContainer> availableSources = new List<ITCDataContainer>();

        readonly SupportingFigureTableView compositionTable = CompositionTable();
        readonly NSTableView sourceTable = Table(true);
        readonly NSSearchField sourceFilter = new NSSearchField();
        readonly NSPopover sourcePopover = new NSPopover { Behavior = NSPopoverBehavior.Transient };
        readonly PdfView pdfView = new PdfView();
        readonly SupportingFigurePreviewSnapshotView previewSnapshotView = new SupportingFigurePreviewSnapshotView();
        readonly NSPopUpButton zoomPopup = new NSPopUpButton();

        readonly NSTextField plotWidthField = Field();
        readonly NSTextField plotHeightField = Field();
        readonly NSTextField fontSizeField = Field();
        readonly NSTextField symbolSizeField = Field();
        readonly NSTextField columnsField = Field();
        readonly NSTextField rowsField = Field();
        readonly NSStepper plotWidthStepper = Stepper(1, 20, 0.1);
        readonly NSStepper plotHeightStepper = Stepper(2, 28, 0.1);
        readonly NSStepper fontSizeStepper = Stepper(5, 24, 0.5);
        readonly NSStepper symbolSizeStepper = Stepper(3, 14, 0.5);
        readonly NSStepper columnsStepper = Stepper(1, 6, 1);
        readonly NSStepper rowsStepper = Stepper(1, 10, 1);
        readonly NSSegmentedControl strokeWidthControl = StrokeControl();
        readonly NSSwitch panelLettersSwitch = Toggle("Panel letters");
        readonly NSSwitch groupResultsSwitch = Toggle("Group result figures");
        readonly NSSwitch informationBoxesSwitch = Toggle("Parameter and information boxes");
        readonly NSTextField plotSizeLabel = ValueLabel("-");
        readonly NSTextField figureSizeLabel = ValueLabel("-");
        readonly NSTextField previewDimensionsLabel = Label("Figure dimensions: -");
        readonly NSTextField statusLabel = Label("");
        readonly NSButton exportButton = Button("Export PDF...");
        readonly NSButton addButton = SymbolButton("plus", "Add Figures...");
        readonly NSButton removeButton = SymbolButton("minus", "Remove Selected Figure");
        readonly NSButton moveUpButton = SymbolButton("chevron.up", "Move Selected Figure Up");
        readonly NSButton moveDownButton = SymbolButton("chevron.down", "Move Selected Figure Down");
        readonly NSButton pickerAddButton = Button("Add Selected");

        SupportingFigureTableDataSource compositionSource;
        SupportingFigureTableDelegate compositionDelegate;
        SupportingFigureTableDataSource pickerSource;
        SupportingFigureTableDelegate pickerDelegate;
        NSTimer previewTimer;
        NSTimer previewTransitionTimer;
        NSData currentPdfData;
        PdfDocument currentPdfDocument;
        NSImage previewSnapshotImage;
        CoreGraphicsFigureCanvasRenderPlan currentPlan;
        NSWindow parentWindow;
        Action closed;
        bool disposed;

        public SupportingFigureCanvasWindowController(PublicationFigureOptions figureOptions, ITCDataContainer selectedItem)
            : base(CreateWindow())
        {
            this.figureOptions = figureOptions ?? new PublicationFigureOptions();
            if (selectedItem != null) composition.Add(selectedItem);

            Window.Title = "Supporting Figure";
            BuildInterface();
            ApplyDefaults();
            ReloadComposition(selectedItem);
            RefreshPreview();
        }

        public void ShowSheet(NSWindow parent, Action onClosed)
        {
            parentWindow = parent;
            closed = onClosed;
            parent.BeginSheet(Window, result =>
            {
                Window.OrderOut(this);
                var callback = closed;
                closed = null;
                callback?.Invoke();
                Dispose();
            });
        }

        void BuildInterface()
        {
            var root = new SupportingFigureBackgroundView(
                new CGRect(0, 0, Window.ContentView.Frame.Width, Window.ContentView.Frame.Height),
                NSColor.WindowBackground)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            };
            Window.ContentView = root;

            var left = new NSVisualEffectView
            {
                Material = NSVisualEffectMaterial.Sidebar,
                BlendingMode = NSVisualEffectBlendingMode.WithinWindow,
                State = NSVisualEffectState.FollowsWindowActiveState,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            var preview = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            var inspector = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            var footer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            var leftSeparator = Separator();
            var rightSeparator = Separator();
            root.AddSubview(left);
            root.AddSubview(leftSeparator);
            root.AddSubview(preview);
            root.AddSubview(rightSeparator);
            root.AddSubview(inspector);
            root.AddSubview(footer);

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                left.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                left.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                left.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                left.WidthAnchor.ConstraintEqualToConstant(300),

                leftSeparator.LeadingAnchor.ConstraintEqualToAnchor(left.TrailingAnchor),
                leftSeparator.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                leftSeparator.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                leftSeparator.WidthAnchor.ConstraintEqualToConstant(1),

                preview.LeadingAnchor.ConstraintEqualToAnchor(leftSeparator.TrailingAnchor),
                preview.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                preview.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),

                rightSeparator.LeadingAnchor.ConstraintEqualToAnchor(preview.TrailingAnchor),
                rightSeparator.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                rightSeparator.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                rightSeparator.WidthAnchor.ConstraintEqualToConstant(1),

                inspector.LeadingAnchor.ConstraintEqualToAnchor(rightSeparator.TrailingAnchor),
                inspector.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                inspector.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                inspector.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                inspector.WidthAnchor.ConstraintEqualToConstant(310),

                footer.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                footer.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                footer.BottomAnchor.ConstraintEqualToAnchor(root.BottomAnchor),
                footer.HeightAnchor.ConstraintEqualToConstant(52),
            });

            BuildCompositionPanel(left);
            BuildPreviewPanel(preview);
            BuildInspector(inspector);
            BuildFooter(footer);
            BuildSourcePopover();
            WireOptionChanges();
        }

        void BuildCompositionPanel(NSView panel)
        {
            var header = Header("Figure Order");
            var scroll = Scroll(compositionTable);
            var toolbar = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            var toolbarSeparator = Separator();
            var buttons = HorizontalStack(addButton, removeButton, moveUpButton, moveDownButton);
            toolbar.AddSubview(toolbarSeparator);
            toolbar.AddSubview(buttons);

            panel.AddSubview(header);
            panel.AddSubview(scroll);
            panel.AddSubview(toolbar);
            header.TranslatesAutoresizingMaskIntoConstraints = false;
            scroll.TranslatesAutoresizingMaskIntoConstraints = false;
            buttons.TranslatesAutoresizingMaskIntoConstraints = false;

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                header.LeadingAnchor.ConstraintEqualToAnchor(panel.LeadingAnchor, 14),
                header.TrailingAnchor.ConstraintEqualToAnchor(panel.TrailingAnchor, -14),
                header.TopAnchor.ConstraintEqualToAnchor(panel.TopAnchor, 14),
                scroll.LeadingAnchor.ConstraintEqualToAnchor(panel.LeadingAnchor),
                scroll.TrailingAnchor.ConstraintEqualToAnchor(panel.TrailingAnchor),
                scroll.TopAnchor.ConstraintEqualToAnchor(header.BottomAnchor, 10),
                scroll.BottomAnchor.ConstraintEqualToAnchor(toolbar.TopAnchor),

                toolbar.LeadingAnchor.ConstraintEqualToAnchor(panel.LeadingAnchor),
                toolbar.TrailingAnchor.ConstraintEqualToAnchor(panel.TrailingAnchor),
                toolbar.BottomAnchor.ConstraintEqualToAnchor(panel.BottomAnchor),
                toolbar.HeightAnchor.ConstraintEqualToConstant(49),
                toolbarSeparator.LeadingAnchor.ConstraintEqualToAnchor(toolbar.LeadingAnchor),
                toolbarSeparator.TrailingAnchor.ConstraintEqualToAnchor(toolbar.TrailingAnchor),
                toolbarSeparator.TopAnchor.ConstraintEqualToAnchor(toolbar.TopAnchor),
                toolbarSeparator.HeightAnchor.ConstraintEqualToConstant(1),
                buttons.LeadingAnchor.ConstraintEqualToAnchor(toolbar.LeadingAnchor, 12),
                buttons.CenterYAnchor.ConstraintEqualToAnchor(toolbar.CenterYAnchor, 1),
            });

            addButton.Activated += (sender, e) => OpenSourcePopover(addButton);
            removeButton.Activated += (sender, e) => RemoveSelected();
            moveUpButton.Activated += (sender, e) => MoveSelected(-1);
            moveDownButton.Activated += (sender, e) => MoveSelected(1);
            compositionTable.DeleteRequested = RemoveSelected;
            compositionTable.MoveRequested = MoveSelected;
            compositionTable.RegisterForDraggedTypes(new[] { CompositionPasteboardType });
            compositionTable.SetDraggingSourceOperationMask(NSDragOperation.Move, true);

            compositionSource = new SupportingFigureTableDataSource(
                composition,
                CompositionPasteboardType,
                ReorderComposition);
            compositionDelegate = new SupportingFigureTableDelegate(
                compositionSource,
                item => item.Name,
                SourceDescription,
                UpdateCompositionActions);
            compositionTable.DataSource = compositionSource;
            compositionTable.Delegate = compositionDelegate;
            UpdateCompositionActions();
        }

        void BuildPreviewPanel(NSView panel)
        {
            var header = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            var title = Header("Preview");
            var zoomLabel = Label("Preview zoom");
            zoomPopup.ControlSize = NSControlSize.Regular;
            zoomPopup.Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);
            zoomPopup.AddItems(new[] { "Fit", "25%", "50%", "75%", "100%" });
            zoomPopup.SelectItem(4);
            var toolbar = HorizontalStack(zoomLabel, zoomPopup);
            var separator = Separator();
            header.AddSubview(title);
            header.AddSubview(previewDimensionsLabel);
            header.AddSubview(toolbar);
            header.AddSubview(separator);
            title.TranslatesAutoresizingMaskIntoConstraints = false;
            previewDimensionsLabel.TranslatesAutoresizingMaskIntoConstraints = false;
            toolbar.TranslatesAutoresizingMaskIntoConstraints = false;

            pdfView.AutoScales = false;
            pdfView.MinScaleFactor = 0.1f;
            pdfView.MaxScaleFactor = 4;
            pdfView.ScaleFactor = 1;
            pdfView.DisplaysPageBreaks = true;
            pdfView.BackgroundColor = NSColor.UnderPageBackground;
            pdfView.TranslatesAutoresizingMaskIntoConstraints = false;
            previewSnapshotView.Hidden = true;
            previewSnapshotView.ImageFrameStyle = NSImageFrameStyle.None;
            previewSnapshotView.ImageScaling = NSImageScale.AxesIndependently;
            previewSnapshotView.TranslatesAutoresizingMaskIntoConstraints = false;

            panel.AddSubview(header);
            panel.AddSubview(pdfView);
            panel.AddSubview(previewSnapshotView);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                header.LeadingAnchor.ConstraintEqualToAnchor(panel.LeadingAnchor),
                header.TrailingAnchor.ConstraintEqualToAnchor(panel.TrailingAnchor),
                header.TopAnchor.ConstraintEqualToAnchor(panel.TopAnchor),
                header.HeightAnchor.ConstraintEqualToConstant(45),
                title.LeadingAnchor.ConstraintEqualToAnchor(header.LeadingAnchor, 14),
                title.CenterYAnchor.ConstraintEqualToAnchor(header.CenterYAnchor, -1),
                previewDimensionsLabel.LeadingAnchor.ConstraintEqualToAnchor(title.TrailingAnchor, 12),
                previewDimensionsLabel.CenterYAnchor.ConstraintEqualToAnchor(title.CenterYAnchor),
                previewDimensionsLabel.TrailingAnchor.ConstraintLessThanOrEqualToAnchor(toolbar.LeadingAnchor, -12),
                toolbar.TrailingAnchor.ConstraintEqualToAnchor(header.TrailingAnchor, -12),
                toolbar.CenterYAnchor.ConstraintEqualToAnchor(title.CenterYAnchor),
                separator.LeadingAnchor.ConstraintEqualToAnchor(header.LeadingAnchor),
                separator.TrailingAnchor.ConstraintEqualToAnchor(header.TrailingAnchor),
                separator.BottomAnchor.ConstraintEqualToAnchor(header.BottomAnchor),
                separator.HeightAnchor.ConstraintEqualToConstant(1),
                pdfView.LeadingAnchor.ConstraintEqualToAnchor(panel.LeadingAnchor),
                pdfView.TrailingAnchor.ConstraintEqualToAnchor(panel.TrailingAnchor),
                pdfView.TopAnchor.ConstraintEqualToAnchor(header.BottomAnchor),
                pdfView.BottomAnchor.ConstraintEqualToAnchor(panel.BottomAnchor),
                previewSnapshotView.LeadingAnchor.ConstraintEqualToAnchor(pdfView.LeadingAnchor),
                previewSnapshotView.TrailingAnchor.ConstraintEqualToAnchor(pdfView.TrailingAnchor),
                previewSnapshotView.TopAnchor.ConstraintEqualToAnchor(pdfView.TopAnchor),
                previewSnapshotView.BottomAnchor.ConstraintEqualToAnchor(pdfView.BottomAnchor),
            });

            zoomPopup.Activated += (sender, e) => ApplyZoom();
        }

        void BuildInspector(NSView panel)
        {
            var document = new SupportingFigureFlippedView(new CGRect(0, 0, 310, 810));
            var scroll = new NSScrollView
            {
                DocumentView = document,
                HasVerticalScroller = true,
                HasHorizontalScroller = false,
                AutohidesScrollers = true,
                BorderType = NSBorderType.NoBorder,
                DrawsBackground = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            panel.AddSubview(scroll);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                scroll.LeadingAnchor.ConstraintEqualToAnchor(panel.LeadingAnchor),
                scroll.TrailingAnchor.ConstraintEqualToAnchor(panel.TrailingAnchor),
                scroll.TopAnchor.ConstraintEqualToAnchor(panel.TopAnchor),
                scroll.BottomAnchor.ConstraintEqualToAnchor(panel.BottomAnchor),
            });

            var stack = new SupportingFigureFlippedStackView(new CGRect(16, 14, 278, 700))
            {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.Width,
                Spacing = 12,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            document.AddSubview(stack);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                stack.LeadingAnchor.ConstraintEqualToAnchor(document.LeadingAnchor, 16),
                stack.TrailingAnchor.ConstraintEqualToAnchor(document.TrailingAnchor, -16),
                stack.TopAnchor.ConstraintEqualToAnchor(document.TopAnchor, 14),
            });

            var inspectorTitle = Header("Figure Settings");
            AddFullWidth(stack, inspectorTitle);
            AddFullWidth(stack, InspectorSection("Plot Size", new[]
            {
                InspectorRow("Width (cm)", NumericEditor(plotWidthField, plotWidthStepper)),
                InspectorRow("Height (cm)", NumericEditor(plotHeightField, plotHeightStepper)),
                InspectorRow("Calculated", plotSizeLabel),
            }));
            AddFullWidth(stack, InspectorSection("Grid", new[]
            {
                InspectorRow("Columns", NumericEditor(columnsField, columnsStepper)),
                InspectorRow("Rows", NumericEditor(rowsField, rowsStepper)),
            }));
            AddFullWidth(stack, InspectorSection("Typography", new[]
            {
                InspectorRow("Base font (pt)", NumericEditor(fontSizeField, fontSizeStepper)),
            }));
            var typeNote = Label("Ticks use the base size; axis titles use base + 1 pt. Parameter boxes use 6 pt and panel letters use 10 pt.");
            typeNote.TextColor = NSColor.SecondaryLabel;
            typeNote.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            typeNote.MaximumNumberOfLines = 4;
            AddFullWidth(stack, typeNote);
            AddFullWidth(stack, InspectorSection("Data Points", new[]
            {
                InspectorRow("Size (pt)", NumericEditor(symbolSizeField, symbolSizeStepper)),
            }));
            AddFullWidth(stack, InspectorSection("Lines and Ticks", new[]
            {
                InspectorRow("Weight", strokeWidthControl),
            }));
            AddFullWidth(stack, InspectorSection("Output", new[]
            {
                InspectorRow("Figure size", figureSizeLabel),
            }));
            AddFullWidth(stack, InspectorSection("Labels", new[]
            {
                InspectorRow("Panel letters", TrailingControl(panelLettersSwitch)),
                InspectorRow("Group result figures", TrailingControl(groupResultsSwitch)),
                InspectorRow("Information boxes", TrailingControl(informationBoxesSwitch)),
            }));
        }

        void BuildFooter(NSView panel)
        {
            var closeButton = Button("Cancel");
            closeButton.ControlSize = NSControlSize.Large;
            exportButton.ControlSize = NSControlSize.Large;
            var buttons = HorizontalStack(closeButton, exportButton);
            var separator = Separator();
            closeButton.KeyEquivalent = "\u001b";
            exportButton.KeyEquivalent = "\r";
            panel.AddSubview(statusLabel);
            panel.AddSubview(buttons);
            panel.AddSubview(separator);
            statusLabel.TranslatesAutoresizingMaskIntoConstraints = false;
            buttons.TranslatesAutoresizingMaskIntoConstraints = false;
            statusLabel.LineBreakMode = NSLineBreakMode.TruncatingTail;

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                separator.LeadingAnchor.ConstraintEqualToAnchor(panel.LeadingAnchor),
                separator.TrailingAnchor.ConstraintEqualToAnchor(panel.TrailingAnchor),
                separator.TopAnchor.ConstraintEqualToAnchor(panel.TopAnchor),
                separator.HeightAnchor.ConstraintEqualToConstant(1),
                statusLabel.LeadingAnchor.ConstraintEqualToAnchor(panel.LeadingAnchor, 14),
                statusLabel.CenterYAnchor.ConstraintEqualToAnchor(panel.CenterYAnchor),
                statusLabel.TrailingAnchor.ConstraintLessThanOrEqualToAnchor(buttons.LeadingAnchor, -12),
                buttons.TrailingAnchor.ConstraintEqualToAnchor(panel.TrailingAnchor, -12),
                buttons.CenterYAnchor.ConstraintEqualToAnchor(panel.CenterYAnchor),
            });

            closeButton.Activated += (sender, e) => CloseSheet();
            exportButton.Activated += (sender, e) => ExportPdf();
        }

        void BuildSourcePopover()
        {
            var view = new NSView(new CGRect(0, 0, 390, 420));
            sourceFilter.TranslatesAutoresizingMaskIntoConstraints = false;
            sourceFilter.ControlSize = NSControlSize.Regular;
            sourceFilter.PlaceholderString = "Filter by name or type";
            var scroll = Scroll(sourceTable);
            scroll.TranslatesAutoresizingMaskIntoConstraints = false;
            var footer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            var separator = Separator();
            var cancel = Button("Cancel");
            var buttons = HorizontalStack(cancel, pickerAddButton);
            footer.AddSubview(separator);
            footer.AddSubview(buttons);
            buttons.TranslatesAutoresizingMaskIntoConstraints = false;
            view.AddSubview(sourceFilter);
            view.AddSubview(scroll);
            view.AddSubview(footer);

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                sourceFilter.LeadingAnchor.ConstraintEqualToAnchor(view.LeadingAnchor, 12),
                sourceFilter.TrailingAnchor.ConstraintEqualToAnchor(view.TrailingAnchor, -12),
                sourceFilter.TopAnchor.ConstraintEqualToAnchor(view.TopAnchor, 12),
                sourceFilter.HeightAnchor.ConstraintEqualToConstant(28),
                scroll.LeadingAnchor.ConstraintEqualToAnchor(view.LeadingAnchor),
                scroll.TrailingAnchor.ConstraintEqualToAnchor(view.TrailingAnchor),
                scroll.TopAnchor.ConstraintEqualToAnchor(sourceFilter.BottomAnchor, 8),
                scroll.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                footer.LeadingAnchor.ConstraintEqualToAnchor(view.LeadingAnchor),
                footer.TrailingAnchor.ConstraintEqualToAnchor(view.TrailingAnchor),
                footer.BottomAnchor.ConstraintEqualToAnchor(view.BottomAnchor),
                footer.HeightAnchor.ConstraintEqualToConstant(50),
                separator.LeadingAnchor.ConstraintEqualToAnchor(footer.LeadingAnchor),
                separator.TrailingAnchor.ConstraintEqualToAnchor(footer.TrailingAnchor),
                separator.TopAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                separator.HeightAnchor.ConstraintEqualToConstant(1),
                buttons.TrailingAnchor.ConstraintEqualToAnchor(footer.TrailingAnchor, -12),
                buttons.CenterYAnchor.ConstraintEqualToAnchor(footer.CenterYAnchor, 1),
            });

            pickerSource = new SupportingFigureTableDataSource(availableSources);
            pickerDelegate = new SupportingFigureTableDelegate(
                pickerSource,
                item => item.Name,
                SourceDescription,
                UpdatePickerActions);
            sourceTable.DataSource = pickerSource;
            sourceTable.Delegate = pickerDelegate;

            var controller = new NSViewController { View = view };
            sourcePopover.ContentViewController = controller;
            sourcePopover.ContentSize = view.Frame.Size;
            sourceFilter.Changed += (sender, e) => RefreshAvailableSources();
            cancel.Activated += (sender, e) => sourcePopover.Close();
            pickerAddButton.Activated += (sender, e) => AddSelectedSources();
            UpdatePickerActions();
        }

        void WireOptionChanges()
        {
            ConfigureNumberField(plotWidthField, 1, 20, 1);
            ConfigureNumberField(plotHeightField, 2, 28, 1);
            ConfigureNumberField(fontSizeField, 5, 24, 1);
            ConfigureNumberField(symbolSizeField, 3, 14, 1);
            ConfigureNumberField(columnsField, 1, 6, 0);
            ConfigureNumberField(rowsField, 1, 10, 0);

            WireNumberControl(plotWidthField, plotWidthStepper, false);
            WireNumberControl(plotHeightField, plotHeightStepper, false);
            WireNumberControl(fontSizeField, fontSizeStepper, false);
            WireNumberControl(symbolSizeField, symbolSizeStepper, false);
            WireNumberControl(columnsField, columnsStepper, true);
            WireNumberControl(rowsField, rowsStepper, true);
            panelLettersSwitch.Activated += (sender, e) => SchedulePreview();
            groupResultsSwitch.Activated += (sender, e) => SchedulePreview();
            informationBoxesSwitch.Activated += (sender, e) => SchedulePreview();
            strokeWidthControl.Activated += (sender, e) => SchedulePreview();
        }

        void ApplyDefaults()
        {
            plotWidthField.StringValue = defaults.PlotWidthCentimeters.ToString("G6", CultureInfo.CurrentCulture);
            plotHeightField.StringValue = defaults.PlotHeightCentimeters.ToString("G6", CultureInfo.CurrentCulture);
            fontSizeField.StringValue = defaults.FontSize.ToString("G6", CultureInfo.CurrentCulture);
            symbolSizeField.StringValue = defaults.SymbolSize.ToString("G6", CultureInfo.CurrentCulture);
            columnsField.StringValue = defaults.Columns.ToString(CultureInfo.CurrentCulture);
            rowsField.StringValue = defaults.Rows.ToString(CultureInfo.CurrentCulture);
            plotWidthStepper.DoubleValue = defaults.PlotWidthCentimeters;
            plotHeightStepper.DoubleValue = defaults.PlotHeightCentimeters;
            fontSizeStepper.DoubleValue = defaults.FontSize;
            symbolSizeStepper.DoubleValue = defaults.SymbolSize;
            columnsStepper.IntValue = defaults.Columns;
            rowsStepper.IntValue = defaults.Rows;
            strokeWidthControl.SelectedSegment = defaults.StrokeWidth <= 0.5 ? 0 : 1;
            panelLettersSwitch.State = defaults.ShowPanelLetters ? (int)NSCellStateValue.On : (int)NSCellStateValue.Off;
            groupResultsSwitch.State = defaults.GroupResultFigures ? (int)NSCellStateValue.On : (int)NSCellStateValue.Off;
            informationBoxesSwitch.State = defaults.ShowInformationBoxes ? (int)NSCellStateValue.On : (int)NSCellStateValue.Off;
        }

        void OpenSourcePopover(NSView relativeTo)
        {
            sourceFilter.StringValue = "";
            sourceTable.DeselectAll(this);
            RefreshAvailableSources();
            sourcePopover.Show(relativeTo.Bounds, relativeTo, NSRectEdge.MaxYEdge);
            sourceFilter.Window?.MakeFirstResponder(sourceFilter);
            UpdatePickerActions();
        }

        void RefreshAvailableSources()
        {
            var filter = sourceFilter.StringValue?.Trim() ?? "";
            sourceTable.DeselectAll(this);
            availableSources.Clear();
            availableSources.AddRange(DataManager.SourceItems
                .Where(item => !composition.Contains(item))
                .Where(item => string.IsNullOrWhiteSpace(filter) || SourceMatches(item, filter)));
            sourceTable.ReloadData();
            UpdatePickerActions();
        }

        static bool SourceMatches(ITCDataContainer item, string filter)
        {
            var type = item is AnalysisResult ? "result analysis" : "experiment data";
            return (item.Name ?? "").IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
                || type.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        void AddSelectedSources()
        {
            var selected = new HashSet<ITCDataContainer>();
            sourceTable.SelectedRows.EnumerateIndexes((nuint index, ref bool stop) =>
            {
                if (index < (nuint)availableSources.Count) selected.Add(availableSources[(int)index]);
            });

            ITCDataContainer last = null;
            foreach (var item in DataManager.SourceItems.Where(selected.Contains))
            {
                if (composition.Contains(item)) continue;
                composition.Add(item);
                last = item;
            }
            sourcePopover.Close();
            ReloadComposition(last);
            SchedulePreview();
        }

        void RemoveSelected()
        {
            var index = (int)compositionTable.SelectedRow;
            if (index < 0 || index >= composition.Count) return;
            composition.RemoveAt(index);
            ReloadComposition(composition.Count == 0 ? null : composition[Math.Min(index, composition.Count - 1)]);
            SchedulePreview();
        }

        void MoveSelected(int offset)
        {
            var index = (int)compositionTable.SelectedRow;
            var target = index + offset;
            if (index < 0 || index >= composition.Count || target < 0 || target >= composition.Count) return;
            var item = composition[index];
            composition.RemoveAt(index);
            composition.Insert(target, item);
            ReloadComposition(item);
            SchedulePreview();
        }

        void ReorderComposition(int sourceIndex, int dropIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= composition.Count) return;
            dropIndex = Math.Max(0, Math.Min(dropIndex, composition.Count));
            var item = composition[sourceIndex];
            composition.RemoveAt(sourceIndex);
            if (dropIndex > sourceIndex) dropIndex--;
            composition.Insert(Math.Max(0, Math.Min(dropIndex, composition.Count)), item);
            ReloadComposition(item);
            SchedulePreview();
        }

        void ReloadComposition(ITCDataContainer selected)
        {
            compositionTable.ReloadData();
            if (selected != null)
            {
                var index = composition.IndexOf(selected);
                if (index >= 0) compositionTable.SelectRow(index, false);
            }
            UpdateCompositionActions();
        }

        void UpdateCompositionActions()
        {
            var row = (int)compositionTable.SelectedRow;
            var selected = row >= 0 && row < composition.Count;
            removeButton.Enabled = selected;
            moveUpButton.Enabled = selected && row > 0;
            moveDownButton.Enabled = selected && row < composition.Count - 1;
        }

        void UpdatePickerActions()
        {
            pickerAddButton.Enabled = sourceTable.SelectedRowCount > 0;
        }

        static string SourceDescription(ITCDataContainer item)
        {
            if (!(item is AnalysisResult result)) return "Experiment";
            var count = result.Solution?.Solutions?.Count ?? 0;
            return $"Result · {count} experiment{(count == 1 ? "" : "s")}";
        }

        void SchedulePreview()
        {
            previewTimer?.Invalidate();
            previewTimer?.Dispose();
            previewTimer = NSTimer.CreateScheduledTimer(0.08, timer =>
            {
                previewTimer = null;
                RefreshPreview();
            });
        }

        void RefreshPreview()
        {
            try
            {
                if (!TryCurrentCanvasOptions(out var options, out var inputError))
                {
                    ShowInvalidPreview(inputError);
                    return;
                }

                var document = PublicationFigureCanvasBuilder.Build(composition, figureOptions, options);
                var plan = renderer.CreatePlan(document);
                currentPlan = plan;
                exportButton.Enabled = plan.IsValid;

                if (!plan.IsValid)
                {
                    ShowInvalidPreview(plan.ValidationError);
                    return;
                }

                var nextData = renderer.CreatePdfData(plan);
                var nextDocument = new PdfDocument(nextData);
                ReplacePdf(nextData, nextDocument);
                plotSizeLabel.StringValue = $"{plan.LayoutResult.PlotWidthCentimeters:F2} × {plan.LayoutResult.PlotHeightCentimeters:F2} cm";
                figureSizeLabel.StringValue = $"{plan.LayoutResult.FigureWidthCentimeters:F2} × {plan.LayoutResult.FigureHeightCentimeters:F2} cm";
                previewDimensionsLabel.StringValue = $"{plan.LayoutResult.FigureWidthCentimeters:F2} × {plan.LayoutResult.FigureHeightCentimeters:F2} cm";
                statusLabel.StringValue = plan.LayoutResult.PlotWidthCentimeters < 2.5 || plan.LayoutResult.PlotHeightCentimeters < 4
                    ? "The common plot size is small; consider increasing it."
                    : $"{plan.Document.Cells.Count} panel{(plan.Document.Cells.Count == 1 ? "" : "s")}";
                ApplyZoom();
            }
            catch (Exception ex)
            {
                ShowInvalidPreview($"Could not render figure: {ex.Message}");
            }
        }

        void ShowInvalidPreview(string message)
        {
            currentPlan = null;
            exportButton.Enabled = false;
            ClearPdf();
            plotSizeLabel.StringValue = "-";
            figureSizeLabel.StringValue = "-";
            previewDimensionsLabel.StringValue = "Figure dimensions: -";
            statusLabel.StringValue = message ?? "Could not create the supporting figure.";
        }

        bool TryCurrentCanvasOptions(out PublicationFigureCanvasOptions options, out string error)
        {
            options = null;
            error = "";
            double width = 0;
            double height = 0;
            double fontSize = 0;
            double symbolSize = 0;
            int columns = 0;
            int rows = 0;
            if (!TryParseDouble(plotWidthField.StringValue, out width))
                error = "Enter a valid plot width.";
            else if (!TryParseDouble(plotHeightField.StringValue, out height))
                error = "Enter a valid plot height.";
            else if (!TryParseDouble(fontSizeField.StringValue, out fontSize))
                error = "Enter a valid base font size.";
            else if (!TryParseDouble(symbolSizeField.StringValue, out symbolSize))
                error = "Enter a valid data point size.";
            else if (!TryParseInt(columnsField.StringValue, out columns))
                error = "Enter a valid number of columns.";
            else if (!TryParseInt(rowsField.StringValue, out rows))
                error = "Enter a valid number of rows.";

            if (!string.IsNullOrWhiteSpace(error)) return false;

            options = new PublicationFigureCanvasOptions
            {
                PlotWidthCentimeters = width,
                PlotHeightCentimeters = height,
                FontSize = fontSize,
                SymbolSize = symbolSize,
                StrokeWidth = strokeWidthControl.SelectedSegment == 0 ? 0.5 : 1,
                Columns = columns,
                Rows = rows,
                ShowPanelLetters = panelLettersSwitch.State == (int)NSCellStateValue.On,
                GroupResultFigures = groupResultsSwitch.State == (int)NSCellStateValue.On,
                ShowInformationBoxes = informationBoxesSwitch.State == (int)NSCellStateValue.On,
            };
            error = options.Validate();
            return string.IsNullOrWhiteSpace(error);
        }

        void ApplyZoom()
        {
            if (pdfView.Document == null) return;
            pdfView.AutoScales = false;
            switch ((int)zoomPopup.IndexOfSelectedItem)
            {
                case 0:
                    pdfView.LayoutSubtreeIfNeeded();
                    pdfView.ScaleFactor = (nfloat)Math.Max(
                        (double)pdfView.MinScaleFactor,
                        Math.Min(1, (double)pdfView.ScaleFactorForSizeToFit));
                    break;
                case 1: pdfView.ScaleFactor = 0.25f; break;
                case 2: pdfView.ScaleFactor = 0.5f; break;
                case 3: pdfView.ScaleFactor = 0.75f; break;
                default: pdfView.ScaleFactor = 1; break;
            }
        }

        void ExportPdf()
        {
            if (currentPlan == null || !currentPlan.IsValid || currentPdfData == null) return;
            var panel = NSSavePanel.SavePanel;
            panel.Title = "Export Supporting Figure";
            panel.NameFieldStringValue = "supporting-figure.pdf";
            panel.AllowedFileTypes = new[] { "pdf" };
            panel.CanCreateDirectories = true;
            panel.BeginSheet(Window, result =>
            {
                if (result != (int)NSModalResponse.OK || panel.Url == null) return;
                try
                {
                    File.WriteAllBytes(panel.Url.Path, currentPdfData.ToArray());
                    statusLabel.StringValue = $"Supporting figure exported · {currentPlan.LayoutResult.FigureWidthCentimeters:F2} × {currentPlan.LayoutResult.FigureHeightCentimeters:F2} cm";
                    StatusBarManager.SetStatus("Supporting figure PDF exported", 3000);
                }
                catch (Exception ex)
                {
                    AppEventHandler.DisplayHandledException(ex);
                }
            });
        }

        void ReplacePdf(NSData data, PdfDocument document)
        {
            PreserveCurrentPreview();
            pdfView.Document = document;
            var oldDocument = currentPdfDocument;
            var oldData = currentPdfData;
            currentPdfDocument = document;
            currentPdfData = data;
            oldDocument?.Dispose();
            oldData?.Dispose();
            SchedulePreviewTransitionCompletion();
        }

        void PreserveCurrentPreview()
        {
            previewTransitionTimer?.Invalidate();
            previewTransitionTimer?.Dispose();
            previewTransitionTimer = null;

            // When changes arrive rapidly, keep the existing snapshot rather
            // than capturing a PDF view that may still be painting its page.
            if (!previewSnapshotView.Hidden || currentPdfDocument == null) return;
            pdfView.LayoutSubtreeIfNeeded();
            var bounds = pdfView.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            using (var representation = pdfView.BitmapImageRepForCachingDisplayInRect(bounds))
            {
                if (representation == null) return;
                pdfView.CacheDisplay(bounds, representation);
                var image = new NSImage(bounds.Size);
                image.AddRepresentation(representation);
                previewSnapshotImage?.Dispose();
                previewSnapshotImage = image;
                previewSnapshotView.Image = image;
                previewSnapshotView.Hidden = false;
            }
        }

        void SchedulePreviewTransitionCompletion()
        {
            if (previewSnapshotView.Hidden) return;
            previewTransitionTimer = NSTimer.CreateScheduledTimer(0.18, timer =>
            {
                previewTransitionTimer = null;
                if (disposed) return;
                pdfView.DisplayIfNeeded();
                ClearPreviewSnapshot();
            });
        }

        void ClearPreviewSnapshot()
        {
            previewTransitionTimer?.Invalidate();
            previewTransitionTimer?.Dispose();
            previewTransitionTimer = null;
            previewSnapshotView.Hidden = true;
            previewSnapshotView.Image = null;
            previewSnapshotImage?.Dispose();
            previewSnapshotImage = null;
        }

        void ClearPdf()
        {
            ClearPreviewSnapshot();
            pdfView.Document = null;
            currentPdfDocument?.Dispose();
            currentPdfData?.Dispose();
            currentPdfDocument = null;
            currentPdfData = null;
        }

        void CloseSheet()
        {
            sourcePopover.Close();
            if (parentWindow != null) parentWindow.EndSheet(Window, NSModalResponse.Cancel);
            else Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposed) return;
            disposed = true;
            if (disposing)
            {
                previewTimer?.Invalidate();
                previewTimer?.Dispose();
                previewTransitionTimer?.Invalidate();
                previewTransitionTimer?.Dispose();
                previewTransitionTimer = null;
                sourcePopover.Close();
                ClearPdf();
                compositionTable.DataSource = null;
                compositionTable.Delegate = null;
                sourceTable.DataSource = null;
                sourceTable.Delegate = null;
                compositionSource?.Dispose();
                compositionDelegate?.Dispose();
                pickerSource?.Dispose();
                pickerDelegate?.Dispose();
            }
            base.Dispose(disposing);
        }

        static NSWindow CreateWindow()
        {
            var window = new NSWindow(
                new CGRect(0, 0, 1320, 780),
                NSWindowStyle.Titled | NSWindowStyle.Resizable,
                NSBackingStore.Buffered,
                false) { MinSize = new CGSize(1040, 640) };
            window.Center();
            return window;
        }

        static PublicationFigureOptions SnapshotFinalFigureOptions()
        {
            return new PublicationFigureOptions
            {
                PlotWidthCentimeters = FinalFigureGraphView.Width,
                PlotHeightCentimeters = FinalFigureGraphView.Height,
                FontSize = GraphAxis.TickFont.Size,
                EnergyUnit = FinalFigureGraphView.EnergyUnit,
                TimeUnit = FinalFigureGraphView.TimeAxisUnit,
                ShowThermogram = FinalFigureGraphView.ShowDataGraph,
                ShowResiduals = FinalFigureGraphView.ShowResiduals,
                ShowErrorBars = FinalFigureGraphView.ShowErrorBars,
                ShowConfidenceBand = FinalFigureGraphView.DrawConfidence,
                ShowExperimentDetails = FinalFigureGraphView.DrawExpDetails,
                ShowFitParameters = FinalFigureGraphView.DrawFitParameters,
                ShowAxisTitles = FinalFigureGraphView.ShowAxisTitles,
                DrawFitOffsetCorrected = FinalFigureGraphView.DrawFitOffsetCorrected,
                ShowBadData = FinalFigureGraphView.ShowBadData,
                ShowBadDataErrorBars = FinalFigureGraphView.ShowBadDataErrorBars,
                AutoAxesIgnoresBadData = FinalFigureGraphView.AutoAxesIgnoresBadData,
                IncludeResidualGraphGap = FinalFigureGraphView.GapResidualGraph,
                SanitizeTicks = FinalFigureGraphView.SanitizeTicks,
                DrawBaselineCorrected = FinalFigureGraphView.DrawBaselineCorrected,
                ShowBaseline = FinalFigureGraphView.DrawBaseline,
                BaselineStyle = FinalFigureGraphView.BaselineDisplayStyle == BaselineDisplayStyle.Dashed
                    ? PublicationBaselineStyle.Dashed : PublicationBaselineStyle.Solid,
                BaselineLayer = FinalFigureGraphView.BaselineLayerPosition == BaselineLayerPosition.UnderData
                    ? PublicationBaselineLayer.UnderData : PublicationBaselineLayer.OverData,
                BaselineWidth = FinalFigureGraphView.BaselineThickness,
                ShowIntegrationRegions = FinalFigureGraphView.ShowIntegrationRegions,
                IntegrationRegionStyle = (PublicationIntegrationRegionStyle)(int)FinalFigureGraphView.IntegrationRegionDisplayStyle,
                ShowZeroLine = FinalFigureGraphView.DrawZeroLine,
                DataXTickCount = FinalFigureGraphView.DataXTickCount,
                DataYTickCount = FinalFigureGraphView.DataYTickCount,
                FitXTickCount = FinalFigureGraphView.FitXTickCount,
                FitYTickCount = FinalFigureGraphView.FitYTickCount,
                InformationBoxPlacement = (PublicationInfoBoxPlacement)(int)FinalFigureGraphView.InformationBoxPosition,
                SymbolShape = FinalFigureGraphView.SymbolShape == 1 ? PublicationSymbolShape.Circle : PublicationSymbolShape.Square,
                SymbolSize = FinalFigureGraphView.SymbolSize,
                FitLineWidth = 2,
                FitLineSmoothness = (LineSmoothness)(int)FinalFigureGraphView.FitLineSmoothness,
                PowerAxisTitle = FinalFigureGraphView.PowerAxisTitle,
                TimeAxisTitle = FinalFigureGraphView.TimeAxisTitle,
                EnthalpyAxisTitle = FinalFigureGraphView.EnthalpyAxisTitle,
                XAxisTitle = FinalFigureGraphView.MolarRatioAxisTitle,
                DataXAxisMinimum = FinalFigureGraphView.DataXAxisMin,
                DataXAxisMaximum = FinalFigureGraphView.DataXAxisMax,
                DataYAxisMinimum = FinalFigureGraphView.DataYAxisMin,
                DataYAxisMaximum = FinalFigureGraphView.DataYAxisMax,
                FitXAxisMinimum = FinalFigureGraphView.FitXAxisMin,
                FitXAxisMaximum = FinalFigureGraphView.FitXAxisMax,
                FitYAxisMinimum = FinalFigureGraphView.FitYAxisMin,
                FitYAxisMaximum = FinalFigureGraphView.FitYAxisMax,
                DisplayParameters = FinalFigureGraphView.VisibleFinalFigureDisplayParameters,
                AttributeOptions = AppSettings.DisplayAttributeOptions,
                TextUncertaintyStyle = FinalFigureGraphView.TextUncertaintyStyle,
            };
        }

        internal static SupportingFigureCanvasWindowController CreateForCurrentSelection()
        {
            ITCDataContainer selected = null;
            var index = DataManager.SelectedContentIndex;
            if (index >= 0 && index < DataManager.SourceItems.Count) selected = DataManager.SourceItems[index];
            return new SupportingFigureCanvasWindowController(SnapshotFinalFigureOptions(), selected);
        }

        void ConfigureNumberField(NSTextField field, double minimum, double maximum, int fractionDigits)
        {
            field.Formatter = new NSNumberFormatter
            {
                NumberStyle = NSNumberFormatterStyle.Decimal,
                Minimum = NSNumber.FromDouble(minimum),
                Maximum = NSNumber.FromDouble(maximum),
                AllowsFloats = fractionDigits > 0,
                MinimumFractionDigits = 0,
                MaximumFractionDigits = fractionDigits,
            };
        }

        void WireNumberControl(NSTextField field, NSStepper stepper, bool integer)
        {
            field.Changed += (sender, e) =>
            {
                if (TryParseDouble(field.StringValue, out var value)) stepper.DoubleValue = value;
                SchedulePreview();
            };
            stepper.Activated += (sender, e) =>
            {
                field.StringValue = integer
                    ? stepper.IntValue.ToString(CultureInfo.CurrentCulture)
                    : stepper.DoubleValue.ToString("0.#", CultureInfo.CurrentCulture);
                SchedulePreview();
            };
        }

        static bool TryParseDouble(string text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        static bool TryParseInt(string text, out int value)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
                || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        static SupportingFigureTableView CompositionTable()
        {
            var table = new SupportingFigureTableView
            {
                HeaderView = null,
                RowHeight = 48,
                AllowsMultipleSelection = false,
                AllowsEmptySelection = true,
                UsesAlternatingRowBackgroundColors = false,
                SelectionHighlightStyle = NSTableViewSelectionHighlightStyle.SourceList,
                Style = NSTableViewStyle.SourceList,
                BackgroundColor = NSColor.Clear,
            };
            // Source-list rows are inset by AppKit. Keep the column inside the
            // fixed 300 pt sidebar so trailing metadata is never clipped.
            table.AddColumn(new NSTableColumn("main") { Width = 268 });
            return table;
        }

        static NSTableView Table(bool multiple)
        {
            var table = new NSTableView
            {
                HeaderView = null,
                RowHeight = 48,
                AllowsMultipleSelection = multiple,
                AllowsEmptySelection = true,
                UsesAlternatingRowBackgroundColors = false,
                SelectionHighlightStyle = NSTableViewSelectionHighlightStyle.SourceList,
                Style = multiple ? NSTableViewStyle.Inset : NSTableViewStyle.SourceList,
                BackgroundColor = NSColor.Clear,
            };
            table.AddColumn(new NSTableColumn("main") { Width = multiple ? 360 : 298 });
            return table;
        }

        static NSScrollView Scroll(NSTableView table) => new NSScrollView
        {
            DocumentView = table,
            HasVerticalScroller = true,
            HasHorizontalScroller = false,
            AutohidesScrollers = true,
            BorderType = NSBorderType.NoBorder,
            DrawsBackground = false,
        };

        static NSTextField Field() => new NSTextField
        {
            Alignment = NSTextAlignment.Right,
            ControlSize = NSControlSize.Regular,
            Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
            BezelStyle = NSTextFieldBezelStyle.Rounded,
        };

        static NSTextField Label(string text) => new NSTextField
        {
            StringValue = text ?? "",
            Bezeled = false,
            Bordered = false,
            DrawsBackground = false,
            Editable = false,
            Selectable = false,
            Alignment = NSTextAlignment.Left,
            Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
        };

        static NSTextField Header(string text)
        {
            var label = Label(text);
            label.Alignment = NSTextAlignment.Left;
            label.Font = NSFont.BoldSystemFontOfSize(14);
            return label;
        }

        static NSTextField ValueLabel(string text)
        {
            var label = Label(text);
            label.Alignment = NSTextAlignment.Right;
            label.TextColor = NSColor.SecondaryLabel;
            return label;
        }

        static NSButton Button(string title) => new NSButton
        {
            Title = title,
            BezelStyle = NSBezelStyle.Rounded,
            ControlSize = NSControlSize.Regular,
            Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
        };

        static NSSegmentedControl StrokeControl()
        {
            var control = new NSSegmentedControl
            {
                SegmentCount = 2,
                SegmentStyle = NSSegmentStyle.Rounded,
                ControlSize = NSControlSize.Regular,
                SelectedSegment = 1,
                ToolTip = "Choose 0.5 pt lines with short ticks or 1 pt lines with standard ticks",
            };
            control.SetLabel("0.5 · Short", 0);
            control.SetLabel("1 · Standard", 1);
            control.SetWidth(76, 0);
            control.SetWidth(76, 1);
            SetAccessibilityLabel(control, "Line width and tick length");
            return control;
        }

        static NSButton SymbolButton(string symbol, string accessibilityLabel)
        {
            var image = NSImage.GetSystemSymbol(symbol, accessibilityLabel);
            if (image == null)
            {
                var fallback = symbol == "plus" ? NSImageName.AddTemplate
                    : symbol == "minus" ? NSImageName.RemoveTemplate
                    : symbol == "chevron.up" ? NSImageName.TouchBarGoUpTemplate
                    : NSImageName.TouchBarGoDownTemplate;
                image = NSImage.ImageNamed(fallback);
            }
            var button = new NSButton
            {
                Title = "",
                Image = image,
                ImagePosition = NSCellImagePosition.ImageOnly,
                BezelStyle = NSBezelStyle.TexturedRounded,
                ControlSize = NSControlSize.Regular,
                ToolTip = accessibilityLabel,
            };
            SetAccessibilityLabel(button, accessibilityLabel);
            button.WidthAnchor.ConstraintEqualToConstant(32).Active = true;
            button.HeightAnchor.ConstraintEqualToConstant(32).Active = true;
            return button;
        }

        static NSSwitch Toggle(string accessibilityLabel)
        {
            var control = new NSSwitch
            {
                ControlSize = NSControlSize.Mini,
                ToolTip = accessibilityLabel,
            };
            SetAccessibilityLabel(control, accessibilityLabel);
            return control;
        }

        static void SetAccessibilityLabel(NSObject control, string label)
            => control.SetValueForKey(new NSString(label ?? ""), new NSString("accessibilityLabel"));

        static NSStepper Stepper(double minimum, double maximum, double increment) => new NSStepper
        {
            MinValue = minimum,
            MaxValue = maximum,
            Increment = increment,
            Autorepeat = true,
            ValueWraps = false,
            ControlSize = NSControlSize.Regular,
        };

        static NSBox Separator() => new NSBox
        {
            BoxType = NSBoxType.NSBoxSeparator,
            TitlePosition = NSTitlePosition.NoTitle,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

        static NSStackView HorizontalStack(params NSView[] views)
        {
            var stack = new NSStackView(new CGRect(0, 0, 100, 28))
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.CenterY,
                Spacing = 6,
            };
            foreach (var view in views) stack.AddArrangedSubview(view);
            return stack;
        }

        static NSView NumericEditor(NSTextField field, NSStepper stepper)
        {
            var stack = HorizontalStack(field, stepper);
            stack.Distribution = NSStackViewDistribution.Fill;
            field.HorizontalContentSizeConstraintActive = false;
            field.HeightAnchor.ConstraintEqualToConstant(26).Active = true;
            return stack;
        }

        static NSView TrailingControl(NSView control)
        {
            var container = new NSView(new CGRect(0, 0, 120, 22));
            control.TranslatesAutoresizingMaskIntoConstraints = false;
            container.AddSubview(control);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                control.TrailingAnchor.ConstraintEqualToAnchor(container.TrailingAnchor),
                control.CenterYAnchor.ConstraintEqualToAnchor(container.CenterYAnchor),
            });
            return container;
        }

        static NSView[] InspectorRow(string title, NSView value)
            => new[] { Label(title), value };

        static void AddFullWidth(NSStackView stack, NSView view)
        {
            stack.AddArrangedSubview(view);
            view.WidthAnchor.ConstraintEqualToAnchor(stack.WidthAnchor).Active = true;
        }

        static NSView InspectorSection(string title, NSView[][] rows)
        {
            var stack = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.Width,
                Spacing = 8,
            };
            var separator = Separator();
            separator.HeightAnchor.ConstraintEqualToConstant(1).Active = true;
            var header = Header(title);
            header.Font = NSFont.BoldSystemFontOfSize(12);
            var grid = NSGridView.Create(rows);
            grid.ColumnSpacing = 10;
            grid.RowSpacing = 8;
            grid.RowAlignment = NSGridRowAlignment.FirstBaseline;
            grid.X = NSGridCellPlacement.Fill;
            grid.Y = NSGridCellPlacement.Center;
            grid.GetColumn(0).Width = 112;
            grid.GetColumn(0).X = NSGridCellPlacement.Leading;
            grid.GetColumn(1).X = NSGridCellPlacement.Fill;
            AddFullWidth(stack, separator);
            AddFullWidth(stack, header);
            AddFullWidth(stack, grid);
            return stack;
        }
    }

    sealed class SupportingFigureTableView : NSTableView
    {
        public Action DeleteRequested { get; set; }
        public Action<int> MoveRequested { get; set; }

        public override void KeyDown(NSEvent theEvent)
        {
            if (theEvent != null && (theEvent.KeyCode == 51 || theEvent.KeyCode == 117))
            {
                DeleteRequested?.Invoke();
                return;
            }

            if (theEvent != null && (theEvent.ModifierFlags & NSEventModifierMask.CommandKeyMask) != 0)
            {
                if ((NSKey)theEvent.KeyCode == NSKey.UpArrow)
                {
                    MoveRequested?.Invoke(-1);
                    return;
                }
                if ((NSKey)theEvent.KeyCode == NSKey.DownArrow)
                {
                    MoveRequested?.Invoke(1);
                    return;
                }
            }

            base.KeyDown(theEvent);
        }
    }

    sealed class SupportingFigureFlippedView : NSView
    {
        public SupportingFigureFlippedView(CGRect frame) : base(frame) { }
        public override bool IsFlipped => true;
    }

    sealed class SupportingFigureFlippedStackView : NSStackView
    {
        public SupportingFigureFlippedStackView(CGRect frame) : base(frame) { }
        public override bool IsFlipped => true;
    }

    sealed class SupportingFigurePreviewSnapshotView : NSImageView
    {
        public override NSView HitTest(CGPoint point) => null;
    }

    sealed class SupportingFigureBackgroundView : NSView
    {
        readonly NSColor color;

        public SupportingFigureBackgroundView(CGRect frame, NSColor color) : base(frame)
            => this.color = color ?? NSColor.WindowBackground;

        public override void DrawRect(CGRect dirtyRect)
        {
            color.SetFill();
            NSBezierPath.FillRect(dirtyRect);
            base.DrawRect(dirtyRect);
        }
    }

    sealed class SupportingFigureTableDataSource : NSTableViewDataSource
    {
        readonly IList<ITCDataContainer> items;
        readonly string pasteboardType;
        readonly Action<int, int> reorder;
        int draggedRow = -1;

        public SupportingFigureTableDataSource(
            IList<ITCDataContainer> items,
            string pasteboardType = null,
            Action<int, int> reorder = null)
        {
            this.items = items;
            this.pasteboardType = pasteboardType;
            this.reorder = reorder;
        }

        public ITCDataContainer Item(nint row) => row >= 0 && row < items.Count ? items[(int)row] : null;
        public override nint GetRowCount(NSTableView tableView) => items.Count;

        public override bool WriteRows(NSTableView tableView, NSIndexSet rowIndexes, NSPasteboard pboard)
        {
            if (reorder == null || rowIndexes == null || rowIndexes.Count != 1) return false;
            draggedRow = (int)rowIndexes.FirstIndex;
            pboard.DeclareTypes(new[] { pasteboardType }, tableView);
            return pboard.SetStringForType(draggedRow.ToString(CultureInfo.InvariantCulture), pasteboardType);
        }

        public override NSDragOperation ValidateDrop(
            NSTableView tableView,
            NSDraggingInfo info,
            nint row,
            NSTableViewDropOperation dropOperation)
        {
            if (reorder == null || draggedRow < 0 || dropOperation != NSTableViewDropOperation.Above)
                return NSDragOperation.None;
            tableView.SetDropRowDropOperation(row, NSTableViewDropOperation.Above);
            return NSDragOperation.Move;
        }

        public override bool AcceptDrop(
            NSTableView tableView,
            NSDraggingInfo info,
            nint row,
            NSTableViewDropOperation dropOperation)
        {
            if (reorder == null || draggedRow < 0 || dropOperation != NSTableViewDropOperation.Above)
                return false;
            var source = draggedRow;
            draggedRow = -1;
            reorder(source, (int)row);
            return true;
        }
    }

    sealed class SupportingFigureTableDelegate : NSTableViewDelegate
    {
        readonly SupportingFigureTableDataSource source;
        readonly Func<ITCDataContainer, string> title;
        readonly Func<ITCDataContainer, string> detail;
        readonly Action selectionChanged;

        public SupportingFigureTableDelegate(
            SupportingFigureTableDataSource source,
            Func<ITCDataContainer, string> title,
            Func<ITCDataContainer, string> detail,
            Action selectionChanged = null)
        {
            this.source = source;
            this.title = title;
            this.detail = detail;
            this.selectionChanged = selectionChanged;
        }

        public override void SelectionDidChange(NSNotification notification) => selectionChanged?.Invoke();

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var item = source.Item(row);
            if (item == null) return new NSView();

            var cell = new NSTableCellView
            {
                Frame = new CGRect(0, 0, tableColumn.Width, tableView.RowHeight),
                AutoresizingMask = NSViewResizingMask.WidthSizable,
            };
            var titleLabel = RowLabel(title(item), NSFont.SystemFontOfSize(13));
            var detailLabel = RowLabel(detail(item), NSFont.SystemFontOfSize(11));
            detailLabel.TextColor = NSColor.SecondaryLabel;
            cell.AddSubview(titleLabel);
            cell.AddSubview(detailLabel);

            titleLabel.TranslatesAutoresizingMaskIntoConstraints = false;
            detailLabel.TranslatesAutoresizingMaskIntoConstraints = false;
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                detailLabel.LeadingAnchor.ConstraintEqualToAnchor(cell.LeadingAnchor, 9),
                detailLabel.TrailingAnchor.ConstraintEqualToAnchor(cell.TrailingAnchor, -9),
                detailLabel.TopAnchor.ConstraintEqualToAnchor(cell.TopAnchor, 5),
                titleLabel.LeadingAnchor.ConstraintEqualToAnchor(cell.LeadingAnchor, 9),
                titleLabel.TrailingAnchor.ConstraintEqualToAnchor(cell.TrailingAnchor, -9),
                titleLabel.TopAnchor.ConstraintEqualToAnchor(detailLabel.BottomAnchor),
                titleLabel.BottomAnchor.ConstraintLessThanOrEqualToAnchor(cell.BottomAnchor, -4),
            });
            cell.TextField = titleLabel;
            return cell;
        }

        static NSTextField RowLabel(string text, NSFont font)
        {
            return new NSTextField
            {
                StringValue = text ?? "",
                Bezeled = false,
                Bordered = false,
                DrawsBackground = false,
                Editable = false,
                Selectable = false,
                LineBreakMode = NSLineBreakMode.TruncatingTail,
                Font = font,
                Alignment = NSTextAlignment.Left,
            };
        }
    }
}
