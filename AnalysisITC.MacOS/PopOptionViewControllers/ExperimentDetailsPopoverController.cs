using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using Buffer = AnalysisITC.Core.Data.Buffer;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Processing;
using AnalysisITC.UI.MacOS.CustomViews;

namespace AnalysisITC
{
    public partial class ExperimentDetailsPopoverController : NSViewController
    {
        const float Width = 720, MaximumHeight = 600;
        public static event EventHandler UpdateTable;
        public static ExperimentData Data { get; set; }

        static List<ExperimentAttribute> tmpoptions = new List<ExperimentAttribute>();
        public static IEnumerable<AttributeKey> AvailableAttributes => tmpoptions.Select(x => x.Key);
        public static IEnumerable<AttributeKey> AllAddedOptions => Data.Attributes.Select(x => x.Key).Concat(tmpoptions.Select(x => x.Key));
        NSDatePicker datePicker, timePicker;
        DateTime originalDate;
        NSStackView formStack;
        NSView footerView, headerRule;
        NSScrollView commentsScroll, detailsScroll, attributesScroll;
        NSTextField headingView;
        NSTextField filenameLabel;
        NSTextField experimentSummaryLabel;
        NSSegmentedControl pageControl;
        NSView detailsPage, attributesPage;
        NSTextField emptyAttributesLabel;
        readonly List<ExperimentAttributeView> attributeViews = new List<ExperimentAttributeView>();
        bool sizeUpdateQueued;
        bool preparingInitialLayout;
        NSButton AddAttributeButton;
        NSStackView AttributeStackView;
        NSTextField CellConcentrationErrorField, CellConcentrationField, CellVolumeField;
        NSTextView CommentTextField;
        NSTextField ExperimentNameField, SyringeConcentrationErrorField, SyringeConcentrationField, TemperatureField;
        public ExperimentDetailsPopoverController(): base()
        {
        }

        public ExperimentDetailsPopoverController(IntPtr handle): base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            // Resolve the content and its size before AppKit starts presenting
            // the sheet. Loading in ViewDidAppear causes a visible second resize.
            preparingInitialLayout = true;
            View.SetFrameSize(new CGSize(Width, MaximumHeight));
            BuildView();
            Setup();
            preparingInitialLayout = false;
            UpdateContentSize(resizeWindow: false);
        }

        NSTextField Field(string placeholder) => new NSTextField{PlaceholderString = placeholder, Bordered = false, Bezeled = true, DrawsBackground = true, Font = NSFont.SystemFontOfSize(13), BezelStyle = NSTextFieldBezelStyle.Rounded, TranslatesAutoresizingMaskIntoConstraints = false, AccessibilityLabel = placeholder};
        void Fill(NSStackView parent, NSView child)
        {
            child.TranslatesAutoresizingMaskIntoConstraints = false;
            parent.AddConstraint(NSLayoutConstraint.Create(child, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, parent, NSLayoutAttribute.Leading, 1, 0));
            parent.AddConstraint(NSLayoutConstraint.Create(child, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, parent, NSLayoutAttribute.Trailing, 1, 0));
        }

        NSView SectionHeading(string title)
        {
            var label = NSTextField.CreateLabel(title);
            label.Font = NSFont.BoldSystemFontOfSize(13);
            label.TextColor = NSColor.Label;
            label.TranslatesAutoresizingMaskIntoConstraints = false;
            label.Cell.Wraps = false;
            label.Cell.UsesSingleLineMode = true;
            return label;
        }

