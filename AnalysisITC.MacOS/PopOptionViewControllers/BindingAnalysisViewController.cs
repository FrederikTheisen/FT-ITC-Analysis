using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.UI.MacOS.CustomViews;

namespace AnalysisITC
{
    public partial class BindingAnalysisViewController : NSViewController
    {
        const float Width = 720, MaximumHeight = 600;
        public static event EventHandler UpdateTable;
        AnalysisResult Result { get; set; }
        NSTextField nameField, statusField, headerResultName;
        NSTextView commentsView;
        NSScrollView[] pages;
        WorkspaceTabControl tabs;
        NSScrollView commentScroll;

        public BindingAnalysisViewController(IntPtr handle) : base(handle) { }
        public void SetResult(AnalysisResult result) => Result = result;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            if (Result == null) throw new InvalidOperationException("An analysis result is required.");
            View.SetFrameSize(new CGSize(Width, MaximumHeight));
            BuildView();
            UpdateContentSize(false);
        }

        static void Fill(NSView parent, NSView child)
        {
            child.TranslatesAutoresizingMaskIntoConstraints = false;
            parent.AddConstraints(new[] {
                NSLayoutConstraint.Create(child, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, parent, NSLayoutAttribute.Leading, 1, 0),
                NSLayoutConstraint.Create(child, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, parent, NSLayoutAttribute.Trailing, 1, 0)
            });
        }

        static NSTextField Label(string text, bool secondary = false)
        {
            var label = NSTextField.CreateLabel(text ?? "");
            label.TextColor = secondary ? NSColor.SecondaryLabel : NSColor.Label;
            label.Cell.Wraps = true;
            label.Cell.UsesSingleLineMode = false;
            label.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            label.HorizontalContentSizeConstraintActive = false;
            label.Selectable = true;
            label.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
            label.TranslatesAutoresizingMaskIntoConstraints = false;
            return label;
        }

