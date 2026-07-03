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
            var accessory = new NSView(new CGRect(0, 0, 340, 92));
            var deadVolumeField = new NSTextField(new CGRect(190, 58, 80, 22))
            {
                StringValue = (defaults.DeadVolume * 1e6).ToString("G4", CultureInfo.InvariantCulture),
            };
            var mixingFractionField = new NSTextField(new CGRect(190, 30, 80, 22))
            {
                StringValue = "20",
            };
            var removeOverflowCheckbox = new NSButton(new CGRect(0, 0, 300, 20))
            {
                Title = "Remove overflow between segments",
                State = defaults.DidRemoveOverflow ? NSCellStateValue.On : NSCellStateValue.Off,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };
            removeOverflowCheckbox.SetButtonType(NSButtonType.Switch);

            accessory.AddSubview(MakeLabel("Dead volume (uL)", 0, 61));
            accessory.AddSubview(deadVolumeField);
            accessory.AddSubview(MakeLabel("Mixing fraction (%)", 0, 33));
            accessory.AddSubview(mixingFractionField);
            accessory.AddSubview(removeOverflowCheckbox);

            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = "Tandem ITC File Detected",
                InformativeText = $"The file \"{fileName}\" contains {segmentCount} tandem segments. Choose how to process segment-to-segment concentrations.",
                AccessoryView = accessory,
            };

            alert.AddButton("Use MicroCal Defaults");
            alert.AddButton("Use Back-Mixing Compensation");
            alert.Layout();

            var response = (int)alert.RunModal();
            if (response != 1001) return defaults;

            if (!TryParseDouble(deadVolumeField.StringValue, out var deadVolumeMicroliters)
                || deadVolumeMicroliters <= 0)
                throw new FormatException("Dead volume must be a positive number in microliters.");
            if (!TryParseDouble(mixingFractionField.StringValue, out var mixingFractionPercent)
                || mixingFractionPercent < 0
                || mixingFractionPercent > 100)
                throw new FormatException("Mixing fraction must be a number from 0 to 100 percent.");

            return new TandemConcatenation.BackMixingSettings
            {
                UseBackMixingMethod = true,
                DeadVolume = deadVolumeMicroliters * 1e-6,
                MixingFraction = mixingFractionPercent / 100.0,
                DidRemoveOverflow = removeOverflowCheckbox.State == NSCellStateValue.On,
                RemoveOverflowVolume = defaults.RemoveOverflowVolume,
            };
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
