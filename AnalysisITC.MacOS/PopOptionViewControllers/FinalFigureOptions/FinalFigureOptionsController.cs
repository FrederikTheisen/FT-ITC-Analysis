using System;
using System.Globalization;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.UI.MacOS.Drawing;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC
{
    internal static class FinalFigureOptionsController
    {
        const float MinimumWidthCentimeters = 3;
        const float MaximumWidthCentimeters = 20;
        const float MinimumHeightCentimeters = 4;
        const float MaximumHeightCentimeters = 28;

        internal sealed class GeneralControls
        {
            public NSSwitch ShowDataGraphControl { get; set; }
            public NSSegmentedControl EnergyUnitControl { get; set; }
            public NSSegmentedControl TimeUnitControl { get; set; }
            public NSSwitch SanitizeTicks { get; set; }
            public NSTextField WidthLabel { get; set; }
            public NSTextField HeightLabel { get; set; }
            public NSSwitch ShowParametersControl { get; set; }
            public NSSwitch ModelInfoControl { get; set; }
            public NSSwitch FitParametersControl { get; set; }
            public NSSwitch ShowTextUncertaintiesControl { get; set; }
            public NSSegmentedControl TextUncertaintyStyleControl { get; set; }
            public NSSegmentedControl InformationBoxPositionControl { get; set; }
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
            public NSSwitch ShowIntegrationRegions { get; set; }
            public NSSegmentedControl IntegrationRegionStyleControl { get; set; }
            public NSSegmentedControl BaselineStyleControl { get; set; }
            public NSSegmentedControl BaselineLayerControl { get; set; }
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
            public NSStepper FitLineWidthControl { get; set; }
            public NSTextField FitLineWidthLabel { get; set; }
            public NSSegmentedControl SplineInterpolationControl { get; set; }
            public NSSegmentedControl SymbolControl { get; set; }
            public NSTextField SymbolSizeLabel { get; set; }
            public NSStepper SymbolSizeStepper { get; set; }
            public NSSwitch UnifiedHeatAxis { get; set; }
            public NSSwitch UnifiedMolarRatioAxis { get; set; }
            public NSSwitch ShowResiduals { get; set; }
            public NSSwitch AddGapToResidualPlot { get; set; }
            public NSSwitch HideBadData { get; set; }
            public NSSwitch IncludeBadDataInAutoYScaling { get; set; }
            public NSSwitch DrawZeroLine { get; set; }
            public NSSwitch DrawErrorBars { get; set; }
            public NSSwitch BadDataErrorBars { get; set; }
            public NSSwitch DrawConfidence { get; set; }
            public NSSwitch DrawOffsetCorrected { get; set; }
            public NSSwitch DrawParameterGuides { get; set; }
            public NSSegmentedControl PlottedModelControl { get; set; }
            public NSTextField ModelAssessmentLabel { get; set; }
        }

        internal static void SyncGeneral(GeneralControls controls, bool widthValid = true, bool heightValid = true)
        {
            if (controls == null) return;

            if (controls.EnergyUnitControl != null)
            {
                controls.EnergyUnitControl.SelectSegment(
                    FinalFigureModelPresentation.EnergyUnitFamily == EnergyUnitFamily.Calories ? 1 : 0);
                controls.EnergyUnitControl.ToolTip = FinalFigureModelPresentation.EnergyUnitTooltip;
            }
            controls.TimeUnitControl?.SelectSegment((int)FinalFigureGraphView.TimeAxisUnit);

            SyncDimension(controls.WidthLabel, FinalFigureGraphView.Width, widthValid,
                MinimumWidthCentimeters, MaximumWidthCentimeters, "Plot-area width in centimeters");
            SyncDimension(controls.HeightLabel, FinalFigureGraphView.Height, heightValid,
                MinimumHeightCentimeters, MaximumHeightCentimeters, "Plot-area height in centimeters");

            SetState(controls.SanitizeTicks, FinalFigureGraphView.SanitizeTicks);
            SetState(controls.ShowParametersControl, FinalFigureGraphView.DrawExpDetails);
            SetState(controls.ModelInfoControl, FinalFigureGraphView.DrawModelInfo);
            SetState(controls.FitParametersControl, FinalFigureGraphView.DrawFitParameterValues);
            if (controls.ShowParametersControl != null) controls.ShowParametersControl.ToolTip = FinalFigureModelPresentation.ExperimentDetailsTooltip;
            if (controls.ModelInfoControl != null)
            {
                controls.ModelInfoControl.ToolTip = FinalFigureModelPresentation.ModelInfoTooltip;
                controls.ModelInfoControl.Enabled = FinalFigureModelPresentation.Selection != PublicationFigureModelSelection.None;
            }
            if (controls.FitParametersControl != null)
            {
                controls.FitParametersControl.ToolTip = FinalFigureModelPresentation.FitParametersTooltip;
                controls.FitParametersControl.Enabled = FinalFigureModelPresentation.Selection != PublicationFigureModelSelection.None;
            }
            SetState(controls.ShowTextUncertaintiesControl, FinalFigureGraphView.ShowTextUncertainties);
            controls.TextUncertaintyStyleControl?.SelectSegment((int)FinalFigureGraphView.TextUncertaintyStyle);
            if (controls.TextUncertaintyStyleControl != null)
            {
                controls.TextUncertaintyStyleControl.ToolTip = UncertaintyDisplayPresentation.StyleTooltip;
                controls.TextUncertaintyStyleControl.Enabled = FinalFigureGraphView.ShowTextUncertainties;
            }
            controls.InformationBoxPositionControl?.SelectSegment((int)FinalFigureGraphView.InformationBoxPosition);
            SetState(controls.ShowDataGraphControl, FinalFigureGraphView.ShowDataGraph);
            if (controls.ShowDataGraphControl != null) controls.ShowDataGraphControl.ToolTip = FinalFigureModelPresentation.DataGraphTooltip;
            SetText(controls.SyringeNameField, FinalFigureGraphView.SyringeName, "Syringe");
            SetText(controls.CellNameField, FinalFigureGraphView.CellName, "Cell");
            SetState(controls.UseNameAttributes, FinalFigureGraphView.UseNameAttributes);
            controls.NameDisplayModeControl?.SelectSegment((int)FinalFigureGraphView.NameDisplayMode);
            SetState(controls.ShowAxisTitles, FinalFigureGraphView.ShowAxisTitles);
            UpdateParameterDisplayMenu(controls.ParameterDisplayOptionsControl);
            UpdateAttributeDisplayMenu(controls.AttributeDisplayOptionsControl);
        }

        internal static void SyncChangedDefaults(
            GeneralControls general,
            FitControls fit,
            PreferencesChangeSet changes,
            bool widthValid,
            bool heightValid)
        {
            if (changes.Contains(nameof(PreferencesState.FinalFigureWidthCentimeters)))
                SyncDimension(general?.WidthLabel, FinalFigureGraphView.Width, widthValid,
                    MinimumWidthCentimeters, MaximumWidthCentimeters, "Plot-area width in centimeters");
            if (changes.Contains(nameof(PreferencesState.FinalFigureHeightCentimeters)))
                SyncDimension(general?.HeightLabel, FinalFigureGraphView.Height, heightValid,
                    MinimumHeightCentimeters, MaximumHeightCentimeters, "Plot-area height in centimeters");
            if (changes.Contains(nameof(PreferencesState.FinalFigureShowDetailsAsDefault)))
                SetState(general?.ShowParametersControl, FinalFigureGraphView.DrawExpDetails);
            if (changes.Contains(nameof(PreferencesState.FinalFigureShowModelInfoAsDefault)))
                SetState(general?.ModelInfoControl, FinalFigureGraphView.DrawModelInfo);
            if (changes.Contains(nameof(PreferencesState.FinalFigureShowParameterBoxAsDefault)))
                SetState(general?.FitParametersControl, FinalFigureGraphView.DrawFitParameterValues);
            if (changes.Contains(nameof(PreferencesState.UncertaintyDisplayStyle)))
                general?.TextUncertaintyStyleControl?.SelectSegment((int)FinalFigureGraphView.TextUncertaintyStyle);
            if (changes.Contains(nameof(PreferencesState.FinalFigureParameterDisplay)))
                UpdateParameterDisplayMenu(general?.ParameterDisplayOptionsControl);
            if (changes.Contains(nameof(PreferencesState.ShowResidualGraph)))
                SetState(fit?.ShowResiduals, FinalFigureGraphView.ShowResiduals);
            if (changes.Contains(nameof(PreferencesState.ShowResidualGraphGap)))
                SetState(fit?.AddGapToResidualPlot, FinalFigureGraphView.GapResidualGraph);
            if (changes.Contains(nameof(PreferencesState.AutoAxesIgnoresBadData)))
                SetState(fit?.IncludeBadDataInAutoYScaling, !FinalFigureGraphView.AutoAxesIgnoresBadData);
            if (changes.Contains(nameof(PreferencesState.FitLineSmoothness)))
                fit?.SplineInterpolationControl?.SelectSegment((int)FinalFigureGraphView.FitLineSmoothness);
        }

        internal static (bool WidthValid, bool HeightValid) ApplyGeneral(GeneralControls controls)
        {
            if (controls == null) return (true, true);

            var widthValid = TryParseCentimeterValue(controls.WidthLabel,
                MinimumWidthCentimeters, MaximumWidthCentimeters, out var width);
            var heightValid = TryParseCentimeterValue(controls.HeightLabel,
                MinimumHeightCentimeters, MaximumHeightCentimeters, out var height);
            if (widthValid)
            {
                FinalFigureGraphView.Width = width;
            }

            if (heightValid)
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

            if (controls.ModelInfoControl != null)
            {
                FinalFigureGraphView.DrawModelInfo = IsOn(controls.ModelInfoControl);
            }

            if (controls.FitParametersControl != null)
            {
                FinalFigureGraphView.DrawFitParameterValues = IsOn(controls.FitParametersControl);
            }

            if (controls.ShowTextUncertaintiesControl != null)
            {
                FinalFigureGraphView.ShowTextUncertainties = IsOn(controls.ShowTextUncertaintiesControl);
            }

            if (controls.TextUncertaintyStyleControl != null)
            {
                controls.TextUncertaintyStyleControl.Enabled = FinalFigureGraphView.ShowTextUncertainties;
                FinalFigureGraphView.TextUncertaintyStyle =
                    (UncertaintyDisplayStyle)(int)controls.TextUncertaintyStyleControl.SelectedSegment;
            }

            if (controls.InformationBoxPositionControl != null)
            {
                FinalFigureGraphView.InformationBoxPosition =
                    (InformationBoxPlacement)(int)controls.InformationBoxPositionControl.SelectedSegment;
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
                FinalFigureModelPresentation.EnergyUnitFamily = controls.EnergyUnitControl.SelectedSegment == 1
                    ? EnergyUnitFamily.Calories : EnergyUnitFamily.Joules;
            }

            return (controls.WidthLabel == null || widthValid, controls.HeightLabel == null || heightValid);
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

            menu.AutoEnablesItems = false;
            for (int i = 1; i < menu.Items.Length; i++)
            {
                var item = menu.Items[i];
                var flag = GetParameterFlagForMenuIndex(i);

                if (flag == FinalFigureDisplayParameters.None) continue;
                item.Enabled = FinalFigureModelPresentation.Selection != PublicationFigureModelSelection.None;

                item.State = AppSettings.FinalFigureParameterDisplay.HasFlag(flag)
                    ? NSCellStateValue.On
                    : NSCellStateValue.Off;
            }
        }

        internal static void ToggleAttributeOption(NSObject sender, NSMenu menu)
        {
            if (sender is not NSPopUpButton btn) return;

            int index = (int)btn.IndexOfSelectedItem;
            if (index == 1 || index == 2)
            {
                var detailFlag = index == 1
                    ? FinalFigureDisplayParameters.Temperature
                    : FinalFigureDisplayParameters.Concentrations;
                var currentlyEnabled = AppSettings.FinalFigureParameterDisplay.HasFlag(detailFlag);
                SetParameterFlag(detailFlag, !currentlyEnabled);
            }
            else
            {
                var flag = GetAttributeFlagForMenuIndex(index);
                if (flag == DisplayAttributeOptions.None) return;

                bool currentlyEnabled = AppSettings.DisplayAttributeOptions.HasFlag(flag);
                SetAttributeFlag(flag, !currentlyEnabled);
            }

            FinalFigureGraphView.UpdateParameterBoxVisibility();
            UpdateAttributeDisplayMenu(menu);
        }

        internal static void UpdateAttributeDisplayMenu(NSMenu menu)
        {
            if (menu == null) return;

            menu.AutoEnablesItems = false;
            SetMenuState(menu, 1, AppSettings.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Temperature));
            SetMenuState(menu, 2, AppSettings.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Concentrations));

            for (int i = 4; i < menu.Items.Length; i++)
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
            SetState(controls.ShowIntegrationRegions, FinalFigureGraphView.ShowIntegrationRegions);
            SetState(controls.DrawCorrected, FinalFigureGraphView.DrawBaselineCorrected);
            controls.IntegrationRegionStyleControl?.SelectSegment((int)FinalFigureGraphView.IntegrationRegionDisplayStyle);

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

            if (controls.BaselineLayerControl != null)
            {
                controls.BaselineLayerControl.SelectSegment((int)FinalFigureGraphView.BaselineLayerPosition);
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

            if (controls.ShowIntegrationRegions != null)
            {
                FinalFigureGraphView.ShowIntegrationRegions = IsOn(controls.ShowIntegrationRegions);
            }

            if (controls.IntegrationRegionStyleControl != null)
            {
                FinalFigureGraphView.IntegrationRegionDisplayStyle = (IntegrationRegionDisplayStyle)(int)controls.IntegrationRegionStyleControl.SelectedSegment;
            }

            if (controls.DrawCorrected != null)
            {
                FinalFigureGraphView.DrawBaselineCorrected = IsOn(controls.DrawCorrected);
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

            if (controls.BaselineLayerControl != null)
            {
                FinalFigureGraphView.BaselineLayerPosition = (BaselineLayerPosition)(int)controls.BaselineLayerControl.SelectedSegment;
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

            if (controls.FitLineWidthControl != null) controls.FitLineWidthControl.FloatValue = FinalFigureGraphView.FitLineWidth;
            SetState(controls.UnifiedHeatAxis, FinalFigureGraphView.UnifiedEnthalpyAxis);
            SetState(controls.UnifiedMolarRatioAxis, FinalFigureGraphView.UseUnifiedMolarRatioAxis);
            SetState(controls.DrawZeroLine, FinalFigureGraphView.DrawZeroLine);
            SetState(controls.DrawErrorBars, FinalFigureGraphView.ShowErrorBars);
            SetState(controls.BadDataErrorBars, FinalFigureGraphView.ShowBadDataErrorBars);
            SetState(controls.DrawConfidence, FinalFigureGraphView.DrawConfidence);
            SetState(controls.DrawOffsetCorrected, FinalFigureGraphView.DrawFitOffsetCorrected);
            SetState(controls.DrawParameterGuides, FinalFigureGraphView.DrawParameterGuides);
            SyncExcludedInjectionVisibility(controls);
            SetState(controls.IncludeBadDataInAutoYScaling, !FinalFigureGraphView.AutoAxesIgnoresBadData);
            if (controls.IncludeBadDataInAutoYScaling != null)
                controls.IncludeBadDataInAutoYScaling.ToolTip = FinalFigureModelPresentation.ExcludedAutoYScaleTooltip;
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
            SyncPlottedModel(controls);
        }

        /// <summary>Null keeps the Binding offset and confidence choices but disables them.</summary>
        internal static void SyncPlottedModel(FitControls controls)
        {
            if (controls == null) return;

            var binding = FinalFigureModelPresentation.Selection == PublicationFigureModelSelection.BindingModel;
            if (controls.PlottedModelControl != null)
            {
                controls.PlottedModelControl.SelectedSegment = FinalFigureModelPresentation.SelectionIndex;
                controls.PlottedModelControl.ToolTip = FinalFigureModelPresentation.SelectorTooltip;
            }

            var hasModel = FinalFigureModelPresentation.Selection != PublicationFigureModelSelection.None;
            if (controls.FitLineWidthControl != null)
            {
                controls.FitLineWidthControl.Enabled = hasModel;
                controls.FitLineWidthControl.ToolTip = FinalFigureModelPresentation.LineWidthTooltip;
            }
            if (controls.FitLineWidthLabel != null) controls.FitLineWidthLabel.ToolTip = FinalFigureModelPresentation.LineWidthTooltip;
            if (controls.SplineInterpolationControl != null)
            {
                controls.SplineInterpolationControl.Enabled = hasModel;
                controls.SplineInterpolationControl.ToolTip = FinalFigureModelPresentation.SmoothnessTooltip;
            }
            if (controls.ShowResiduals != null) controls.ShowResiduals.Enabled = hasModel;
            if (controls.AddGapToResidualPlot != null) controls.AddGapToResidualPlot.Enabled = hasModel;
            if (controls.DrawParameterGuides != null) controls.DrawParameterGuides.Enabled = binding;
            if (controls.DrawConfidence != null)
            {
                controls.DrawConfidence.Enabled = binding;
                controls.DrawConfidence.ToolTip = FinalFigureModelPresentation.ConfidenceTooltip;
            }
            if (controls.DrawOffsetCorrected != null)
            {
                controls.DrawOffsetCorrected.Enabled = binding;
                controls.DrawOffsetCorrected.ToolTip = FinalFigureModelPresentation.OffsetCorrectionTooltip;
            }

            if (controls.ModelAssessmentLabel != null)
            {
                controls.ModelAssessmentLabel.StringValue = FinalFigureGraphView.CurrentAssessmentText;
                controls.ModelAssessmentLabel.ToolTip = FinalFigureModelPresentation.AssessmentTooltip;
            }
        }

        internal static void ApplyFit(FitControls controls)
        {
            if (controls == null) return;

            if (controls.FitLineWidthControl != null) FinalFigureGraphView.FitLineWidth = controls.FitLineWidthControl.FloatValue;
            UpdateFitLabels(controls);

            if (controls.PlottedModelControl != null)
            {
                FinalFigureModelPresentation.Selection = FinalFigureModelPresentation.FromSelectionIndex((int)controls.PlottedModelControl.SelectedSegment);
                SyncPlottedModel(controls);
            }

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

            if (controls.DrawParameterGuides != null)
            {
                FinalFigureGraphView.DrawParameterGuides = IsOn(controls.DrawParameterGuides);
            }

            if (controls.HideBadData != null && ExcludedInjectionPresentation.CanHideExcludedInjections)
            {
                FinalFigureGraphView.ShowBadData = IsOn(controls.HideBadData);
            }
            SyncExcludedInjectionVisibility(controls);

            if (controls.IncludeBadDataInAutoYScaling != null)
            {
                FinalFigureGraphView.AutoAxesIgnoresBadData = !IsOn(controls.IncludeBadDataInAutoYScaling);
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
                4 => DisplayAttributeOptions.UsedInAnalysis,
                5 => DisplayAttributeOptions.Buffer,
                6 => DisplayAttributeOptions.Salt,
                7 => DisplayAttributeOptions.IonicStrength,
                8 => DisplayAttributeOptions.ProtonationEnthalpy,
                9 => DisplayAttributeOptions.Competitor,

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

        static void SetMenuState(NSMenu menu, int index, bool enabled)
        {
            if (index >= menu.Items.Length) return;

            menu.Items[index].State = enabled ? NSCellStateValue.On : NSCellStateValue.Off;
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
            if (controls?.FitLineWidthLabel != null && controls.FitLineWidthControl != null)
                controls.FitLineWidthLabel.StringValue = controls.FitLineWidthControl.FloatValue.ToString("0.##") + " pt";
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

        static void SyncDimension(NSTextField field, float value, bool valid, float minimum, float maximum, string tooltip)
        {
            if (field == null) return;

            if (valid) field.StringValue = value.ToString("R", CultureInfo.CurrentCulture) + " cm";
            field.TextColor = valid ? NSColor.ControlText : NSColor.SystemRed;
            field.ToolTip = valid ? tooltip : $"Use {minimum}–{maximum} cm; decimal point or comma";
        }

        static bool TryParseCentimeterValue(NSTextField textField, float minimum, float maximum, out float value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(textField?.StringValue)) return false;

            var number = textField.StringValue.Trim();
            if (number.EndsWith("cm", StringComparison.OrdinalIgnoreCase))
                number = number.Substring(0, number.Length - 2).TrimEnd();
            if (number.Contains(",") && number.Contains(".")) return false;

            if (!double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < minimum || parsed > maximum)
                return false;

            value = (float)parsed;
            return true;
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

        static void SyncExcludedInjectionVisibility(FitControls controls)
        {
            if (controls.HideBadData == null) return;
            SetState(controls.HideBadData, FinalFigureGraphView.ShowBadData);
            controls.HideBadData.Enabled = ExcludedInjectionPresentation.CanHideExcludedInjections;
            controls.HideBadData.ToolTip = ExcludedInjectionPresentation.VisibilityTooltip;
        }

        static bool IsOn(NSSwitch control) => control.State == (int)NSCellStateValue.On;

        static void SetState(NSSwitch control, bool enabled)
        {
            if (control == null) return;

            control.State = enabled ? (int)NSCellStateValue.On : (int)NSCellStateValue.Off;
        }
    }
}
