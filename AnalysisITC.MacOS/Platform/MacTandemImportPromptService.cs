using System;
using System.Globalization;
using AnalysisITC.Platform;
using AppKit;
using CoreGraphics;

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
    public sealed class MacTandemImportPromptService : ITandemImportPromptService
    {
        public TandemConcatenation.BackMixingSettings AskBackMixingSettings(
            string fileName,
            int segmentCount,
            TandemConcatenation.BackMixingSettings defaults)
        {
            var accessory = new NSView(new CGRect(0, 0, 340, 140));
            var deadVolumeField = new NSTextField(new CGRect(190, 106, 80, 22))
            {
                StringValue = (defaults.DeadVolume * 1e6).ToString("G4", CultureInfo.InvariantCulture),
            };
            var mixingFractionField = new NSTextField(new CGRect(190, 78, 80, 22))
            {
                StringValue = "20",
            };
            var removeOverflowCheckbox = new NSButton(new CGRect(0, 48, 300, 20))
            {
                Title = "Remove overflow between segments",
                State = defaults.DidRemoveOverflow ? NSCellStateValue.On : NSCellStateValue.Off,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };
            removeOverflowCheckbox.SetButtonType(NSButtonType.Switch);
            var errorText = new NSTextField(new CGRect(0, 0, 340, 40))
            {
                Editable = false,
                Bordered = false,
                DrawsBackground = false,
                Selectable = false,
                TextColor = NSColor.SystemRed,
                Hidden = true,
            };
            errorText.Cell.Wraps = true;
            errorText.Cell.Scrollable = false;
            errorText.Cell.UsesSingleLineMode = false;
            errorText.Cell.LineBreakMode = NSLineBreakMode.ByWordWrapping;

            accessory.AddSubview(MakeLabel("Dead volume (µL)", 0, 109));
            accessory.AddSubview(deadVolumeField);
            accessory.AddSubview(MakeLabel("Mixing fraction (%)", 0, 81));
            accessory.AddSubview(mixingFractionField);
            accessory.AddSubview(removeOverflowCheckbox);
            accessory.AddSubview(errorText);

            using var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = "Tandem ITC File Detected",
                InformativeText = $"The file \"{fileName}\" contains {segmentCount} tandem segments. Choose how to process segment-to-segment concentrations.",
                AccessoryView = accessory,
            };

            alert.AddButton("Use MicroCal Concat");
            alert.AddButton("Use Back-Mixing Compensation");
            alert.Layout();

            var settings = defaults;
            // Replace the alert's automatic dismissal action so invalid entries
            // can be corrected in this same dialog.
            alert.Buttons[1].Activated += (_, _) =>
            {
                if (!TryParseDouble(deadVolumeField.StringValue, out var deadVolumeMicroliters)
                    || !TandemConcatenation.IsValidDeadVolume(deadVolumeMicroliters)
                    || deadVolumeMicroliters * 1e-6 <= 0)
                {
                    ShowError("Dead volume must be a finite positive number in microliters.", deadVolumeField);
                    return;
                }
                if (!TryParseDouble(mixingFractionField.StringValue, out var mixingFractionPercent)
                    || mixingFractionPercent < 0
                    || mixingFractionPercent > 100)
                {
                    ShowError("Mixing fraction must be a number from 0 to 100 percent.", mixingFractionField);
                    return;
                }

                settings = new TandemConcatenation.BackMixingSettings
                {
                    UseBackMixingMethod = true,
                    DeadVolume = deadVolumeMicroliters * 1e-6,
                    MixingFraction = mixingFractionPercent / 100.0,
                    DidRemoveOverflow = removeOverflowCheckbox.State == NSCellStateValue.On,
                    RemoveOverflowVolume = defaults.RemoveOverflowVolume,
                };
                NSApplication.SharedApplication.StopModalWithCode(1001);
            };

            return alert.RunModal() == 1001 ? settings : defaults;

            void ShowError(string message, NSTextField field)
            {
                errorText.StringValue = message;
                errorText.Hidden = false;
                alert.Window.MakeFirstResponder(field);
                field.SelectText(alert);
            }
        }

        static NSTextField MakeLabel(string text, nfloat x, nfloat y)
        {
            return new NSTextField(new CGRect(x, y, 170, 17))
            {
                StringValue = text,
                Editable = false,
                Bordered = false,
                DrawsBackground = false,
                Selectable = false,
            };
        }

        static bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