        NSView Section(string title, NSView body)
        {
            var s = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 10, TranslatesAutoresizingMaskIntoConstraints = false};
            var l = SectionHeading(title);
            s.AddArrangedSubview(l);
            s.AddArrangedSubview(body);
            Fill(s, l);
            Fill(s, body);
            return Padded(s, 12, 12, true);
        }

        NSView Divider() => new NSBox
        {
            BoxType = NSBoxType.NSBoxSeparator,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        NSView Padded(NSView body, float horizontal = 16, float vertical = 16, bool outlined = false, bool tinted = false)
        {
            var section = outlined ? new PanelView { Tinted = tinted } : new NSView();
            section.TranslatesAutoresizingMaskIntoConstraints = false;
            body.TranslatesAutoresizingMaskIntoConstraints = false;
            section.AddSubview(body);
            section.AddConstraints(new[]
            {
                NSLayoutConstraint.Create(body, NSLayoutAttribute.Top, NSLayoutRelation.Equal, section, NSLayoutAttribute.Top, 1, vertical),
                NSLayoutConstraint.Create(body, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, section, NSLayoutAttribute.Bottom, 1, -vertical),
                NSLayoutConstraint.Create(body, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, section, NSLayoutAttribute.Leading, 1, horizontal),
                NSLayoutConstraint.Create(body, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, section, NSLayoutAttribute.Trailing, 1, -horizontal)
            });
            return section;
        }

        void BuildView()
        {
            foreach (var v in View.Subviews.ToArray())
                v.RemoveFromSuperview();
            var root = new NSView{TranslatesAutoresizingMaskIntoConstraints = false};
            View.AddSubview(root);
            View.AddConstraints(new[]{NSLayoutConstraint.Create(root, NSLayoutAttribute.Top, NSLayoutRelation.Equal, View, NSLayoutAttribute.Top, 1, 0), NSLayoutConstraint.Create(root, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, View, NSLayoutAttribute.Bottom, 1, 0), NSLayoutConstraint.Create(root, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, View, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(root, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, View, NSLayoutAttribute.Trailing, 1, 0)});
            var title = headingView = NSTextField.CreateLabel("Experiment Details");
            title.TranslatesAutoresizingMaskIntoConstraints = false;
            title.Alignment = NSTextAlignment.Left;
            title.Font = NSFont.BoldSystemFontOfSize(NSFont.SystemFontSize);
            title.AccessibilityLabel = "Experiment Details";
            var filename = filenameLabel = NSTextField.CreateLabel("");
            filename.TextColor = NSColor.SecondaryLabel;
            filename.LineBreakMode = NSLineBreakMode.TruncatingTail;
            filename.ToolTip = Data?.FileName;
            filename.Alignment = NSTextAlignment.Left;
            filename.TranslatesAutoresizingMaskIntoConstraints = false;
            experimentSummaryLabel = NSTextField.CreateLabel("");
            experimentSummaryLabel.Font = NSFont.SystemFontOfSize(13);
            experimentSummaryLabel.TextColor = NSColor.Label;
            experimentSummaryLabel.LineBreakMode = NSLineBreakMode.TruncatingTail;
            var header = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 2, TranslatesAutoresizingMaskIntoConstraints = false };
            header.AddArrangedSubview(title);
            header.AddArrangedSubview(experimentSummaryLabel);
            header.AddArrangedSubview(filename);
            Fill(header, title); Fill(header, experimentSummaryLabel); Fill(header, filename);

            formStack = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 12, TranslatesAutoresizingMaskIntoConstraints = false};
            ExperimentNameField = Field("Experiment name");
            var identity = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false};
            var nameRow = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.CenterY, Spacing = 12 };
            nameRow.AddArrangedSubview(NSTextField.CreateLabel("Name"));
            nameRow.AddArrangedSubview(ExperimentNameField);
            identity.AddArrangedSubview(nameRow);
            identity.AddArrangedSubview(DateRows());
            foreach (var child in identity.Views)
                Fill(identity, child);
            formStack.AddArrangedSubview(Section("Experiment", identity));
            var paired = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Distribution = NSStackViewDistribution.FillEqually, Alignment = NSLayoutAttribute.Top, Spacing = 12, TranslatesAutoresizingMaskIntoConstraints = false };
            paired.AddArrangedSubview(Section("Conditions", Conditions()));
            paired.AddArrangedSubview(Section("Concentrations", Concentrations()));
            foreach (var child in paired.Views) child.TranslatesAutoresizingMaskIntoConstraints = false;
            formStack.AddArrangedSubview(paired);
            formStack.AddArrangedSubview(Section("Comments", Comments()));
            foreach (var c in formStack.Views) Fill(formStack, c);

            detailsPage = MakePage(Padded(formStack), out detailsScroll);

            AttributeStackView = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 4, TranslatesAutoresizingMaskIntoConstraints = false};
            AddAttributeButton = new NSButton{Title = "Add Attribute", ControlSize = NSControlSize.Regular, Font = NSFont.SystemFontOfSize(13), BezelStyle = NSBezelStyle.Inline, Image = NSImage.GetSystemSymbol("plus", null), ImagePosition = NSCellImagePosition.ImageLeading, TranslatesAutoresizingMaskIntoConstraints = false};
            AddAttributeButton.Activated += (s, e) => AddAttribute(AddAttributeButton);
            var ah = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Distribution = NSStackViewDistribution.Fill, TranslatesAutoresizingMaskIntoConstraints = false};
            var al = SectionHeading("Attributes");
            ah.AddArrangedSubview(al);
            ah.AddArrangedSubview(new NSView{TranslatesAutoresizingMaskIntoConstraints = false});
            ah.AddArrangedSubview(AddAttributeButton);
            var explanation = NSTextField.CreateLabel("Optional metadata used to describe the experiment and support analysis.");
            explanation.TextColor = NSColor.SecondaryLabel;
            explanation.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            var empty = emptyAttributesLabel = NSTextField.CreateLabel("No attributes added");
            empty.TextColor = NSColor.SecondaryLabel;
            empty.Tag = 9021;
            var attributes = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false};
            attributes.AddArrangedSubview(ah);
            attributes.AddArrangedSubview(explanation);
            attributes.AddArrangedSubview(empty);
            attributes.AddArrangedSubview(AttributeStackView);
            foreach (var c in attributes.Views) Fill(attributes, c);
            attributesPage = MakePage(Padded(Padded(attributes, 12, 12, true)), out attributesScroll);
            var footer = footerView = new NSView{TranslatesAutoresizingMaskIntoConstraints = false};
            var separator = new NSBox{BoxType = NSBoxType.NSBoxSeparator, TranslatesAutoresizingMaskIntoConstraints = false};
            var buttons = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Distribution = NSStackViewDistribution.Fill, Spacing = 10, TranslatesAutoresizingMaskIntoConstraints = false};
            var spacer = new NSView{TranslatesAutoresizingMaskIntoConstraints = false};
            var cancel = new NSButton{Title = "Cancel", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\u001b", TranslatesAutoresizingMaskIntoConstraints = false};
            var apply = new NSButton{Title = "Apply", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\r", TranslatesAutoresizingMaskIntoConstraints = false};
            cancel.Activated += (s, e) => Cancel(cancel);
            apply.Activated += (s, e) => Apply(apply);
            buttons.AddArrangedSubview(spacer);
            buttons.AddArrangedSubview(cancel);
            buttons.AddArrangedSubview(apply);
            cancel.AddConstraint(NSLayoutConstraint.Create(cancel, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 80));
            apply.AddConstraint(NSLayoutConstraint.Create(apply, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 80));
            footer.AddSubview(separator);
            footer.AddSubview(buttons);
            footer.AddConstraints(new[]{NSLayoutConstraint.Create(separator, NSLayoutAttribute.Top, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Top, 1, 0), NSLayoutConstraint.Create(separator, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(separator, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(buttons, NSLayoutAttribute.Top, NSLayoutRelation.Equal, separator, NSLayoutAttribute.Bottom, 1, 14), NSLayoutConstraint.Create(buttons, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Leading, 1, 20), NSLayoutConstraint.Create(buttons, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Trailing, 1, -20), NSLayoutConstraint.Create(buttons, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Bottom, 1, 0)});
            var headerDivider = headerRule = Divider();
            root.AddSubview(headerDivider);
            root.AddSubview(header);
            root.AddConstraints(new[] {
                NSLayoutConstraint.Create(headerDivider, NSLayoutAttribute.Top, NSLayoutRelation.Equal, header, NSLayoutAttribute.Bottom, 1, 12),
                NSLayoutConstraint.Create(headerDivider, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 0),
                NSLayoutConstraint.Create(headerDivider, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, 0)
            });
            pageControl = new WorkspaceTabControl(new CGRect(0, 0, 248, 32)) { SegmentCount = 2, ControlSize = NSControlSize.Regular, TranslatesAutoresizingMaskIntoConstraints = false };
            pageControl.SetLabel("Details", 0); pageControl.SetLabel("Attributes", 1); pageControl.SelectSegment(0);
            pageControl.Activated += (s, e) => ShowPage(pageControl.SelectedSegment == 1);
            pageControl.Font = NSFont.SystemFontOfSize(14);
            pageControl.SetWidth(120, 0);
            pageControl.SetWidth(120, 1);
            pageControl.AddConstraint(NSLayoutConstraint.Create(pageControl, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 248));
            var tabBar = new TabBarView { TranslatesAutoresizingMaskIntoConstraints = false };
            root.AddSubview(tabBar);
            tabBar.AddSubview(pageControl);
            root.AddConstraints(new[] {
                NSLayoutConstraint.Create(tabBar, NSLayoutAttribute.Top, NSLayoutRelation.Equal, headerDivider, NSLayoutAttribute.Bottom, 1, 0),
                NSLayoutConstraint.Create(tabBar, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 0),
                NSLayoutConstraint.Create(tabBar, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, 0),
                NSLayoutConstraint.Create(tabBar, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 44)
            });
            root.AddSubview(detailsPage); root.AddSubview(attributesPage);
            root.AddSubview(footer);
            root.AddConstraints(new[]{NSLayoutConstraint.Create(header, NSLayoutAttribute.Top, NSLayoutRelation.Equal, root, NSLayoutAttribute.Top, 1, 12), NSLayoutConstraint.Create(header, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 16), NSLayoutConstraint.Create(header, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, -16), NSLayoutConstraint.Create(pageControl, NSLayoutAttribute.CenterY, NSLayoutRelation.Equal, tabBar, NSLayoutAttribute.CenterY, 1, 0), NSLayoutConstraint.Create(pageControl, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, tabBar, NSLayoutAttribute.Leading, 1, 16), NSLayoutConstraint.Create(detailsPage, NSLayoutAttribute.Top, NSLayoutRelation.Equal, tabBar, NSLayoutAttribute.Bottom, 1, 0), NSLayoutConstraint.Create(detailsPage, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(detailsPage, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(detailsPage, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Top, 1, 0), NSLayoutConstraint.Create(attributesPage, NSLayoutAttribute.Top, NSLayoutRelation.Equal, detailsPage, NSLayoutAttribute.Top, 1, 0), NSLayoutConstraint.Create(attributesPage, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(attributesPage, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(attributesPage, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Top, 1, 0), NSLayoutConstraint.Create(footer, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(footer, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(footer, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, root, NSLayoutAttribute.Bottom, 1, -12)});
            ShowPage(false);
        }

        NSView MakePage(NSView content, out NSScrollView scroll)
        {
            var document = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
            document.AddSubview(content);
            document.AddConstraints(new[] { NSLayoutConstraint.Create(content, NSLayoutAttribute.Top, NSLayoutRelation.Equal, document, NSLayoutAttribute.Top, 1, 0), NSLayoutConstraint.Create(content, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, document, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(content, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, document, NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(content, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, document, NSLayoutAttribute.Bottom, 1, 0) });
            scroll = new NSScrollView { AutohidesScrollers = true, HasVerticalScroller = true, HasHorizontalScroller = false, BorderType = NSBorderType.NoBorder, DocumentView = document, DrawsBackground = true, BackgroundColor = NSColor.WindowBackground, TranslatesAutoresizingMaskIntoConstraints = false };
            scroll.ContentView.AddConstraint(NSLayoutConstraint.Create(document, NSLayoutAttribute.Width, NSLayoutRelation.Equal, scroll.ContentView, NSLayoutAttribute.Width, 1, 0));
            return scroll;
        }

        void ShowPage(bool attributes)
        {
            pageControl.SelectSegment(attributes ? 1 : 0);
            pageControl.NeedsDisplay = true;
            detailsPage.Hidden = attributes; attributesPage.Hidden = !attributes;
        }

        NSView DateRows()
        {
            var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.CenterY, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false };
            var zone = NSTimeZone.FromAbbreviation("UTC");
            datePicker = new NSDatePicker { DatePickerStyle = NSDatePickerStyle.TextFieldAndStepper, Bezeled = true, DrawsBackground = true, DatePickerElements = NSDatePickerElementFlags.YearMonthDateDay, TimeZone = zone, TranslatesAutoresizingMaskIntoConstraints = false, AccessibilityLabel = "Experiment date" };
            timePicker = new NSDatePicker { DatePickerStyle = NSDatePickerStyle.TextFieldAndStepper, Bezeled = true, DrawsBackground = true, DatePickerElements = NSDatePickerElementFlags.HourMinuteSecond, TimeZone = zone, TranslatesAutoresizingMaskIntoConstraints = false, AccessibilityLabel = "Experiment time" };
            var spacer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            spacer.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
            stack.AddArrangedSubview(spacer);
            stack.AddArrangedSubview(NSTextField.CreateLabel("Date"));
            stack.AddArrangedSubview(datePicker);
            stack.AddArrangedSubview(NSTextField.CreateLabel("Time"));
            stack.AddArrangedSubview(timePicker);
            datePicker.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            timePicker.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            return stack;
        }

        NSView Conditions()
        {
            var s = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 4, TranslatesAutoresizingMaskIntoConstraints = false};
            s.AddArrangedSubview(Row("Temperature (°C)", out var te, out _, false));
            TemperatureField = te;
            s.AddArrangedSubview(Row("Cell volume (µl)", out var vo, out _, false));
            CellVolumeField = vo;
            foreach (var f in new[]{te, vo})
            {
                var formatter = new NSNumberFormatter{NumberStyle = NSNumberFormatterStyle.Decimal, UsesGroupingSeparator = true, MaximumFractionDigits = 3, MinimumIntegerDigits = 1};
                f.Formatter = formatter;
            }

            foreach (var child in s.Views)
                Fill(s, child);
            return s;
        }

        NSView Concentrations()
        {
            var s = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 4, TranslatesAutoresizingMaskIntoConstraints = false };
            s.AddArrangedSubview(Row("Cell (µM)", out var c, out var ce, true));
            CellConcentrationField = c; CellConcentrationErrorField = ce;
            s.AddArrangedSubview(Row("Syringe (µM)", out var sy, out var sye, true));
            SyringeConcentrationField = sy; SyringeConcentrationErrorField = sye;
            foreach (var f in new[] { c, ce, sy, sye })
            {
                var formatter = new NSNumberFormatter { NumberStyle = NSNumberFormatterStyle.Decimal, UsesGroupingSeparator = true, MaximumFractionDigits = 3, MinimumIntegerDigits = 1, Minimum = new NSNumber(0), Maximum = new NSNumber(10000), Lenient = f == c || f == ce };
                f.Formatter = formatter;
            }
            foreach (var child in s.Views) Fill(s, child);
            return s;
        }

        NSView Row(string label, out NSTextField value, out NSTextField error, bool hasError)
        {
            var r = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.CenterY, Spacing = 10, TranslatesAutoresizingMaskIntoConstraints = false};
            var l = NSTextField.CreateLabel(label);
            l.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
            l.SetContentCompressionResistancePriority(750, NSLayoutConstraintOrientation.Horizontal);
            r.AddArrangedSubview(l);
            value = Field(label);
            value.Alignment = NSTextAlignment.Right;
            value.AddConstraint(NSLayoutConstraint.Create(value, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, hasError ? 78 : 100));
            r.AddArrangedSubview(value);
            error = null;
            if (hasError)
            {
                r.AddArrangedSubview(NSTextField.CreateLabel("±"));
                error = Field(label + " uncertainty");
                error.PlaceholderString = "0.0";
                error.Alignment = NSTextAlignment.Right;
                error.AddConstraint(NSLayoutConstraint.Create(error, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 60));
                r.AddArrangedSubview(error);
            }

            foreach (var child in r.Views)
                child.TranslatesAutoresizingMaskIntoConstraints = false;
            return r;
        }

        NSView Comments()
        {
            var s = new NSStackView{Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 10, TranslatesAutoresizingMaskIntoConstraints = false};
            CommentTextField = new SheetCommentTextView(new CGRect(0, 0, Width - 40, 64))
            {TranslatesAutoresizingMaskIntoConstraints = true, VerticallyResizable = true, HorizontallyResizable = false, AccessibilityLabel = "Comments"};
            CommentTextField.MinSize = new CGSize(0, 0);
            CommentTextField.MaxSize = new CGSize(10000000, 10000000);
            CommentTextField.TextContainer.ContainerSize = new CGSize(Width - 54, 10000000);
            var sc = commentsScroll = new NSScrollView
            {
                HasVerticalScroller = true,
                HasHorizontalScroller = false,
                AutohidesScrollers = true,
                HorizontalScrollElasticity = NSScrollElasticity.None,
                BorderType = NSBorderType.BezelBorder,
                DocumentView = CommentTextField,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
            sc.AddConstraint(NSLayoutConstraint.Create(sc, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 90));
            s.AddArrangedSubview(sc);
            Fill(s, sc);
            return s;
        }

        public override void ViewDidLayout()
        {
            base.ViewDidLayout();
            if (commentsScroll == null) return;
            var size = commentsScroll.ContentSize;
            if (size.Width <= 0) return;
            var frame = CommentTextField.Frame;
            if (Math.Abs(frame.Width - size.Width) > 0.5 || frame.X != 0)
            {
                CommentTextField.Frame = new CGRect(0, 0, size.Width, Math.Max(size.Height, frame.Height));
                CommentTextField.TextContainerInset = new CGSize(7, 5);
            }
            var origin = commentsScroll.ContentView.Bounds.Location;
            if (origin.X != 0)
            {
                commentsScroll.ContentView.ScrollToPoint(new CGPoint(0, origin.Y));
                commentsScroll.ReflectScrolledClipView(commentsScroll.ContentView);
            }
        }

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();
            // Account for the presentation screen before the opening animation.
            UpdateContentSize(resizeWindow: false);
        }

        void Setup()
        {
            if (Data == null)
                return;
            originalDate = Data.Date;
            tmpoptions = Data.Attributes.Select(x => x.Copy()).ToList();
            ExperimentNameField.StringValue = Data.Name ?? "";
            experimentSummaryLabel.StringValue = Data.Name ?? "";
            ExperimentNameField.Changed += (s, e) => experimentSummaryLabel.StringValue = ExperimentNameField.StringValue;
            filenameLabel.StringValue = System.IO.Path.GetFileName(Data.FileName ?? "");
            filenameLabel.ToolTip = Data.FileName;
            CellConcentrationField.DoubleValue = Data.CellConcentration.Value * 1e6;
            CellConcentrationErrorField.DoubleValue = Data.CellConcentration.SD * 1e6;
            SyringeConcentrationField.DoubleValue = Data.SyringeConcentration.Value * 1e6;
            SyringeConcentrationErrorField.DoubleValue = Data.SyringeConcentration.SD * 1e6;
            TemperatureField.DoubleValue = Data.MeasuredTemperature;
            CellVolumeField.DoubleValue = Data.CellVolume * 1e6;
            using (var comment = new NSAttributedString(Data.Comments ?? ""))
                CommentTextField.TextStorage.SetString(comment);

            CommentTextField.TextColor = NSColor.Label;
            var surrogate = DateTime.SpecifyKind(new DateTime(originalDate.Year, originalDate.Month, originalDate.Day, originalDate.Hour, originalDate.Minute, originalDate.Second), DateTimeKind.Utc);
            datePicker.DateValue = (NSDate)surrogate;
            timePicker.DateValue = (NSDate)surrogate;
            RebuildAttributes();
            var t = Data.IsTandemExperiment;
            CellVolumeField.Enabled = !t;
            CellConcentrationField.Enabled = !t;
            CellConcentrationErrorField.Enabled = !t;
            SyringeConcentrationField.Enabled = !t;
            SyringeConcentrationErrorField.Enabled = !t;
            QueueSize();
        }

        partial void AddAttribute(NSObject sender)
        {
            var o = new ExperimentAttribute();
            tmpoptions.Add(o);
            AddAttribute(o);
            QueueSize();
        }

        void AddAttribute(ExperimentAttribute o)
        {
            var v = new ExperimentAttributeView(new CGRect(0, 0, Width - 40, 22), o, true)
            {TranslatesAutoresizingMaskIntoConstraints = false};
            v.Remove += Remove;
            v.KeyChanged += Keys;
            v.SpecialAttributeSelected += Special;
            attributeViews.Add(v);
            var panel = Padded(v, 6, 3, true, true);
            AttributeStackView.Hidden = false;
            AttributeStackView.AddArrangedSubview(panel);
            Fill(AttributeStackView, panel);
            SetEmptyAttributesVisibility();
        }

        void Remove(object s, EventArgs e)
        {
            var view = (ExperimentAttributeView)s;
            attributeViews.Remove(view);
            var panel = view.Superview;
            panel?.RemoveFromSuperview();
            tmpoptions.Remove(view.Option);
            AttributeStackView.Hidden = tmpoptions.Count == 0;
            SetEmptyAttributesVisibility();
            Keys(null, null);
            QueueSize();
        }

        void Keys(object s, EventArgs e)
        {
            foreach (var v in attributeViews)
                v.UpdateKeyMenu();
        }

        void Special(object s, Tuple<AttributeKey, int> e)
        {
            if (e.Item1 == AttributeKey.Buffer)
                BufferAttribute.SetupSpecialBuffer(tmpoptions, (Buffer)e.Item2);
            RebuildAttributes();
        }

        void RebuildAttributes()
        {
            attributeViews.Clear();
            foreach (var v in AttributeStackView.Views.ToArray())
                v.RemoveFromSuperview();
            foreach (var o in tmpoptions)
                AddAttribute(o);
            AttributeStackView.Hidden = tmpoptions.Count == 0;
            SetEmptyAttributesVisibility();
            QueueSize();
        }

        void SetEmptyAttributesVisibility()
        {
            if (emptyAttributesLabel != null) emptyAttributesLabel.Hidden = tmpoptions.Count != 0;
        }

        void QueueSize()
        {
            if (preparingInitialLayout || sizeUpdateQueued) return;
            sizeUpdateQueued = true;
            BeginInvokeOnMainThread(() =>
            {
                sizeUpdateQueued = false;
                UpdateContentSize(resizeWindow: true);
            });
        }

        void UpdateContentSize(bool resizeWindow)
        {
            View.LayoutSubtreeIfNeeded();
            formStack.LayoutSubtreeIfNeeded();
            var screenHeight = (View.Window?.Screen ?? NSScreen.MainScreen)?.VisibleFrame.Height;
            var max = screenHeight.HasValue ? screenHeight.Value - 80 : MaximumHeight;
            var size = new CGSize(Width, Math.Min(MaximumHeight, max));
            PreferredContentSize = size;
            if (resizeWindow && View.Window != null)
            {
                // The sheet is intentionally stable: content changes only alter scrolling.
                if (Math.Abs(View.Window.ContentView.Frame.Width - size.Width) > 0.5 || Math.Abs(View.Window.ContentView.Frame.Height - size.Height) > 0.5)
                    View.Window.SetContentSize(size);
            }
            else
            {
                View.SetFrameSize(size);
                View.LayoutSubtreeIfNeeded();
            }
        }

        partial void Apply(NSObject sender)
        {
            try
            {
                var d = NSDateToDateTime(datePicker.DateValue, timePicker.DateValue, originalDate);
                if (d != originalDate)
                {
                    Data.Date = d;
                    Data.DateSource = ExperimentDateSource.UserModified;
                }

                if (!string.IsNullOrEmpty(SyringeConcentrationField.StringValue))
                    Data.SyringeConcentration = new(SyringeConcentrationField.DoubleValue / 1e6, SyringeConcentrationErrorField.DoubleValue / 1e6);
                if (!string.IsNullOrEmpty(CellConcentrationField.StringValue))
                    Data.CellConcentration = new(CellConcentrationField.DoubleValue / 1e6, CellConcentrationErrorField.DoubleValue / 1e6);
                if (!string.IsNullOrEmpty(TemperatureField.StringValue))
                    Data.MeasuredTemperature = TemperatureField.DoubleValue;
                if (!string.IsNullOrEmpty(ExperimentNameField.StringValue))
                    Data.Name = ExperimentNameField.StringValue;
                if (!string.IsNullOrEmpty(CellVolumeField.StringValue))
                    Data.CellVolume = CellVolumeField.DoubleValue / 1e6;
                Data.Comments = CommentTextField.String;
                Data.ClearBufferSubtraction(false);
                Data.Attributes.Clear();
                foreach (var v in attributeViews)
                    try
                    {
                        v.ApplyOption(Data);
                    }
                    catch (Exception ex)
                    {
                        ShowPage(true);
                        AppEventHandler.DisplayHandledException(ex);
                        return;
                    }

                RawDataReader.ProcessInjections(Data);
                new DataProcessor(Data).IntegratePeaks();
                DismissViewController(this);
                UpdateTable?.Invoke(this, null);
            }
            catch (Exception ex)
            {
                ShowPage(false);
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        partial void Cancel(NSObject sender) => DismissViewController(this);
        internal static DateTime NSDateToDateTime(NSDate date, NSDate time, DateTime original)
        {
            var d = (DateTime)date;
            var t = (DateTime)time;
            if (d.Year == original.Year && d.Month == original.Month && d.Day == original.Day && t.Hour == original.Hour && t.Minute == original.Minute && t.Second == original.Second)
                return original;
            return DateTime.SpecifyKind(new DateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute, t.Second), original.Kind);
        }

        sealed class TabBarView : NSView
        {
            public override void DrawRect(CGRect dirtyRect)
            {
                NSColor.WindowBackground.SetFill();
                NSBezierPath.FillRect(Bounds);
                NSColor.Separator.SetFill();
                NSBezierPath.FillRect(new CGRect(0, 0, Bounds.Width, 0.5));
            }
        }

        sealed class PanelView : NSView
        {
            public bool Tinted { get; set; }
            public override void DrawRect(CGRect dirtyRect)
            {
                base.DrawRect(dirtyRect);
                var rect = new CGRect(0.5, 0.5, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1));
                using (var path = NSBezierPath.FromRoundedRect(rect, 4, 4))
                {
                    NSColor.ControlBackground.SetFill();
                    path.Fill();
                    if (Tinted)
                    {
                        NSColor.Label.ColorWithAlphaComponent(0.035f).SetFill();
                        path.Fill();
                    }
                    NSColor.Separator.SetStroke();
                    path.LineWidth = 0.5f;
                    path.Stroke();
                }
            }
        }

        sealed class FlippedView : NSView
        {
            public override bool IsFlipped => true;
        }
    }
}