        NSView Section(string title, IEnumerable<NSView> content)
        {
            var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false };
            var heading = Label(title); heading.Font = NSFont.BoldSystemFontOfSize(13);
            stack.AddArrangedSubview(heading);
            Fill(stack, heading);
            foreach (var item in content) { stack.AddArrangedSubview(item); Fill(stack, item); }
            var panel = new PanelView { TranslatesAutoresizingMaskIntoConstraints = false };
            panel.AddSubview(stack);
            panel.AddConstraints(new[] {
                NSLayoutConstraint.Create(stack, NSLayoutAttribute.Top, NSLayoutRelation.Equal, panel, NSLayoutAttribute.Top, 1, 12),
                NSLayoutConstraint.Create(stack, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, panel, NSLayoutAttribute.Bottom, 1, -12),
                NSLayoutConstraint.Create(stack, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, panel, NSLayoutAttribute.Leading, 1, 12),
                NSLayoutConstraint.Create(stack, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, panel, NSLayoutAttribute.Trailing, 1, -12)
            });
            return panel;
        }

        NSView Pair(string title, string value, string tooltip = null, bool status = false)
        {
            var row = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.Top, Spacing = 12, TranslatesAutoresizingMaskIntoConstraints = false };
            var key = Label(title, true); key.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
            key.WidthAnchor.ConstraintEqualToConstant(166).Active = true;
            var val = Label(value);
            if (tooltip != null) { val.ToolTip = tooltip; key.ToolTip = tooltip; }
            if (status) val.Font = NSFont.BoldSystemFontOfSize(13);
            row.AddArrangedSubview(key); row.AddArrangedSubview(val);
            val.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
            return row;
        }

        void BuildView()
        {
            foreach (var child in View.Subviews.ToArray()) child.RemoveFromSuperview();
            var root = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            View.AddSubview(root);
            View.AddConstraints(new[] {
                NSLayoutConstraint.Create(root, NSLayoutAttribute.Top, NSLayoutRelation.Equal, View, NSLayoutAttribute.Top, 1, 0),
                NSLayoutConstraint.Create(root, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, View, NSLayoutAttribute.Bottom, 1, 0),
                NSLayoutConstraint.Create(root, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, View, NSLayoutAttribute.Leading, 1, 0),
                NSLayoutConstraint.Create(root, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, View, NSLayoutAttribute.Trailing, 1, 0)
            });

            var title = Label("Result Details"); title.Font = NSFont.BoldSystemFontOfSize(NSFont.SystemFontSize);
            var resultName = headerResultName = Label(Result.Name);
            var desc = Label(Result.GetListDescriptionString().Replace(Environment.NewLine, " | "), true);
            foreach (var headerLabel in new[] { resultName, desc })
            {
                headerLabel.Cell.Wraps = false;
                headerLabel.Cell.UsesSingleLineMode = true;
                headerLabel.LineBreakMode = NSLineBreakMode.TruncatingTail;
            }
            resultName.ToolTip = Result.Name;
            desc.ToolTip = Result.GetListDescriptionString().Replace(Environment.NewLine, " | ");
            var header = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 2, TranslatesAutoresizingMaskIntoConstraints = false };
            foreach (var l in new[] { title, resultName, desc }) { header.AddArrangedSubview(l); Fill(header, l); }
            root.AddSubview(header);
            var divider = new NSBox { BoxType = NSBoxType.NSBoxSeparator, TranslatesAutoresizingMaskIntoConstraints = false };
            root.AddSubview(divider);

            var detailContent = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 10, TranslatesAutoresizingMaskIntoConstraints = false };
            nameField = new NSTextField { StringValue = Result.Name ?? "", PlaceholderString = "Result name", BezelStyle = NSTextFieldBezelStyle.Rounded, TranslatesAutoresizingMaskIntoConstraints = false, AccessibilityLabel = "Result name" };
            nameField.Changed += (_, _) =>
            {
                if (headerResultName != null)
                {
                    headerResultName.StringValue = nameField.StringValue;
                    headerResultName.ToolTip = nameField.StringValue;
                }
            };
            detailContent.AddArrangedSubview(Section("Result", new NSView[] { LabeledControl("Name", nameField), Pair("Date", Result.UILongDateWithTime) }));
            var comments = new SheetCommentTextView(new CGRect(0, 0, Width - 64, 90)) { VerticallyResizable = true, HorizontallyResizable = false, MinSize = new CGSize(0, 0), MaxSize = new CGSize(10000000, 10000000), AccessibilityLabel = "Comments" };
            comments.TextStorage.SetString(new NSAttributedString(Result.Comments ?? ""));
            comments.TextColor = NSColor.Label;
            commentsView = comments;
            commentScroll = new NSScrollView { HasVerticalScroller = true, HasHorizontalScroller = false, AutohidesScrollers = true, BorderType = NSBorderType.BezelBorder, DocumentView = comments, TranslatesAutoresizingMaskIntoConstraints = false };
            commentScroll.HeightAnchor.ConstraintEqualToConstant(96).Active = true;
            detailContent.AddArrangedSubview(Section("Comments", new NSView[] { commentScroll }));
            detailContent.AddArrangedSubview(Section("Summary", SummaryRows()));
            foreach (var v in detailContent.ArrangedSubviews) Fill(detailContent, v);

            var experimentContent = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 10, TranslatesAutoresizingMaskIntoConstraints = false };
            foreach (var sol in Result.Solution.Solutions)
            {
                var data = sol.Data;
                experimentContent.AddArrangedSubview(Section(data?.Name ?? sol.SolutionName, new NSView[] {
                    Pair("Date", data?.UIShortDateWithTime ?? ""),
                    Pair("Temperature", data == null ? "" : $"{data.MeasuredTemperature:G3} °C"),
                    Pair("Status", sol.IsValid ? "Valid solution" : "Invalid solution", status: true)
                }));
            }
            if (experimentContent.ArrangedSubviews.Length == 0)
                experimentContent.AddArrangedSubview(Section("Experiments", new NSView[] { Label("No experiments are attached to this result.", true) }));
            foreach (var v in experimentContent.ArrangedSubviews) Fill(experimentContent, v);

            var actionContent = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 12, TranslatesAutoresizingMaskIntoConstraints = false };
            actionContent.AddArrangedSubview(Label("These actions update the current session immediately.", true));
            foreach (var item in new[] { ActionButton("Copy result table", CopyResultTable), ActionButton("Load solutions to experiments", Load), ActionButton("Select result experiments", SelectExperiments) })
                actionContent.AddArrangedSubview(item);
            Fill(actionContent, actionContent.ArrangedSubviews[0]);

            pages = new[] { MakePage(detailContent), MakePage(experimentContent), MakePage(Section("Actions", new[] { actionContent })) };
            tabs = new WorkspaceTabControl(new CGRect(0, 0, 372, 32)) { SegmentCount = 3, ControlSize = NSControlSize.Regular, TranslatesAutoresizingMaskIntoConstraints = false };
            string[] tabNames = { "Details", "Experiments", "Actions" };
            for (nint i = 0; i < 3; i++) { tabs.SetLabel(tabNames[i], i); tabs.SetWidth(120, i); }
            tabs.SelectSegment(0); tabs.Activated += (_, _) => ShowPage((int)tabs.SelectedSegment);
            var tabBar = new TabBarView { TranslatesAutoresizingMaskIntoConstraints = false };
            tabBar.AddSubview(tabs); root.AddSubview(tabBar);
            foreach (var page in pages) root.AddSubview(page);

            var footer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            var footerLine = new NSBox { BoxType = NSBoxType.NSBoxSeparator, TranslatesAutoresizingMaskIntoConstraints = false };
            statusField = Label("");
            statusField.Cell.Wraps = false;
            statusField.Cell.UsesSingleLineMode = true;
            statusField.LineBreakMode = NSLineBreakMode.TruncatingTail;
            statusField.ToolTip = "";
            statusField.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
            var cancel = new NSButton { Title = "Cancel", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\u001b", TranslatesAutoresizingMaskIntoConstraints = false };
            var apply = new NSButton { Title = "Apply", BezelStyle = NSBezelStyle.Rounded, KeyEquivalent = "\r", TranslatesAutoresizingMaskIntoConstraints = false };
            cancel.Activated += (_, _) => DismissViewController(this);
            apply.Activated += (_, _) => Apply();
            cancel.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            apply.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            var buttons = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 10, TranslatesAutoresizingMaskIntoConstraints = false };
            buttons.AddArrangedSubview(statusField); buttons.AddArrangedSubview(new NSView()); buttons.AddArrangedSubview(cancel); buttons.AddArrangedSubview(apply);
            cancel.WidthAnchor.ConstraintEqualToConstant(80).Active = true; apply.WidthAnchor.ConstraintEqualToConstant(80).Active = true;
            footer.AddSubview(footerLine); footer.AddSubview(buttons);
            root.AddSubview(footer);
            footer.AddConstraints(new[] {
                NSLayoutConstraint.Create(footerLine, NSLayoutAttribute.Top, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Top, 1, 0), NSLayoutConstraint.Create(footerLine, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(footerLine, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Trailing, 1, 0),
                NSLayoutConstraint.Create(buttons, NSLayoutAttribute.Top, NSLayoutRelation.Equal, footerLine, NSLayoutAttribute.Bottom, 1, 10), NSLayoutConstraint.Create(buttons, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Leading, 1, 16), NSLayoutConstraint.Create(buttons, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Trailing, 1, -16), NSLayoutConstraint.Create(buttons, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Bottom, 1, 0)
            });

            root.AddConstraints(new[] {
                NSLayoutConstraint.Create(header, NSLayoutAttribute.Top, NSLayoutRelation.Equal, root, NSLayoutAttribute.Top, 1, 12), NSLayoutConstraint.Create(header, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 16), NSLayoutConstraint.Create(header, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, -16),
                NSLayoutConstraint.Create(divider, NSLayoutAttribute.Top, NSLayoutRelation.Equal, header, NSLayoutAttribute.Bottom, 1, 10), NSLayoutConstraint.Create(divider, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(divider, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, 0),
                NSLayoutConstraint.Create(tabBar, NSLayoutAttribute.Top, NSLayoutRelation.Equal, divider, NSLayoutAttribute.Bottom, 1, 0), NSLayoutConstraint.Create(tabBar, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(tabBar, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(tabBar, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 44),
                NSLayoutConstraint.Create(tabs, NSLayoutAttribute.CenterY, NSLayoutRelation.Equal, tabBar, NSLayoutAttribute.CenterY, 1, 0), NSLayoutConstraint.Create(tabs, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, tabBar, NSLayoutAttribute.Leading, 1, 16), NSLayoutConstraint.Create(tabs, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 372),
                NSLayoutConstraint.Create(pages[0], NSLayoutAttribute.Top, NSLayoutRelation.Equal, tabBar, NSLayoutAttribute.Bottom, 1, 0), NSLayoutConstraint.Create(pages[0], NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(pages[0], NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(pages[0], NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, footer, NSLayoutAttribute.Top, 1, 0),
                NSLayoutConstraint.Create(pages[1], NSLayoutAttribute.Top, NSLayoutRelation.Equal, pages[0], NSLayoutAttribute.Top, 1, 0), NSLayoutConstraint.Create(pages[1], NSLayoutAttribute.Leading, NSLayoutRelation.Equal, pages[0], NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(pages[1], NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, pages[0], NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(pages[1], NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, pages[0], NSLayoutAttribute.Bottom, 1, 0),
                NSLayoutConstraint.Create(pages[2], NSLayoutAttribute.Top, NSLayoutRelation.Equal, pages[0], NSLayoutAttribute.Top, 1, 0), NSLayoutConstraint.Create(pages[2], NSLayoutAttribute.Leading, NSLayoutRelation.Equal, pages[0], NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(pages[2], NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, pages[0], NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(pages[2], NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, pages[0], NSLayoutAttribute.Bottom, 1, 0),
                NSLayoutConstraint.Create(footer, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, root, NSLayoutAttribute.Leading, 1, 0), NSLayoutConstraint.Create(footer, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, root, NSLayoutAttribute.Trailing, 1, 0), NSLayoutConstraint.Create(footer, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, root, NSLayoutAttribute.Bottom, 1, -12)
            });
            ShowPage(0);
        }

        static NSView LabeledControl(string title, NSView control)
        {
            var row = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.CenterY, Spacing = 12, TranslatesAutoresizingMaskIntoConstraints = false };
            var label = Label(title, true); label.WidthAnchor.ConstraintEqualToConstant(166).Active = true;
            row.AddArrangedSubview(label); row.AddArrangedSubview(control); return row;
        }

        NSView[] SummaryRows()
        {
            var s = Result.Solution; var c = s.Convergence;
            var rows = new List<NSView> {
                Pair("Experiments", s.Solutions.Count.ToString(CultureInfo.CurrentCulture)), Pair("Model", s.SolutionName),
                Pair("RMSD", s.UnweightedRmsd.ToString("G4", CultureInfo.CurrentCulture), FitMetricTooltipPresentation.Rmsd(c))
            };
            if (s.MolarRMSD.HasValue) { var unit = EnergyUnitResolver.Resolve(AppSettings.EnergyUnitFamily, s.MolarRMSD.Value.Value); rows.Add(Pair("Molar RMSD", s.MolarRMSD.Value.ToString(unit, "G3", withunit: true, permole: true), FitMetricTooltipPresentation.MolarRmsd)); }
            rows.AddRange(new[] {
                Pair("Algorithm", c?.Algorithm.GetProperties().Name ?? ""), Pair("Iterations", c?.Iterations.ToString(CultureInfo.CurrentCulture) ?? ""), Pair("Solve time", c == null ? "" : TimeUnitAttribute.FormatTimeSpanShort(c.Time)), Pair("Error method", s.ErrorEstimationMethod.Description()), Pair("Fitting", s.UseWeightedFitting ? "Weighted injection errors" : "Unweighted"),
                Pair("Concentration uncertainty", ConcentrationSummary(s)), Pair("Parameter unlocking", UnlockingSummary(s)), Pair("Validity", ValiditySummary(Result), status: true)
            });
            if (s.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals) rows.AddRange(new[] { Pair("Bootstrap", $"{s.BootstrapIterations} iterations"), Pair("Bootstrap time", c == null ? "" : TimeUnitAttribute.FormatTimeSpanShort(c.ErrorEstimationTime)) });
            else if (s.ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood) { var p = ProfileLikelihoodEstimator.Summarize(s); rows.AddRange(new[] { Pair("Profile status", ProfileLikelihoodDisplayFormatter.Status(p)), Pair("95% CI endpoints", ProfileLikelihoodDisplayFormatter.Endpoints(p)), Pair("Profile calculation time", p == null ? "Not applicable" : TimeUnitAttribute.FormatTimeSpanShort(p.Elapsed)) }); }
            else if (s.ErrorEstimationMethod == ErrorEstimationMethod.LeaveOneOut) rows.Add(Pair("Error-estimation time", c == null ? "" : TimeUnitAttribute.FormatTimeSpanShort(c.ErrorEstimationTime)));
            return rows.ToArray();
        }

        static string ConcentrationSummary(GlobalSolution s) { var o = s.ModelCloneOptions; if (o?.HasLegacyCombinedLeaveOneOut == true && o.IncludeConcentrationErrorsInBootstrap) return "Legacy combined leave-one-out calculation"; return o?.IncludeConcentrationErrorsInBootstrap == true ? "Bootstrap enabled" : "Not used"; }
        static string UnlockingSummary(GlobalSolution s) { var o = s.ModelCloneOptions; if (o?.HasLegacyCombinedLeaveOneOut == true && o.UnlockBootstrapParameters) return "Legacy combined leave-one-out calculation"; return o?.UnlockBootstrapParameters == true ? "Bootstrap enabled" : "Not used"; }
        static string ValiditySummary(AnalysisResult result) { var report = result.ValidityReport; return report.Reasons.Count == 0 ? report.Status.ToString() : $"{report.Status}: {string.Join("; ", AnalysisResultValidityReasonFormatter.Format(result))}"; }

        NSScrollView MakePage(NSView content)
        {
            var document = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
            document.AddSubview(content);
            document.AddConstraints(new[] { NSLayoutConstraint.Create(content, NSLayoutAttribute.Top, NSLayoutRelation.Equal, document, NSLayoutAttribute.Top, 1, 10), NSLayoutConstraint.Create(content, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, document, NSLayoutAttribute.Leading, 1, 16), NSLayoutConstraint.Create(content, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, document, NSLayoutAttribute.Trailing, 1, -16), NSLayoutConstraint.Create(content, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, document, NSLayoutAttribute.Bottom, 1, -10) });
            var scroll = new NSScrollView { AutohidesScrollers = true, HasVerticalScroller = true, HasHorizontalScroller = false, BorderType = NSBorderType.NoBorder, DocumentView = document, DrawsBackground = true, BackgroundColor = NSColor.WindowBackground, TranslatesAutoresizingMaskIntoConstraints = false };
            scroll.ContentView.AddConstraint(NSLayoutConstraint.Create(document, NSLayoutAttribute.Width, NSLayoutRelation.Equal, scroll.ContentView, NSLayoutAttribute.Width, 1, 0));
            return scroll;
        }

        void ShowPage(int index)
        {
            tabs.SelectSegment(index);
            tabs.NeedsDisplay = true;
            for (var i = 0; i < pages.Length; i++)
                pages[i].Hidden = i != index;
        }

        NSButton ActionButton(string title, Action action)
        {
            var button = new NSButton
            {
                Title = title,
                BezelStyle = NSBezelStyle.Rounded,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
            button.Activated += (_, _) =>
            {
                try { action(); }
                catch (Exception ex) { SetStatus(ex.Message); }
            };
            return button;
        }
        void SetStatus(string message)
        {
            statusField.StringValue = message ?? "";
            statusField.ToolTip = message ?? "";
        }

        void CopyResultTable()
        {
            Exporter.CopyToClipboard(
                Result,
                AppSettings.EnergyUnitFamily,
                energyUnitOverride: null,
                usekelvin: false);
            SetStatus("Result table copied.");
        }

        void Load()
        {
            DataManager.LoadResultSolutionsToExperiments(Result);
            DataManager.InvokeDataDidChange();
            DataManager.InvokeUpdateTable();
            DataAnalysisViewController.InvalidateGraph();
            UpdateTable?.Invoke(this, EventArgs.Empty);
            StatusBarManager.SetStatus("Result solutions loaded into experiments", 3000);
            SetStatus("Solutions loaded.");
        }

        void SelectExperiments()
        {
            var ids = Result.Solution.Solutions
                .Select(solution => solution.Data?.UniqueID)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();
            foreach (var data in DataManager.Data)
                data.Include = ids.Contains(data.UniqueID);

            DataManager.InvokeDataInclusionDidChange();
            UpdateTable?.Invoke(this, EventArgs.Empty);
            StatusBarManager.SetStatus("Experiments used by result selected", 3000);
            SetStatus("Experiment inclusion updated.");
        }

        void Apply()
        {
            var name = nameField.StringValue?.Trim() ?? "";
            if (name.Length == 0)
            {
                ShowPage(0);
                SetStatus("Enter a result name.");
                nameField.ScrollRectToVisible(nameField.Bounds);
                View.Window?.MakeFirstResponder(nameField);
                return;
            }
            Result.Name = name;
            Result.Comments = commentsView.String;
            DataManager.InvokeDataDidChange();
            DataManager.InvokeUpdateDataViewCells();
            DataManager.InvokeUpdateTable();
            UpdateTable?.Invoke(this, EventArgs.Empty);
            StatusBarManager.SetStatus($"{Result.Name} details updated", 2500);
            DismissViewController(this);
        }

        public override void ViewWillAppear() { base.ViewWillAppear(); UpdateContentSize(false); }
        public override void ViewDidLayout()
        {
            base.ViewDidLayout();
            if (commentScroll == null || commentsView == null) return;
            var size = commentScroll.ContentSize;
            if (size.Width <= 0) return;
            var frame = commentsView.Frame;
            if (Math.Abs(frame.Width - size.Width) > 0.5 || frame.X != 0)
            {
                commentsView.Frame = new CGRect(0, 0, size.Width, Math.Max(size.Height, frame.Height));
                commentsView.TextContainerInset = new CGSize(7, 5);
            }
        }
        void UpdateContentSize(bool resize)
        {
            var screen = (PresentingViewController?.View?.Window?.Screen ?? View.Window?.Screen ?? NSScreen.MainScreen)?.VisibleFrame.Height;
            var size = new CGSize(Width, Math.Min(MaximumHeight, screen.HasValue ? screen.Value - 80 : MaximumHeight));
            PreferredContentSize = size;
            if (resize && View.Window != null) View.Window.SetContentSize(size); else { View.SetFrameSize(size); View.LayoutSubtreeIfNeeded(); }
        }

        sealed class PanelView : NSView
        {
            public override void DrawRect(CGRect dirtyRect)
            {
                base.DrawRect(dirtyRect);
                var rect = new CGRect(0.5, 0.5, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1));
                using (var path = NSBezierPath.FromRoundedRect(rect, 4, 4))
                {
                    NSColor.ControlBackground.SetFill();
                    path.Fill();
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
    }
}
