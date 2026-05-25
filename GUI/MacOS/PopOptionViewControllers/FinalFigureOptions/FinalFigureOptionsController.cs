using System;
using System.Globalization;
using System.Text.RegularExpressions;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.AppClasses.AnalysisClasses.Models;

namespace AnalysisITC
{
    internal static class FinalFigureOptionsController
    {
        internal sealed class GeneralControls
        {
            public NSSwitch ShowDataGraphControl { get; set; }
            public NSSegmentedControl EnergyUnitControl { get; set; }
            public NSSegmentedControl TimeUnitControl { get; set; }
            public NSSwitch SanitizeTicks { get; set; }
            public NSTextField WidthLabel { get; set; }
            public NSTextField HeightLabel { get; set; }
            public NSSwitch ShowParametersControl { get; set; }
            public NSSegmentedControl InformationBoxPositionControl { get; set; }
            public NSSwitch ModelInfoControl { get; set; }
            public NSSwitch TemperatureDetailControl { get; set; }
            public NSSwitch ConcentrationDetailControl { get; set; }
            public NSMenu ParameterDisplayOptionsControl { get; set; }
            public NSMenu AttributeDisplayOptionsControl { get; set; }
            public NSTextField SyringeNameField { get; set; }
            public NSTextField CellNameField { get; set; }
            public NSSwitch UseNameAttributes { get; set; }
            public NSSegmentedControl NameDisplayModeControl { get; set; }
            public NSSwitch ShowAxisTitles { get; set; }
        }

        internal sealed class DataControls
        {
            public NSTextField PowerAxisTitleLabel { get; set; }
            public NSTextField TimeAxisTitleLabel { get; set; }
            public NSSegmentedControl TimeUnitControl { get; set; }
            public NSTextField XTickLabel { get; set; }
            public NSStepper XTickStepper { get; set; }
            public NSTextField YTickLabel { get; set; }
            public NSStepper YTickStepper { get; set; }
            public NSTextField XMinField { get; set; }
            public NSTextField XMaxField { get; set; }
            public NSTextField YMinField { get; set; }
            public NSTextField YMaxField { get; set; }
            public NSSwitch UnifiedPowerAxis { get; set; }
            public NSSwitch DrawCorrected { get; set; }
            public NSSwitch DrawBaseline { get; set; }
            public NSSegmentedControl BaselineStyleControl { get; set; }
            public NSStepper BaselineThicknessControl { get; set; }
            public NSTextField BaselineThicknessLabel { get; set; }
        }

        internal sealed class FitControls
        {
            public NSTextField EnthalpyAxisTitleLabel { get; set; }
            public NSStepper YAxisTickStepper { get; set; }
            public NSTextField YTickLabel { get; set; }
            public NSTextField MolarRatioAxisTitleLabel { get; set; }
            public NSStepper XAxisTickStepper { get; set; }
            public NSTextField XTickLabel { get; set; }
            public NSTextField XMinField { get; set; }
            public NSTextField XMaxField { get; set; }
            public NSTextField YMinField { get; set; }
            public NSTextField YMaxField { get; set; }
            public NSSegmentedControl SplineInterpolationControl { get; set; }
            public NSSegmentedControl SymbolControl { get; set; }
            public NSTextField SymbolSizeLabel { get; set; }
            public NSStepper SymbolSizeStepper { get; set; }
            public NSSwitch UnifiedHeatAxis { get; set; }
            public NSSwitch UnifiedMolarRatioAxis { get; set; }
            public NSSwitch ShowResiduals { get; set; }
            public NSSwitch AddGapToResidualPlot { get; set; }
            public NSSwitch HideBadData { get; set; }
            public NSSwitch DrawZeroLine { get; set; }
            public NSSwitch DrawErrorBars { get; set; }
            public NSSwitch BadDataErrorBars { get; set; }
            public NSSwitch DrawConfidence { get; set; }
            public NSSwitch DrawOffsetCorrected { get; set; }
        }

