using System;
using AnalysisITC.Platform;
using AppKit;
using CoreGraphics;
using Foundation;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS
{
    public sealed class MacAppNotificationService : IAppNotificationService
    {
        public void ShowInfoAlert(string title, string message, bool useLeftAlignedAccessory = false, string actionUrl = null)
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                using var alert = new NSAlert
                {
                    AlertStyle = NSAlertStyle.Informational,
                    MessageText = title,
                    InformativeText = useLeftAlignedAccessory ? string.Empty : message
                };

                if (useLeftAlignedAccessory)
                    alert.AccessoryView = BuildLeftAlignedTextAccessory(message);

                if (!string.IsNullOrWhiteSpace(actionUrl))
                    alert.AddButton("Open Releases");

                alert.AddButton("OK");

                var response = alert.RunModal();
                if (response == (int)NSAlertButtonReturn.First && !string.IsNullOrWhiteSpace(actionUrl))
                    NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(actionUrl));
            });
        }

        static NSView BuildLeftAlignedTextAccessory(string text, float width = 350)
        {
            var font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);
            var paragraph = new NSMutableParagraphStyle
            {
                Alignment = NSTextAlignment.Left,
                LineBreakMode = NSLineBreakMode.ByWordWrapping
            };

            var attributes = new NSStringAttributes
            {
                Font = font,
                ParagraphStyle = paragraph
            };

            var attributedText = new NSAttributedString(text ?? string.Empty, attributes);
            var bounds = attributedText.BoundingRectWithSize(
                new CGSize(width, nfloat.MaxValue),
                NSStringDrawingOptions.UsesLineFragmentOrigin | NSStringDrawingOptions.UsesFontLeading);

            var height = (nfloat)Math.Ceiling(bounds.Height + 8);
            var container = new NSView(new CGRect(0, 0, width, height));
            var textField = new NSTextField(new CGRect(0, 0, width, height))
            {
                Alignment = NSTextAlignment.Left,
                AttributedStringValue = attributedText,
                Bordered = false,
                DrawsBackground = false,
                Editable = false,
                Selectable = true
            };

            textField.Cell.Alignment = NSTextAlignment.Left;
            textField.Cell.Wraps = true;
            textField.Cell.Scrollable = false;
            textField.Cell.UsesSingleLineMode = false;
            textField.Cell.LineBreakMode = NSLineBreakMode.ByWordWrapping;

            container.AddSubview(textField);
            return container;
        }
    }
}
