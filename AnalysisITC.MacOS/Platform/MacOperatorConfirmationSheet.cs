using System;
using System.Threading.Tasks;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.Core.Application;

namespace AnalysisITC.UI.MacOS
{
    /// <summary>Startup operator confirmation shown as a sheet on the main window.</summary>
    static class MacOperatorConfirmationSheet
    {
        const int ContinueResponse = 1000;
        const int QuitResponse = 1001;
        const float ContentWidth = 380;
        const float Inset = 20;

        /// <summary>Returns true after the trimmed name is saved; false when the user quits.</summary>
        public static Task<bool> ConfirmAsync(NSWindow parent)
        {
            var completion = new TaskCompletionSource<bool>();

            var heading = NSTextField.CreateLabel("Confirm Operator");
            heading.Font = NSFont.BoldSystemFontOfSize(NSFont.SystemFontSize + 2);
            heading.TranslatesAutoresizingMaskIntoConstraints = false;
            var description = NSTextField.CreateWrappingLabel(
                "Traceability Mode is on. New Analysis Results and reports in this session are attributed to this operator. Change it later in General Preferences.");
            description.TextColor = NSColor.SecondaryLabel;
            description.PreferredMaxLayoutWidth = ContentWidth;
            description.TranslatesAutoresizingMaskIntoConstraints = false;

            var label = NSTextField.CreateLabel("Operator name");
            label.TranslatesAutoresizingMaskIntoConstraints = false;
            var name = new NSTextField
            {
                StringValue = AppSettings.UserName ?? "",
                PlaceholderString = "Required",
                Bezeled = true,
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Font = NSFont.SystemFontOfSize(13),
                TranslatesAutoresizingMaskIntoConstraints = false,
                AccessibilityLabel = "Operator name",
            };
            name.Cell.Scrollable = true;
            var nameRow = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Alignment = NSLayoutAttribute.FirstBaseline,
                Spacing = 12,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            nameRow.AddArrangedSubview(label);
            nameRow.AddArrangedSubview(name);

            var quit = Button("Quit");
            var proceed = Button("Continue");
            proceed.KeyEquivalent = "\r";
            var footer = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Spacing = 8,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            footer.AddView(quit, NSStackViewGravity.Trailing);
            footer.AddView(proceed, NSStackViewGravity.Trailing);
            quit.WidthAnchor.ConstraintGreaterThanOrEqualToConstant(80).Active = true;
            proceed.WidthAnchor.ConstraintEqualToAnchor(quit.WidthAnchor).Active = true;

            var content = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Alignment = NSLayoutAttribute.Leading,
                Spacing = 12,
                EdgeInsets = new NSEdgeInsets(Inset, Inset, Inset, Inset),
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            content.AddArrangedSubview(heading);
            content.AddArrangedSubview(description);
            content.AddArrangedSubview(nameRow);
            content.AddArrangedSubview(footer);
            content.SetCustomSpacing(4, heading);
            content.SetCustomSpacing(16, description);
            content.SetCustomSpacing(20, nameRow);
            foreach (var view in new NSView[] { description, nameRow, footer })
                view.WidthAnchor.ConstraintEqualToConstant(ContentWidth).Active = true;

            var sheet = new NSPanel(new CGRect(0, 0, ContentWidth + 2 * Inset, 200),
                NSWindowStyle.Titled, NSBackingStore.Buffered, false)
            {
                Title = "Confirm Operator",
                ReleasedWhenClosed = false,
            };
            sheet.ContentView = content;
            sheet.SetContentSize(content.FittingSize);
            sheet.DefaultButtonCell = proceed.Cell;
            sheet.InitialFirstResponder = name;

            void UpdateContinue()
            {
                var valid = PreferencesState.TryValidateTraceability(true, name.StringValue, out var _);
                proceed.Enabled = valid;
                proceed.ToolTip = valid ? null : "An operator name is required when Traceability Mode is enabled.";
            }
            name.Changed += (sender, e) => UpdateContinue();
            UpdateContinue();

            quit.Activated += (sender, e) => parent.EndSheet(sheet, QuitResponse);
            proceed.Activated += (sender, e) =>
            {
                if (!PreferencesState.TryValidateTraceability(true, name.StringValue, out var _)) return;
                AppSettings.UserName = name.StringValue.Trim();
                AppSettings.Save();
                parent.EndSheet(sheet, ContinueResponse);
            };

            parent.BeginSheet(sheet, response => completion.TrySetResult(response == ContinueResponse));
            return completion.Task;
        }

        static NSButton Button(string title) => new NSButton
        {
            Title = title,
            BezelStyle = NSBezelStyle.Rounded,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
    }
}