        internal static void SyncGeneral(GeneralControls controls)
        {
            if (controls == null) return;

            var unitnum = FinalFigureGraphView.EnergyUnit switch
            {
                EnergyUnit.KiloJoule => 0,
                EnergyUnit.Joule => 0,
                EnergyUnit.MicroCal => 1,
                EnergyUnit.Cal => 1,
                EnergyUnit.KCal => 1,
                _ => 0,
            };

            controls.EnergyUnitControl?.SelectSegment(unitnum);
            controls.TimeUnitControl?.SelectSegment((int)FinalFigureGraphView.TimeAxisUnit);

            if (controls.WidthLabel != null)
            {
                controls.WidthLabel.StringValue = FinalFigureGraphView.Width.ToString("F1") + " cm";
            }

            if (controls.HeightLabel != null)
            {
                controls.HeightLabel.StringValue = FinalFigureGraphView.Height.ToString("F1") + " cm";
            }

            SetState(controls.SanitizeTicks, FinalFigureGraphView.SanitizeTicks);
            SetState(controls.ShowParametersControl, FinalFigureGraphView.DrawExpDetails);
            controls.InformationBoxPositionControl?.SelectSegment((int)FinalFigureGraphView.InformationBoxPosition);
            SetState(controls.ModelInfoControl, FinalFigureGraphView.DrawModelInfo);
            SetState(controls.TemperatureDetailControl, AppSettings.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Temperature));
            SetState(controls.ConcentrationDetailControl, AppSettings.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Concentrations));
            SetState(controls.ShowDataGraphControl, FinalFigureGraphView.ShowDataGraph);
            SetText(controls.SyringeNameField, FinalFigureGraphView.SyringeName, "Syringe");
            SetText(controls.CellNameField, FinalFigureGraphView.CellName, "Cell");
            SetState(controls.UseNameAttributes, FinalFigureGraphView.UseNameAttributes);
            controls.NameDisplayModeControl?.SelectSegment((int)FinalFigureGraphView.NameDisplayMode);
            SetState(controls.ShowAxisTitles, FinalFigureGraphView.ShowAxisTitles);
            UpdateParameterDisplayMenu(controls.ParameterDisplayOptionsControl);
            UpdateAttributeDisplayMenu(controls.AttributeDisplayOptionsControl);
        }

        internal static void ApplyGeneral(GeneralControls controls)
        {
            if (controls == null) return;

            if (TryParseCentimeterValue(controls.WidthLabel, out var width))
            {
                FinalFigureGraphView.Width = width;
            }

            if (TryParseCentimeterValue(controls.HeightLabel, out var height))
            {
                FinalFigureGraphView.Height = height;
            }

            if (controls.ShowDataGraphControl != null)
            {
                FinalFigureGraphView.ShowDataGraph = IsOn(controls.ShowDataGraphControl);
            }

            if (controls.SanitizeTicks != null)
            {
                FinalFigureGraphView.SanitizeTicks = IsOn(controls.SanitizeTicks);
            }

            if (controls.ShowParametersControl != null)
            {
                FinalFigureGraphView.DrawExpDetails = IsOn(controls.ShowParametersControl);
            }

            if (controls.InformationBoxPositionControl != null)
            {
                FinalFigureGraphView.InformationBoxPosition =
                    (InformationBoxPlacement)(int)controls.InformationBoxPositionControl.SelectedSegment;
            }

            if (controls.ModelInfoControl != null)
            {
                FinalFigureGraphView.DrawModelInfo = IsOn(controls.ModelInfoControl);
            }

            if (controls.TemperatureDetailControl != null)
            {
                SetParameterFlag(FinalFigureDisplayParameters.Temperature, IsOn(controls.TemperatureDetailControl));
            }

            if (controls.ConcentrationDetailControl != null)
            {
                SetParameterFlag(FinalFigureDisplayParameters.Concentrations, IsOn(controls.ConcentrationDetailControl));
            }

            if (controls.SyringeNameField != null)
            {
                FinalFigureGraphView.SyringeName = ReadText(controls.SyringeNameField);
            }

            if (controls.CellNameField != null)
            {
                FinalFigureGraphView.CellName = ReadText(controls.CellNameField);
            }

            if (controls.UseNameAttributes != null)
            {
                FinalFigureGraphView.UseNameAttributes = IsOn(controls.UseNameAttributes);
            }

            if (controls.NameDisplayModeControl != null)
            {
                bool name = controls.NameDisplayModeControl.IsSelectedForSegment(0);
                bool compartment = controls.NameDisplayModeControl.IsSelectedForSegment(1);

                if (name && compartment) FinalFigureGraphView.NameDisplayMode = FinalFigureNameDisplayMode.NameAndCompartment;
                else if (name) FinalFigureGraphView.NameDisplayMode = FinalFigureNameDisplayMode.Name;
                else if (compartment) FinalFigureGraphView.NameDisplayMode = FinalFigureNameDisplayMode.Compartment;
                else FinalFigureGraphView.NameDisplayMode = FinalFigureNameDisplayMode.Compartment;
            }

            if (controls.ShowAxisTitles != null)
            {
                FinalFigureGraphView.ShowAxisTitles = IsOn(controls.ShowAxisTitles);
            }

            FinalFigureGraphView.UpdateParameterBoxVisibility();

            if (controls.TimeUnitControl != null)
            {
                FinalFigureGraphView.TimeAxisUnit = (TimeUnit)(int)controls.TimeUnitControl.SelectedSegment;
            }

            if (controls.EnergyUnitControl != null)
            {
                AppSettings.EnergyUnit = controls.EnergyUnitControl.SelectedSegment == 0
                    ? EnergyUnit.KiloJoule
                    : EnergyUnit.KCal;
            }
        }

        internal static void ToggleParameterOption(NSObject sender, NSMenu menu)
        {
            if (sender is not NSPopUpButton btn) return;

            int index = (int)btn.IndexOfSelectedItem;
            var flag = GetParameterFlagForMenuIndex(index);
            if (flag == FinalFigureDisplayParameters.None) return;

            bool currentlyEnabled = AppSettings.FinalFigureParameterDisplay.HasFlag(flag);
            SetParameterFlag(flag, !currentlyEnabled);
            FinalFigureGraphView.UpdateParameterBoxVisibility();
            UpdateParameterDisplayMenu(menu);
        }

        internal static void UpdateParameterDisplayMenu(NSMenu menu)
        {
            if (menu == null) return;

            for (int i = 1; i < menu.Items.Length; i++)
            {
                var item = menu.Items[i];
                var flag = GetParameterFlagForMenuIndex(i);

                if (flag == FinalFigureDisplayParameters.None) continue;

                item.State = AppSettings.FinalFigureParameterDisplay.HasFlag(flag)
                    ? NSCellStateValue.On
                    : NSCellStateValue.Off;
            }
        }

        internal static void ToggleAttributeOption(NSObject sender, NSMenu menu)
        {
            if (sender is not NSPopUpButton btn) return;

            int index = (int)btn.IndexOfSelectedItem;
            var flag = GetAttributeFlagForMenuIndex(index);
            if (flag == DisplayAttributeOptions.None) return;

            bool currentlyEnabled = AppSettings.DisplayAttributeOptions.HasFlag(flag);
            SetAttributeFlag(flag, !currentlyEnabled);
            UpdateAttributeDisplayMenu(menu);
        }

        internal static void UpdateAttributeDisplayMenu(NSMenu menu)
        {
            if (menu == null) return;

            for (int i = 1; i < menu.Items.Length; i++)
            {
                var item = menu.Items[i];
                var flag = GetAttributeFlagForMenuIndex(i);

                if (flag == DisplayAttributeOptions.None) continue;

                item.State = AppSettings.DisplayAttributeOptions.HasFlag(flag)
                    ? NSCellStateValue.On
                    : NSCellStateValue.Off;
            }
        }

        internal static void SyncData(DataControls controls)
        {
            if (controls == null) return;

            SetState(controls.UnifiedPowerAxis, FinalFigureGraphView.UnifiedPowerAxis);
            SetState(controls.DrawBaseline, FinalFigureGraphView.DrawBaseline);
            SetState(controls.DrawCorrected, FinalFigureGraphView.DrawBaselineCorrected);
            controls.TimeUnitControl?.SelectSegment((int)FinalFigureGraphView.TimeAxisUnit);

            if (controls.PowerAxisTitleLabel != null && FinalFigureGraphView.PowerAxisTitleIsChanged)
            {
                controls.PowerAxisTitleLabel.PlaceholderString = FinalFigureGraphView.PowerAxisTitle;
            }

            if (controls.TimeAxisTitleLabel != null && FinalFigureGraphView.TimeAxisTitleIsChanged)
            {
                controls.TimeAxisTitleLabel.PlaceholderString = FinalFigureGraphView.TimeAxisTitle;
            }

            if (controls.XTickStepper != null)
            {
                controls.XTickStepper.IntValue = FinalFigureGraphView.DataXTickCount;
            }

            if (controls.YTickStepper != null)
            {
                controls.YTickStepper.IntValue = FinalFigureGraphView.DataYTickCount;
            }

            if (controls.BaselineThicknessControl != null)
            {
                controls.BaselineThicknessControl.FloatValue = FinalFigureGraphView.BaselineThickness;
            }

            if (controls.BaselineStyleControl != null)
            {
                controls.BaselineStyleControl.SelectSegment((int)FinalFigureGraphView.BaselineDisplayStyle);
            }

            SetOptionalDouble(controls.XMinField, FinalFigureGraphView.DataXAxisMin);
            SetOptionalDouble(controls.XMaxField, FinalFigureGraphView.DataXAxisMax);
            SetOptionalDouble(controls.YMinField, FinalFigureGraphView.DataYAxisMin);
            SetOptionalDouble(controls.YMaxField, FinalFigureGraphView.DataYAxisMax);
            UpdateDataTickLabels(controls);
        }

        internal static void ApplyData(DataControls controls)
        {
            if (controls == null) return;

            UpdateDataTickLabels(controls);

            if (controls.UnifiedPowerAxis != null)
            {
                FinalFigureGraphView.UnifiedPowerAxis = IsOn(controls.UnifiedPowerAxis);
            }

            if (controls.DrawBaseline != null)
            {
                FinalFigureGraphView.DrawBaseline = IsOn(controls.DrawBaseline);
            }

            if (controls.DrawCorrected != null)
            {
                FinalFigureGraphView.DrawBaselineCorrected = IsOn(controls.DrawCorrected);
            }

            if (controls.TimeUnitControl != null)
            {
                FinalFigureGraphView.TimeAxisUnit = (TimeUnit)(int)controls.TimeUnitControl.SelectedSegment;
            }

            if (!string.IsNullOrWhiteSpace(controls.PowerAxisTitleLabel?.StringValue))
            {
                FinalFigureGraphView.PowerAxisTitle = controls.PowerAxisTitleLabel.StringValue;
            }

            if (!string.IsNullOrWhiteSpace(controls.TimeAxisTitleLabel?.StringValue))
            {
                FinalFigureGraphView.TimeAxisTitle = controls.TimeAxisTitleLabel.StringValue;
            }

            if (controls.XTickStepper != null)
            {
                FinalFigureGraphView.DataXTickCount = controls.XTickStepper.IntValue;
            }

            if (controls.YTickStepper != null)
            {
                FinalFigureGraphView.DataYTickCount = controls.YTickStepper.IntValue;
            }

            if (controls.BaselineStyleControl != null)
            {
                FinalFigureGraphView.BaselineDisplayStyle = (BaselineDisplayStyle)(int)controls.BaselineStyleControl.SelectedSegment;
            }

            if (controls.BaselineThicknessControl != null)
            {
                FinalFigureGraphView.BaselineThickness = controls.BaselineThicknessControl.FloatValue;

            }

            FinalFigureGraphView.DataXAxisMin = ReadOptionalDouble(controls.XMinField);
            FinalFigureGraphView.DataXAxisMax = ReadOptionalDouble(controls.XMaxField);
            FinalFigureGraphView.DataYAxisMin = ReadOptionalDouble(controls.YMinField);
            FinalFigureGraphView.DataYAxisMax = ReadOptionalDouble(controls.YMaxField);
        }

        internal static void SyncFit(FitControls controls)
        {
            if (controls == null) return;

            SetState(controls.UnifiedHeatAxis, FinalFigureGraphView.UnifiedEnthalpyAxis);
            SetState(controls.UnifiedMolarRatioAxis, FinalFigureGraphView.UseUnifiedMolarRatioAxis);
            SetState(controls.DrawZeroLine, FinalFigureGraphView.DrawZeroLine);
            SetState(controls.DrawErrorBars, FinalFigureGraphView.ShowErrorBars);
            SetState(controls.BadDataErrorBars, FinalFigureGraphView.ShowBadDataErrorBars);
            SetState(controls.DrawConfidence, FinalFigureGraphView.DrawConfidence);
            SetState(controls.DrawOffsetCorrected, FinalFigureGraphView.DrawFitOffsetCorrected);
            SetState(controls.HideBadData, FinalFigureGraphView.ShowBadData);
            SetState(controls.ShowResiduals, FinalFigureGraphView.ShowResiduals);
            SetState(controls.AddGapToResidualPlot, FinalFigureGraphView.GapResidualGraph);

            if (controls.SplineInterpolationControl != null)
            {
                controls.SplineInterpolationControl.SelectedSegment = (int)FinalFigureGraphView.FitLineSmoothness;
            }

            if (controls.EnthalpyAxisTitleLabel != null && FinalFigureGraphView.EnthalpyAxisTitleAxisTitleIsChanged)
            {
                controls.EnthalpyAxisTitleLabel.PlaceholderString = FinalFigureGraphView.EnthalpyAxisTitle;
            }

            if (controls.MolarRatioAxisTitleLabel != null && FinalFigureGraphView.MolarRatioAxisTitleIsChanged)
            {
                controls.MolarRatioAxisTitleLabel.PlaceholderString = FinalFigureGraphView.MolarRatioAxisTitle;
            }

            if (controls.XAxisTickStepper != null)
            {
                controls.XAxisTickStepper.IntValue = FinalFigureGraphView.FitXTickCount;
            }

            if (controls.YAxisTickStepper != null)
            {
                controls.YAxisTickStepper.IntValue = FinalFigureGraphView.FitYTickCount;
            }

            if (controls.SymbolSizeStepper != null)
            {
                controls.SymbolSizeStepper.IntValue = (int)(2 * FinalFigureGraphView.SymbolSize);
            }

            SetOptionalDouble(controls.XMinField, FinalFigureGraphView.FitXAxisMin);
            SetOptionalDouble(controls.XMaxField, FinalFigureGraphView.FitXAxisMax);
            SetOptionalDouble(controls.YMinField, FinalFigureGraphView.FitYAxisMin);
            SetOptionalDouble(controls.YMaxField, FinalFigureGraphView.FitYAxisMax);
            controls.SymbolControl?.SetSelected(true, FinalFigureGraphView.SymbolShape);
            UpdateFitLabels(controls);
        }

        internal static void ApplyFit(FitControls controls)
        {
            if (controls == null) return;

            UpdateFitLabels(controls);

            if (controls.UnifiedHeatAxis != null)
            {
                FinalFigureGraphView.UnifiedEnthalpyAxis = IsOn(controls.UnifiedHeatAxis);
            }

            if (controls.UnifiedMolarRatioAxis != null)
            {
                FinalFigureGraphView.UseUnifiedMolarRatioAxis = IsOn(controls.UnifiedMolarRatioAxis);
            }

            if (controls.DrawZeroLine != null)
            {
                FinalFigureGraphView.DrawZeroLine = IsOn(controls.DrawZeroLine);
            }

            if (controls.DrawErrorBars != null)
            {
                FinalFigureGraphView.ShowErrorBars = IsOn(controls.DrawErrorBars);
            }

            if (controls.BadDataErrorBars != null)
            {
                FinalFigureGraphView.ShowBadDataErrorBars = IsOn(controls.BadDataErrorBars);
            }

            if (controls.DrawConfidence != null)
            {
                FinalFigureGraphView.DrawConfidence = IsOn(controls.DrawConfidence);
            }

            if (controls.DrawOffsetCorrected != null)
            {
                FinalFigureGraphView.DrawFitOffsetCorrected = IsOn(controls.DrawOffsetCorrected);
            }

            if (controls.HideBadData != null)
            {
                FinalFigureGraphView.ShowBadData = IsOn(controls.HideBadData);
            }

            if (controls.ShowResiduals != null)
            {
                FinalFigureGraphView.ShowResiduals = IsOn(controls.ShowResiduals);
            }

            if (controls.AddGapToResidualPlot != null)
            {
                FinalFigureGraphView.GapResidualGraph = IsOn(controls.AddGapToResidualPlot);
            }

            if (controls.SplineInterpolationControl != null)
            {
                FinalFigureGraphView.FitLineSmoothness = (GraphBase.LineSmoothness)(int)controls.SplineInterpolationControl.SelectedSegment;
            }

            if (!string.IsNullOrWhiteSpace(controls.EnthalpyAxisTitleLabel?.StringValue))
            {
                FinalFigureGraphView.EnthalpyAxisTitle = controls.EnthalpyAxisTitleLabel.StringValue;
            }

            if (!string.IsNullOrWhiteSpace(controls.MolarRatioAxisTitleLabel?.StringValue))
            {
                FinalFigureGraphView.MolarRatioAxisTitle = controls.MolarRatioAxisTitleLabel.StringValue;
            }

            if (controls.XAxisTickStepper != null)
            {
                FinalFigureGraphView.FitXTickCount = controls.XAxisTickStepper.IntValue;
            }

            if (controls.YAxisTickStepper != null)
            {
                FinalFigureGraphView.FitYTickCount = controls.YAxisTickStepper.IntValue;
            }

            if (controls.SymbolSizeStepper != null)
            {
                FinalFigureGraphView.SymbolSize = controls.SymbolSizeStepper.IntValue / 2f;
            }

            if (controls.SymbolControl != null)
            {
                FinalFigureGraphView.SymbolShape = (int)controls.SymbolControl.SelectedSegment;
            }

            FinalFigureGraphView.FitXAxisMin = ReadOptionalDouble(controls.XMinField);
            FinalFigureGraphView.FitXAxisMax = ReadOptionalDouble(controls.XMaxField);
            FinalFigureGraphView.FitYAxisMin = ReadOptionalDouble(controls.YMinField);
            FinalFigureGraphView.FitYAxisMax = ReadOptionalDouble(controls.YMaxField);
        }

        static FinalFigureDisplayParameters GetParameterFlagForMenuIndex(int index)
        {
            return index switch
            {
                1 => FinalFigureDisplayParameters.Thermodynamic,
                2 => FinalFigureDisplayParameters.Fitted,
                3 => FinalFigureDisplayParameters.Derived,

                5 => FinalFigureDisplayParameters.Nvalue,
                6 => FinalFigureDisplayParameters.Affinity,
                7 => FinalFigureDisplayParameters.Enthalpy,
                8 => FinalFigureDisplayParameters.Gibbs,
                9 => FinalFigureDisplayParameters.Entropy,
                10 => FinalFigureDisplayParameters.Offset,

                _ => FinalFigureDisplayParameters.None,
            };
        }

        static DisplayAttributeOptions GetAttributeFlagForMenuIndex(int index)
        {
            return index switch
            {
                1 => DisplayAttributeOptions.UsedInAnalysis,
                2 => DisplayAttributeOptions.Buffer,
                3 => DisplayAttributeOptions.Salt,
                4 => DisplayAttributeOptions.IonicStrength,
                5 => DisplayAttributeOptions.ProtonationEnthalpy,
                6 => DisplayAttributeOptions.Competitor,

                _ => DisplayAttributeOptions.None,
            };
        }

        static void SetParameterFlag(FinalFigureDisplayParameters flag, bool enabled)
        {
            if (flag == FinalFigureDisplayParameters.None) return;

            if (enabled) AppSettings.FinalFigureParameterDisplay |= flag;
            else AppSettings.FinalFigureParameterDisplay &= ~flag;
        }

        static void SetAttributeFlag(DisplayAttributeOptions flag, bool enabled)
        {
            if (flag == DisplayAttributeOptions.None) return;

            if (enabled) AppSettings.DisplayAttributeOptions |= flag;
            else AppSettings.DisplayAttributeOptions &= ~flag;
        }

        static void UpdateDataTickLabels(DataControls controls)
        {
            if (controls?.XTickLabel != null && controls.XTickStepper != null)
            {
                controls.XTickLabel.IntValue = controls.XTickStepper.IntValue;
            }

            if (controls?.YTickLabel != null && controls.YTickStepper != null)
            {
                controls.YTickLabel.IntValue = controls.YTickStepper.IntValue;
            }

            if (controls?.BaselineThicknessLabel != null && controls.BaselineThicknessControl != null)
            {
                controls.BaselineThicknessLabel.FloatValue = controls.BaselineThicknessControl.FloatValue;
            }
        }

        static void UpdateFitLabels(FitControls controls)
        {
            if (controls?.XTickLabel != null && controls.XAxisTickStepper != null)
            {
                controls.XTickLabel.IntValue = controls.XAxisTickStepper.IntValue;
            }

            if (controls?.YTickLabel != null && controls.YAxisTickStepper != null)
            {
                controls.YTickLabel.IntValue = controls.YAxisTickStepper.IntValue;
            }

            if (controls?.SymbolSizeLabel != null && controls.SymbolSizeStepper != null)
            {
                controls.SymbolSizeLabel.StringValue = (controls.SymbolSizeStepper.IntValue / 2f).ToString("F1");
            }
        }

        static bool TryParseCentimeterValue(NSTextField textField, out float value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(textField?.StringValue)) return false;

            string number = Regex.Replace(textField.StringValue, "[^0-9.]", "");
            return float.TryParse(number, out value);
        }

        static void SetText(NSTextField field, string value, string placeholder)
        {
            if (field == null) return;

            field.StringValue = value ?? "";
            field.PlaceholderString = placeholder;
        }

        static string ReadText(NSTextField field) => field?.StringValue?.Trim() ?? "";

        static void SetOptionalDouble(NSTextField field, double? value)
        {
            if (field == null) return;

            field.PlaceholderString = "Auto";
            field.StringValue = value.HasValue
                ? value.Value.ToString("G4", CultureInfo.CurrentCulture)
                : "";
        }

        static double? ReadOptionalDouble(NSTextField field)
        {
            if (field == null) return null;

            var text = field.StringValue?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)) return value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;

            return null;
        }

        static bool IsOn(NSSwitch control) => control.State == (int)NSCellStateValue.On;

        static void SetState(NSSwitch control, bool enabled)
        {
            if (control == null) return;

            control.State = enabled ? (int)NSCellStateValue.On : (int)NSCellStateValue.Off;
        }
    }
}
