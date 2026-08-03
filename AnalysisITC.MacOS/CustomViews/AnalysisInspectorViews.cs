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
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS.CustomViews
{
    internal interface IAnalysisInspectorEditable
    {
        bool HasValidInput { get; }
        bool IsApplicable { get; }
        void FocusInput();
    }

    internal sealed class AnalysisParameterDraft
    {
        public ParameterType Key { get; set; }
        public string RawText { get; set; }
        public double InternalValue { get; set; }
        public double AutomaticValue { get; set; }
        public bool HasOverride { get; set; }
        public bool Locked { get; set; }
        public bool IsValid { get; set; } = true;
        public bool IsApplicable { get; set; } = true;
    }

    internal sealed class AnalysisOptionDraft
    {
        public ExperimentAttribute Option { get; set; }
        public string ValueText { get; set; }
        public string ErrorText { get; set; }
        public bool IsValid { get; set; } = true;
        public bool IsApplicable { get; set; } = true;
    }

    internal readonly struct AnalysisInspectorDraftKey
        : IEquatable<AnalysisInspectorDraftKey>
    {
        const string ExperimentSeparator = "\u001f";

        public AnalysisModel Model { get; }
        public bool IsGlobal { get; }
        public string ExperimentSignature { get; }

        AnalysisInspectorDraftKey(
            AnalysisModel model,
            bool isGlobal,
            string experimentSignature)
        {
            Model = model;
            IsGlobal = isGlobal;
            ExperimentSignature = experimentSignature ?? string.Empty;
        }

        public static AnalysisInspectorDraftKey Create(
            AnalysisModel model,
            bool isGlobal,
            IEnumerable<ExperimentData> experiments)
        {
            var experimentIds = experiments
                .Where(experiment => experiment != null)
                .Select(experiment => experiment.UniqueID)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.Ordinal);
            return new AnalysisInspectorDraftKey(
                model,
                isGlobal,
                string.Join(ExperimentSeparator, experimentIds));
        }

        public bool ReferencesOnly(ISet<string> availableExperimentIds)
        {
            return string.IsNullOrEmpty(ExperimentSignature)
                || ExperimentSignature
                    .Split(new[] { ExperimentSeparator }, StringSplitOptions.None)
                    .All(availableExperimentIds.Contains);
        }

        public bool Equals(AnalysisInspectorDraftKey other)
        {
            return Model == other.Model
                && IsGlobal == other.IsGlobal
                && string.Equals(
                    ExperimentSignature,
                    other.ExperimentSignature,
                    StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AnalysisInspectorDraftKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int)Model;
                hashCode = (hashCode * 397) ^ IsGlobal.GetHashCode();
                hashCode = (hashCode * 397)
                    ^ StringComparer.Ordinal.GetHashCode(ExperimentSignature);
                return hashCode;
            }
        }
    }

    internal sealed class AnalysisInspectorDraft
    {
        public Dictionary<ParameterType, AnalysisParameterDraft> Parameters { get; } = new();
        public Dictionary<ParameterType, VariableConstraint> Constraints { get; } = new();
        public Dictionary<AttributeKey, AnalysisOptionDraft> Options { get; } = new();

        public static AnalysisInspectorDraft FromContext(
            AnalysisContext context,
            AnalysisSessionState session)
        {
            var draft = new AnalysisInspectorDraft();
            if (context == null || session == null) return draft;

            foreach (var option in context.ExposedModelOptions)
            {
                draft.Options[option.Key] = new AnalysisOptionDraft
                {
                    Option = option.Value.Copy(),
                };
            }

            foreach (var constraint in context.ExposedConstraintOptions)
            {
                draft.Constraints[constraint.Key] =
                    context.GlobalModelParameters?.GetConstraintForParameter(constraint.Key)
                    ?? VariableConstraint.None;
            }

            foreach (var parameter in context.ExposedParameters)
                draft.GetOrCreateParameter(parameter, session);

            return draft;
        }

        public void MergeContext(
            AnalysisContext context,
            AnalysisSessionState session)
        {
            var fresh = FromContext(context, session);

            foreach (var parameter in fresh.Parameters)
            {
                if (Parameters.TryGetValue(parameter.Key, out var retained))
                {
                    retained.AutomaticValue = parameter.Value.AutomaticValue;
                    retained.IsApplicable = parameter.Value.IsApplicable;
                    continue;
                }

                Parameters[parameter.Key] = parameter.Value;
            }

            var mergedConstraints =
                new Dictionary<ParameterType, VariableConstraint>();
            foreach (var constraint in fresh.Constraints)
            {
                var retained = Constraints.TryGetValue(constraint.Key, out var value)
                    && context.ExposedConstraintOptions.TryGetValue(
                        constraint.Key,
                        out var availableValues)
                    && availableValues.Contains(value)
                    ? value
                    : constraint.Value;
                mergedConstraints[constraint.Key] = retained;
            }
            Constraints.Clear();
            foreach (var constraint in mergedConstraints)
                Constraints[constraint.Key] = constraint.Value;

            var mergedOptions = new Dictionary<AttributeKey, AnalysisOptionDraft>();
            foreach (var option in fresh.Options)
            {
                if (!Options.TryGetValue(option.Key, out var retained))
                {
                    mergedOptions[option.Key] = option.Value;
                    continue;
                }

                var refreshedOption = option.Value.Option;
                refreshedOption.BoolValue = retained.Option.BoolValue;
                refreshedOption.IntValue = retained.Option.IntValue;
                refreshedOption.DoubleValue = retained.Option.DoubleValue;
                refreshedOption.StringValue = retained.Option.StringValue;
                refreshedOption.ParameterValue = retained.Option.ParameterValue;
                mergedOptions[option.Key] = new AnalysisOptionDraft
                {
                    Option = refreshedOption,
                    ValueText = retained.ValueText,
                    ErrorText = retained.ErrorText,
                    IsValid = retained.IsValid,
                    IsApplicable = retained.IsApplicable,
                };
            }
            Options.Clear();
            foreach (var option in mergedOptions)
                Options[option.Key] = option.Value;
        }

        public AnalysisParameterDraft GetOrCreateParameter(
            Parameter parameter,
            AnalysisSessionState session)
        {
            if (Parameters.TryGetValue(parameter.Key, out var existing))
            {
                existing.AutomaticValue = parameter.Value;
                return existing;
            }

            var overrideKey = new ParameterOverrideKey(session.ModelType, parameter.Key);
            var hasOverride = session.Active.ParameterOverrides.TryGetValue(overrideKey, out var stored);
            var locked = hasOverride ? stored.IsLocked : parameter.IsLocked;
            var value = hasOverride ? stored.Value : parameter.Value;

            var draft = new AnalysisParameterDraft
            {
                Key = parameter.Key,
                InternalValue = value,
                AutomaticValue = parameter.Value,
                HasOverride = hasOverride || parameter.IsLocked,
                Locked = locked,
            };

            Parameters[parameter.Key] = draft;
            return draft;
        }
    }

    internal static class AnalysisInspectorDisplayCatalog
    {
        public static int OptionOrder(AttributeKey key)
        {
            return key switch
            {
                AttributeKey.UseSyringeActiveFraction => 0,
                AttributeKey.LockDuplicateParameter => 1,
                AttributeKey.NumberOfSites1 => 2,
                AttributeKey.NumberOfSites2 => 3,
                AttributeKey.PreboundLigandConc => 10,
                AttributeKey.PreboundLigandAffinity => 11,
                AttributeKey.PreboundLigandEnthalpy => 12,
                AttributeKey.Percentage => 13,
                _ => 100 + (int)key,
            };
        }

        public static string OptionTitle(ExperimentAttribute option)
        {
            return option.Key switch
            {
                AttributeKey.LockDuplicateParameter => "Share N-values",
                AttributeKey.NumberOfSites1 => "Site 1 Stoichiometry",
                AttributeKey.NumberOfSites2 => "Site 2 Stoichiometry",
                AttributeKey.PreboundLigandConc => "Prebound Ligand Concentration",
                AttributeKey.PreboundLigandAffinity => "Prebound Ligand Affinity",
                AttributeKey.PreboundLigandEnthalpy => "Prebound Ligand Enthalpy",
                AttributeKey.Percentage => "*Cis* Population",
                _ => option.GetDisplayName(),
            };
        }

        public static string ConstraintTitle(VariableConstraint constraint)
        {
            return constraint switch
            {
                VariableConstraint.SameForAll => "Shared",
                VariableConstraint.TemperatureDependent => "Temperature dependent",
                _ => "Independent",
            };
        }

        public static string ParameterUnit(ParameterType key)
        {
            var parent = key.GetProperties().ParentType;

            if (parent == ParameterType.Affinity1 && AppSettings.InputAffinityAsDissociationConstant)
                return AppSettings.DefaultConcentrationUnit.GetProperties().Name;
            if (parent == ParameterType.HeatCapacity1 || parent == ParameterType.Entropy1)
                return AppSettings.EnergyUnit.GetProperties().Unit + "/mol/K";
            if (UsesEnergyScale(key))
                return AppSettings.EnergyUnit.GetProperties().Unit + "/mol";
            if (key == ParameterType.IsomerizationRate)
                return "s⁻¹";
            if (key == ParameterType.CisIsomerPopulationPercentage)
                return "%";

            return string.Empty;
        }

        public static string FormatParameter(ParameterType key, double value)
        {
            var parent = key.GetProperties().ParentType;

            if (parent == ParameterType.Affinity1)
            {
                if (AppSettings.InputAffinityAsDissociationConstant)
                {
                    var display = AppSettings.DefaultConcentrationUnit.GetProperties().Mod
                        / Math.Pow(10, value);
                    return FormatNumber(display);
                }

                return value.ToString("G4", CultureInfo.CurrentCulture);
            }

            if (UsesEnergyScale(key))
                return FormatNumber(Energy.ConvertFromJoule(value, AppSettings.EnergyUnit));

            return FormatNumber(value);
        }

        public static bool TryParseParameter(
            Parameter parameter,
            string text,
            out double internalValue)
        {
            internalValue = parameter.Value;
            if (!TryParseNumber(text, out var displayValue)) return false;

            var parent = parameter.Key.GetProperties().ParentType;
            if (parent == ParameterType.Affinity1 && AppSettings.InputAffinityAsDissociationConstant)
            {
                if (displayValue <= 0) return false;
                internalValue = Math.Log10(
                    AppSettings.DefaultConcentrationUnit.GetProperties().Mod / displayValue);
            }
            else if (UsesEnergyScale(parameter.Key))
            {
                internalValue = Energy.ConvertToJoule(displayValue, AppSettings.EnergyUnit);
            }
            else
            {
                internalValue = displayValue;
            }

            if (double.IsNaN(internalValue) || double.IsInfinity(internalValue))
                return false;

            return parameter.Limits == null
                || (internalValue >= parameter.Limits[0] && internalValue <= parameter.Limits[1]);
        }

        public static string OptionUnit(AttributeKey key)
        {
            return key switch
            {
                AttributeKey.PreboundLigandConc => AppSettings.DefaultConcentrationUnit.GetProperties().Name,
                AttributeKey.PreboundLigandAffinity => AppSettings.DefaultConcentrationUnit.GetProperties().Name,
                AttributeKey.PreboundLigandEnthalpy => AppSettings.EnergyUnit.GetProperties().Unit + "/mol",
                AttributeKey.Percentage => "%",
                _ => string.Empty,
            };
        }

        public static string OptionSymbol(AttributeKey key)
        {
            return key switch
            {
                AttributeKey.NumberOfSites1 => "N{1}",
                AttributeKey.NumberOfSites2 => "N{2}",
                AttributeKey.PreboundLigandConc => "[*L*]{0}",
                AttributeKey.PreboundLigandAffinity => "*K*{d}",
                AttributeKey.PreboundLigandEnthalpy => "∆*H*",
                AttributeKey.EquilibriumConstant => "*K*{eq}",
                AttributeKey.UseSyringeActiveFraction => "α",
                _ => string.Empty,
            };
        }

        public static (double value, double error) OptionDisplayValue(ExperimentAttribute option)
        {
            var value = option.ParameterValue;
            switch (option.Key)
            {
                case AttributeKey.Percentage:
                    return (value.Value * 100, value.SD * 100);
                case AttributeKey.PreboundLigandConc:
                    return (
                        value.Value * AppSettings.DefaultConcentrationUnit.GetProperties().Mod,
                        value.SD * AppSettings.DefaultConcentrationUnit.GetProperties().Mod);
                case AttributeKey.PreboundLigandAffinity:
                    var kd = 1.0 / FWEMath.Pow(10.0, value);
                    return (
                        kd.Value * AppSettings.DefaultConcentrationUnit.GetProperties().Mod,
                        kd.SD * AppSettings.DefaultConcentrationUnit.GetProperties().Mod);
                case AttributeKey.PreboundLigandEnthalpy:
                    return (
                        Energy.ConvertFromJoule(value.Value, AppSettings.EnergyUnit),
                        Math.Abs(Energy.ConvertFromJoule(value.SD, AppSettings.EnergyUnit)));
                default:
                    return (value.Value, value.SD);
            }
        }

        public static bool TrySetOptionParameter(
            AnalysisOptionDraft draft,
            string valueText,
            string errorText)
        {
            if (!TryParseNumber(valueText, out var value)) return false;

            var error = 0.0;
            if (!string.IsNullOrWhiteSpace(errorText)
                && !TryParseNumber(errorText, out error))
                return false;
            if (error < 0) return false;

            FloatWithError stored;
            switch (draft.Option.Key)
            {
                case AttributeKey.Percentage:
                    if (value < 0 || value > 100) return false;
                    stored = new FloatWithError(value / 100, error / 100);
                    break;
                case AttributeKey.PreboundLigandConc:
                    if (value < 0) return false;
                    var concentrationMod = AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                    stored = new FloatWithError(value / concentrationMod, error / concentrationMod);
                    break;
                case AttributeKey.PreboundLigandAffinity:
                    if (value <= 0) return false;
                    var affinityMod = AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                    var kdValue = value / affinityMod;
                    var kdError = error / affinityMod;
                    var association = 1 / kdValue;
                    var associationError = kdError / kdValue * association;
                    stored = FWEMath.Log10(new FloatWithError(association, associationError));
                    break;
                case AttributeKey.PreboundLigandEnthalpy:
                    stored = new Energy(
                        new FloatWithError(value, error),
                        AppSettings.EnergyUnit).FloatWithError;
                    break;
                default:
                    stored = new FloatWithError(value, error);
                    break;
            }

            if (double.IsNaN(stored.Value) || double.IsInfinity(stored.Value)
                || double.IsNaN(stored.SD) || double.IsInfinity(stored.SD))
                return false;

            draft.Option.ParameterValue = stored;
            return true;
        }

        public static bool IsOptionEnabled(
            AttributeKey key,
            IDictionary<AttributeKey, AnalysisOptionDraft> options)
        {
            var useSyringe = options.TryGetValue(AttributeKey.UseSyringeActiveFraction, out var syringe)
                && syringe.Option.BoolValue;
            var shareNValues = options.TryGetValue(AttributeKey.LockDuplicateParameter, out var shared)
                && shared.Option.BoolValue;

            return key switch
            {
                AttributeKey.NumberOfSites1 => useSyringe,
                AttributeKey.NumberOfSites2 => useSyringe && !shareNValues,
                _ => true,
            };
        }

        public static string DisabledOptionToolTip(
            AttributeKey key,
            IDictionary<AttributeKey, AnalysisOptionDraft> options)
        {
            if (key == AttributeKey.NumberOfSites1)
                return "Enable Use Syringe Correction to edit site stoichiometry.";
            if (key == AttributeKey.NumberOfSites2)
            {
                var shared = options.TryGetValue(AttributeKey.LockDuplicateParameter, out var option)
                    && option.Option.BoolValue;
                return shared
                    ? "Site 2 uses the shared N-value."
                    : "Enable Use Syringe Correction to edit site stoichiometry.";
            }

            return string.Empty;
        }

        public static bool IsParameterEnabled(
            ParameterType key,
            IDictionary<AttributeKey, AnalysisOptionDraft> options)
        {
            if (key != ParameterType.Nvalue2) return true;

            var useSyringe = options.TryGetValue(AttributeKey.UseSyringeActiveFraction, out var syringe)
                && syringe.Option.BoolValue;
            var shareNValues = options.TryGetValue(AttributeKey.LockDuplicateParameter, out var shared)
                && shared.Option.BoolValue;
            return !(useSyringe || shareNValues);
        }

        public static bool UsesEnergyScale(ParameterType key)
        {
            return ParameterTypeAttribute.IsEnergyUnitParameter(key)
                || key.GetProperties().ParentType == ParameterType.Entropy1;
        }

        public static bool TryParseNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(
                    (text ?? string.Empty).Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
        }

        public static string FormatNumber(double value)
        {
            return value.ToString("G6", CultureInfo.CurrentCulture);
        }
    }

    internal sealed class AnalysisParameterTextFieldCell : NSTextFieldCell
    {
        static readonly nfloat HorizontalTextInset = 6;

        public override CGRect DrawingRectForBounds(CGRect theRect)
        {
            return TextRectForBounds(theRect);
        }

        public override void EditWithFrame(
            CGRect aRect,
            NSView inView,
            NSText editor,
            NSObject delegateObject,
            NSEvent theEvent)
        {
            base.EditWithFrame(
                TextRectForBounds(aRect),
                inView,
                editor,
                delegateObject,
                theEvent);
        }

        public override void SelectWithFrame(
            CGRect aRect,
            NSView inView,
            NSText editor,
            NSObject delegateObject,
            nint selStart,
            nint selLength)
        {
            base.SelectWithFrame(
                TextRectForBounds(aRect),
                inView,
                editor,
                delegateObject,
                selStart,
                selLength);
        }

        CGRect TextRectForBounds(CGRect theRect)
        {
            var drawingRect = base.DrawingRectForBounds(theRect);
            drawingRect.X += HorizontalTextInset;
            drawingRect.Width = (nfloat)Math.Max(
                0,
                (double)(drawingRect.Width - 2 * HorizontalTextInset));

            if (Font != null)
            {
                var textHeight = (nfloat)Math.Ceiling(
                    (double)(Font.Ascender - Font.Descender));
                drawingRect.Y = theRect.Y
                    + (nfloat)Math.Floor(
                        (double)((theRect.Height - textHeight) / 2));
                drawingRect.Height = textHeight;
            }

            return drawingRect;
        }
    }

    internal sealed class AnalysisParameterTextField : NSTextField
    {
        public AnalysisParameterTextField()
        {
            Cell = new AnalysisParameterTextFieldCell();
            Bordered = false;
            Bezeled = false;
            DrawsBackground = false;
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            var fieldBounds = Bounds;
            fieldBounds.Inflate(-0.5, -0.5);
            var fieldPath = NSBezierPath.FromRoundedRect(fieldBounds, 5, 5);

            var fillColor = Enabled
                ? NSColor.ControlBackground.ColorWithAlphaComponent(0.55f)
                : NSColor.Label.ColorWithAlphaComponent(0.035f);
            fillColor.SetFill();
            fieldPath.Fill();

            var borderOpacity = Enabled ? 0.13f : 0.065f;
            NSColor.Label.ColorWithAlphaComponent(borderOpacity).SetStroke();
            fieldPath.LineWidth = 1;
            fieldPath.Stroke();

            base.DrawRect(dirtyRect);
        }
    }

    internal abstract class AnalysisInspectorItemView : NSStackView
    {
        readonly bool showsDivider;
        readonly bool highlightsOnHover;
        NSTrackingArea hoverTrackingArea;
        bool reservesDividerSpace;
        bool isHovered;
        nfloat fixedItemHeight = NSView.NoIntrinsicMetric;

        protected AnalysisInspectorItemView(
            bool showsDivider,
            bool highlightsOnHover = false)
        {
            this.showsDivider = showsDivider;
            this.highlightsOnHover = highlightsOnHover;
            Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterX;
            Spacing = 3;
            DetachesHiddenViews = true;
            TranslatesAutoresizingMaskIntoConstraints = false;
            SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
        }

        protected NSStackView CreateHorizontalRow(
            NSLayoutAttribute alignment = NSLayoutAttribute.CenterY)
        {
            return new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = alignment,
                Spacing = 6,
                DetachesHiddenViews = true,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
        }

        protected NSTextField CreateTitle(
            string title,
            string symbol,
            bool enabled = true,
            bool bold = false,
            bool medium = false)
        {
            var label = new NSTextField
            {
                Bordered = false,
                Editable = false,
                DrawsBackground = false,
                ControlSize = NSControlSize.Large,
                Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                AttributedStringValue = MacStrings.AnalysisInspectorItemTitle(
                    title,
                    symbol,
                    (float)NSFont.SystemFontSize,
                    enabled,
                    bold,
                    medium),
                LineBreakMode = NSLineBreakMode.Clipping,
                MaximumNumberOfLines = 1,
                HorizontalContentSizeConstraintActive = true,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            label.Cell.UsesSingleLineMode = true;
            label.AddConstraint(NSLayoutConstraint.Create(label, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 18));
            label.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
            label.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            return label;
        }

        protected NSTextField CreateParameterMetadata(
            string symbol,
            string unit,
            bool enabled = true)
        {
            var metadata = symbol ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(unit))
                metadata += (string.IsNullOrWhiteSpace(metadata) ? string.Empty : ", ")
                    + unit;

            var font = NSFont.SystemFontOfSize(
                NSFont.SystemFontSize * 0.88f,
                NSFontWeight.Light);
            var attributedMetadata = MacStrings.FromMarkDownString(metadata, font);
            attributedMetadata.AddAttribute(
                NSStringAttributeKey.ForegroundColor,
                enabled ? NSColor.SecondaryLabel : NSColor.DisabledControlText,
                new NSRange(0, attributedMetadata.Length));

            var label = new NSTextField
            {
                Bordered = false,
                Editable = false,
                DrawsBackground = false,
                ControlSize = NSControlSize.Regular,
                AttributedStringValue = attributedMetadata,
                Alignment = NSTextAlignment.Left,
                LineBreakMode = NSLineBreakMode.Clipping,
                MaximumNumberOfLines = 1,
                HorizontalContentSizeConstraintActive = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            label.Cell.UsesSingleLineMode = true;
            label.AddConstraint(NSLayoutConstraint.Create(
                label,
                NSLayoutAttribute.Height,
                NSLayoutRelation.Equal,
                1,
                18));
            label.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            label.SetContentCompressionResistancePriority(
                750,
                NSLayoutConstraintOrientation.Horizontal);
            return label;
        }

        protected NSTextField CreateSymbol(string symbol, bool enabled = true)
        {
            var font = NSFont.SystemFontOfSize(NSFont.SystemFontSize * 0.82f);
            var attributedSymbol = MacStrings.FromMarkDownString(
                symbol ?? string.Empty,
                font);
            attributedSymbol.AddAttribute(
                NSStringAttributeKey.ForegroundColor,
                enabled ? NSColor.SecondaryLabel : NSColor.DisabledControlText,
                new NSRange(0, attributedSymbol.Length));

            var label = new NSTextField
            {
                Bordered = false,
                Editable = false,
                DrawsBackground = false,
                ControlSize = NSControlSize.Regular,
                AttributedStringValue = attributedSymbol,
                Alignment = NSTextAlignment.Right,
                LineBreakMode = NSLineBreakMode.Clipping,
                MaximumNumberOfLines = 1,
                HorizontalContentSizeConstraintActive = true,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            label.Cell.UsesSingleLineMode = true;
            label.AddConstraint(NSLayoutConstraint.Create(label, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 18));
            label.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
            label.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            return label;
        }

        protected NSTextField CreateUnitLabel(string unit)
        {
            var label = new NSTextField
            {
                StringValue = unit ?? string.Empty,
                Bordered = false,
                Editable = false,
                DrawsBackground = false,
                TextColor = NSColor.SecondaryLabel,
                Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                ControlSize = NSControlSize.Regular,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            label.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            return label;
        }

        protected NSTextField CreateExplanationLabel(string text)
        {
            var label = new NSTextField
            {
                StringValue = text ?? string.Empty,
                Bordered = false,
                Editable = false,
                Selectable = false,
                DrawsBackground = false,
                FocusRingType = NSFocusRingType.None,
                TextColor = NSColor.SecondaryLabel,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Left,
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
                ControlSize = NSControlSize.Small,
                MaximumNumberOfLines = 0,
                HorizontalContentSizeConstraintActive = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            label.Cell.Wraps = true;
            label.Cell.Scrollable = false;
            label.Cell.UsesSingleLineMode = false;
            label.SetContentHuggingPriorityForOrientation(
                1000,
                NSLayoutConstraintOrientation.Vertical);
            label.SetContentCompressionResistancePriority(
                1000,
                NSLayoutConstraintOrientation.Vertical);
            return label;
        }

        protected NSTextField CreateTextField(
            bool usesParameterStyle = false,
            NSTextAlignment alignment = NSTextAlignment.Left)
        {
            NSTextField field = usesParameterStyle
                ? new AnalysisParameterTextField()
                : new NSTextField
                {
                    Bordered = false,
                    Bezeled = true,
                    DrawsBackground = true,
                    BezelStyle = NSTextFieldBezelStyle.Rounded,
                };
            field.Editable = true;
            field.Selectable = true;
            field.FocusRingType = NSFocusRingType.None;
            field.ControlSize = NSControlSize.Regular;
            field.Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);
            field.Alignment = alignment;
            field.LineBreakMode = NSLineBreakMode.Clipping;
            field.TranslatesAutoresizingMaskIntoConstraints = false;
            field.AddConstraint(NSLayoutConstraint.Create(
                field, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 22));
            return field;
        }

        protected NSTextField CreateNumericField(
            nfloat width,
            bool usesParameterStyle = false)
        {
            var field = CreateTextField(
                usesParameterStyle,
                NSTextAlignment.Right);
            field.AddConstraint(NSLayoutConstraint.Create(
                field, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, width));
            return field;
        }

        protected NSTextField CreateExpandingNumericField(
            bool usesParameterStyle = false)
        {
            var field = CreateTextField(
                usesParameterStyle,
                NSTextAlignment.Right);
            field.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            field.SetContentCompressionResistancePriority(
                1,
                NSLayoutConstraintOrientation.Horizontal);
            return field;
        }

        protected NSView CreateExpansionSpacer()
        {
            var spacer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            spacer.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            spacer.SetContentCompressionResistancePriority(
                1,
                NSLayoutConstraintOrientation.Horizontal);
            return spacer;
        }

        protected void AddUnitOrExpansionSpacer(NSStackView row, string unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
                row.AddArrangedSubview(CreateExpansionSpacer());
            else
                row.AddArrangedSubview(CreateUnitLabel(unit));
        }

        protected void AddFullWidthArrangedSubview(NSView view)
        {
            view.TranslatesAutoresizingMaskIntoConstraints = false;
            AddArrangedSubview(view);
            AddConstraint(NSLayoutConstraint.Create(
                view,
                NSLayoutAttribute.Width,
                NSLayoutRelation.Equal,
                this,
                NSLayoutAttribute.Width,
                1,
                0));
        }

        protected void AddHorizontallyInsetArrangedSubview(
            NSView view,
            nfloat horizontalInset)
        {
            view.TranslatesAutoresizingMaskIntoConstraints = false;
            AddArrangedSubview(view);
            AddConstraint(NSLayoutConstraint.Create(
                view,
                NSLayoutAttribute.Width,
                NSLayoutRelation.Equal,
                this,
                NSLayoutAttribute.Width,
                1,
                -2 * horizontalInset));
        }

        protected void AddVerticalPadding(nfloat height)
        {
            var spacer = new NSView
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            spacer.AddConstraint(NSLayoutConstraint.Create(
                spacer,
                NSLayoutAttribute.Height,
                NSLayoutRelation.Equal,
                1,
                height));
            AddFullWidthArrangedSubview(spacer);
        }

        void AddEdgeToEdgeArrangedSubview(NSView view)
        {
            view.TranslatesAutoresizingMaskIntoConstraints = false;
            AddArrangedSubview(view);
            AddConstraint(NSLayoutConstraint.Create(
                view,
                NSLayoutAttribute.Width,
                NSLayoutRelation.Equal,
                this,
                NSLayoutAttribute.Width,
                1,
                0));
        }

        protected void SetFixedContentHeight(nfloat contentHeight)
        {
            fixedItemHeight = contentHeight
                + (showsDivider || reservesDividerSpace ? 8 : 0);
            AddConstraint(NSLayoutConstraint.Create(
                this,
                NSLayoutAttribute.Height,
                NSLayoutRelation.Equal,
                1,
                fixedItemHeight));
            SetContentHuggingPriorityForOrientation(
                1000,
                NSLayoutConstraintOrientation.Vertical);
            SetContentCompressionResistancePriority(
                1000,
                NSLayoutConstraintOrientation.Vertical);
        }

        protected void UseContentDrivenHeight()
        {
            fixedItemHeight = NSView.NoIntrinsicMetric;
            SetContentHuggingPriorityForOrientation(
                1000,
                NSLayoutConstraintOrientation.Vertical);
            SetContentCompressionResistancePriority(
                1000,
                NSLayoutConstraintOrientation.Vertical);
            InvalidateIntrinsicContentSize();
        }

        protected void FinishItem(bool reserveDividerSpace = false)
        {
            reservesDividerSpace = reserveDividerSpace;
            if (!showsDivider && !reservesDividerSpace) return;

            var container = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            container.AddConstraint(NSLayoutConstraint.Create(
                container,
                NSLayoutAttribute.Height,
                NSLayoutRelation.Equal,
                1,
                5));

            if (showsDivider)
            {
                var divider = new NSBox
                {
                    BoxType = NSBoxType.NSBoxSeparator,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                container.AddSubview(divider);
                container.AddConstraints(new[]
                {
                    NSLayoutConstraint.Create(
                        divider, NSLayoutAttribute.Leading, NSLayoutRelation.Equal,
                        container, NSLayoutAttribute.Leading, 1, 0),
                    NSLayoutConstraint.Create(
                        divider, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal,
                        container, NSLayoutAttribute.Trailing, 1, 0),
                    NSLayoutConstraint.Create(
                        divider, NSLayoutAttribute.CenterY, NSLayoutRelation.Equal,
                        container, NSLayoutAttribute.CenterY, 1, 0),
                });
            }
            AddEdgeToEdgeArrangedSubview(container);
        }

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();

            if (hoverTrackingArea != null)
            {
                RemoveTrackingArea(hoverTrackingArea);
                hoverTrackingArea = null;
            }

            if (!highlightsOnHover) return;

            hoverTrackingArea = new NSTrackingArea(
                Bounds,
                NSTrackingAreaOptions.ActiveInKeyWindow
                    | NSTrackingAreaOptions.InVisibleRect
                    | NSTrackingAreaOptions.MouseEnteredAndExited,
                this,
                null);
            AddTrackingArea(hoverTrackingArea);
        }

        public override void MouseEntered(NSEvent theEvent)
        {
            base.MouseEntered(theEvent);
            if (!highlightsOnHover) return;
            SetHovered(true);
        }

        public override void MouseExited(NSEvent theEvent)
        {
            base.MouseExited(theEvent);
            if (!highlightsOnHover) return;
            SetHovered(false);
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            if (isHovered)
            {
                NSColor.Label.ColorWithAlphaComponent(0.045f).SetFill();
                var dividerSpace = showsDivider || reservesDividerSpace ? 8 : 0;
                var hoverBounds = new CGRect(
                    0,
                    dividerSpace,
                    Bounds.Width,
                    Math.Max(0, Bounds.Height - dividerSpace));
                NSBezierPath.FromRoundedRect(hoverBounds, 5, 5).Fill();
            }

            base.DrawRect(dirtyRect);
        }

        void SetHovered(bool hovered)
        {
            if (isHovered == hovered) return;

            isHovered = hovered;
            NeedsDisplay = true;
        }

        public override CGSize IntrinsicContentSize
        {
            get
            {
                if (fixedItemHeight == NSView.NoIntrinsicMetric)
                {
                    var intrinsicSize = base.IntrinsicContentSize;
                    return new CGSize(NSView.NoIntrinsicMetric, intrinsicSize.Height);
                }

                return new CGSize(NSView.NoIntrinsicMetric, fixedItemHeight);
            }
        }

        protected static NSImage ResizeTemplateSymbol(NSImage image)
        {
            if (image == null) return null;

            var targetFrame = new CGRect(0, 0, 13, 13);
            var result = new NSImage(targetFrame.Size) { Template = true };
            result.LockFocus();
            image.Draw(
                targetFrame,
                new CGRect(CGPoint.Empty, image.Size),
                NSCompositingOperation.SourceOver,
                1);
            result.UnlockFocus();
            return result;
        }
    }

    internal sealed class AnalysisParameterItemView
        : AnalysisInspectorItemView, IAnalysisInspectorEditable
    {
        readonly Parameter parameter;
        readonly AnalysisParameterDraft draft;
        readonly NSTextField input;
        readonly NSSwitch lockSwitch;
        readonly NSImageView lockImage;
        readonly NSColor defaultTextColor;

        public event EventHandler FittingDimensionsChanged;

        public bool HasValidInput => !IsApplicable || draft.IsValid;
        public bool IsApplicable => draft.IsApplicable;

        public AnalysisParameterItemView(
            Parameter parameter,
            AnalysisParameterDraft draft,
            IDictionary<AttributeKey, AnalysisOptionDraft> options,
            bool showSiteIndex)
            : base(false, highlightsOnHover: true)
        {
            this.parameter = parameter;
            this.draft = draft;

            var correctionFactor =
                parameter.Key == ParameterType.Nvalue1
                && options.TryGetValue(AttributeKey.UseSyringeActiveFraction, out var syringe)
                && syringe.Option.BoolValue;
            var enabled = AnalysisInspectorDisplayCatalog.IsParameterEnabled(parameter.Key, options);
            draft.IsApplicable = enabled;
            draft.AutomaticValue = parameter.Value;

            AddVerticalPadding(2);
            var header = CreateHorizontalRow();
            var title = correctionFactor
                ? "Correction Factor"
                : parameter.Key.GetProperties().Description;
            var symbol = MacStrings.ParameterSymbol(
                parameter.Key,
                showSiteIndex,
                correctionFactor);
            var unit = AnalysisInspectorDisplayCatalog.ParameterUnit(parameter.Key);
            var label = CreateTitle(
                title,
                null,
                enabled,
                medium: true);
            label.ToolTip = "Initial value for "
                + parameter.Key.GetProperties().Description + ".";
            label.SetContentHuggingPriorityForOrientation(
                1000,
                NSLayoutConstraintOrientation.Horizontal);
            header.AddArrangedSubview(label);
            var metadataLabel = CreateParameterMetadata(symbol, unit, enabled);
            metadataLabel.ToolTip = label.ToolTip;
            header.AddArrangedSubview(metadataLabel);
            AddHorizontallyInsetArrangedSubview(header, 5);

            lockImage = new NSImageView
            {
                ImageScaling = NSImageScale.ProportionallyDown,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            lockImage.AddConstraint(NSLayoutConstraint.Create(
                lockImage,
                NSLayoutAttribute.Width,
                NSLayoutRelation.Equal,
                1,
                15));
            lockImage.AddConstraint(NSLayoutConstraint.Create(
                lockImage,
                NSLayoutAttribute.Height,
                NSLayoutRelation.Equal,
                1,
                15));
            lockImage.SetContentHuggingPriorityForOrientation(
                1000,
                NSLayoutConstraintOrientation.Horizontal);

            lockSwitch = new NSSwitch
            {
                State = (int)(draft.Locked
                    ? NSCellStateValue.On
                    : NSCellStateValue.Off),
                ControlSize = NSControlSize.Mini,
                Enabled = enabled,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            lockSwitch.SetContentHuggingPriorityForOrientation(
                1000,
                NSLayoutConstraintOrientation.Horizontal);
            lockSwitch.Activated += LockChanged;
            UpdateLockAppearance();
            header.AddArrangedSubview(lockImage);
            header.AddArrangedSubview(lockSwitch);

            var editor = CreateHorizontalRow();
            input = CreateExpandingNumericField(
                usesParameterStyle: true);
            input.Enabled = enabled;
            if (draft.RawText == null)
            {
                draft.RawText = draft.HasOverride
                    ? AnalysisInspectorDisplayCatalog.FormatParameter(
                        parameter.Key,
                        draft.InternalValue)
                    : string.Empty;
            }
            input.StringValue = draft.RawText;
            input.PlaceholderString = "Auto: "
                + AnalysisInspectorDisplayCatalog.FormatParameter(
                    parameter.Key,
                    parameter.Value);
            input.ToolTip =
                "Enter an initial value for fitting. Leave this field empty "
                + "to calculate one automatically.";
            input.Changed += InputChanged;
            defaultTextColor = input.TextColor;
            editor.AddArrangedSubview(input);
            AddHorizontallyInsetArrangedSubview(editor, 5);
            AddVerticalPadding(2);

            ValidateInput();
            SetFixedContentHeight(53);
        }

        void InputChanged(object sender, EventArgs e)
        {
            var wasLocked = draft.Locked;
            draft.RawText = input.StringValue;

            if (string.IsNullOrWhiteSpace(draft.RawText))
            {
                draft.HasOverride = false;
                draft.Locked = false;
                draft.IsValid = true;
                lockSwitch.State = (int)NSCellStateValue.Off;
                input.TextColor = defaultTextColor;
                UpdateLockAppearance();
                if (wasLocked)
                    FittingDimensionsChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            draft.IsValid = AnalysisInspectorDisplayCatalog.TryParseParameter(
                parameter,
                draft.RawText,
                out var value);
            if (draft.IsValid)
            {
                draft.InternalValue = value;
                draft.HasOverride = true;
            }

            input.TextColor = draft.IsValid ? defaultTextColor : NSColor.SystemRed;
        }

        void ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(draft.RawText))
            {
                draft.IsValid = true;
                input.TextColor = defaultTextColor;
                return;
            }

            draft.IsValid = AnalysisInspectorDisplayCatalog.TryParseParameter(
                parameter,
                draft.RawText,
                out var value);
            if (draft.IsValid)
            {
                draft.InternalValue = value;
                draft.HasOverride = true;
            }
            input.TextColor = draft.IsValid ? defaultTextColor : NSColor.SystemRed;
        }

        void LockChanged(object sender, EventArgs e)
        {
            var locked = lockSwitch.State == (int)NSCellStateValue.On;
            var wasLocked = draft.Locked;
            if (locked && !draft.HasOverride)
            {
                draft.InternalValue = draft.AutomaticValue;
                draft.HasOverride = true;
                draft.RawText = AnalysisInspectorDisplayCatalog.FormatParameter(
                    parameter.Key,
                    draft.InternalValue);
                input.StringValue = draft.RawText;
                draft.IsValid = true;
                input.TextColor = defaultTextColor;
            }

            draft.Locked = locked;
            UpdateLockAppearance();
            if (wasLocked != locked)
                FittingDimensionsChanged?.Invoke(this, EventArgs.Empty);
        }

        void UpdateLockAppearance()
        {
            var locked = lockSwitch.State == (int)NSCellStateValue.On;
            lockImage.Image = ResizeTemplateSymbol(NSImage.GetSystemSymbol(
                locked ? "lock.fill" : "lock.open.fill",
                null));
            var toolTip = locked
                ? "Allow this parameter to vary during fitting."
                : "Keep this parameter fixed at its current value during fitting.";
            lockImage.ToolTip = toolTip;
            lockSwitch.ToolTip = toolTip;
            lockSwitch.AccessibilityTitle = toolTip;
        }

        public void FocusInput()
        {
            input?.Window?.MakeFirstResponder(input);
        }

    }

    internal sealed class AnalysisConstraintItemView : AnalysisInspectorItemView
    {
        readonly IReadOnlyList<VariableConstraint> options;
        readonly NSPopUpButton popup;
        readonly AnalysisInspectorDraft draft;
        readonly ParameterType key;

        public event EventHandler StructureChanged;

        public AnalysisConstraintItemView(
            ParameterType key,
            IReadOnlyList<VariableConstraint> options,
            AnalysisInspectorDraft draft,
            bool showSiteIndex,
            bool showsDivider)
            : base(showsDivider)
        {
            this.key = key;
            this.options = options;
            this.draft = draft;

            var row = CreateHorizontalRow();
            var label = CreateTitle(
                key.GetProperties().Name,
                null,
                medium: true);
            label.ToolTip = "Choose how this parameter varies between experiments.";
            row.AddArrangedSubview(label);

            popup = new NSPopUpButton(CGRect.Empty, false)
            {
                ControlSize = NSControlSize.Large,
                Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                BezelStyle = NSBezelStyle.TexturedRounded,
                ToolTip = label.ToolTip,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            popup.AddItems(options
                .Select(AnalysisInspectorDisplayCatalog.ConstraintTitle)
                .ToArray());
            popup.AddConstraint(NSLayoutConstraint.Create(
                popup,
                NSLayoutAttribute.Width,
                NSLayoutRelation.Equal,
                1,
                180));
            popup.SetContentHuggingPriorityForOrientation(
                1000,
                NSLayoutConstraintOrientation.Horizontal);

            var selected = draft.Constraints.TryGetValue(key, out var value)
                ? value
                : VariableConstraint.None;
            var selectedIndex = options.ToList().IndexOf(selected);
            popup.SelectItem(selectedIndex >= 0 ? selectedIndex : 0);
            popup.Activated += PopupChanged;
            row.AddArrangedSubview(popup);
            AddFullWidthArrangedSubview(row);

            FinishItem();
            SetFixedContentHeight(30);
        }

        void PopupChanged(object sender, EventArgs e)
        {
            var index = (int)popup.IndexOfSelectedItem;
            if (index < 0 || index >= options.Count) return;

            draft.Constraints[key] = options[index];
            StructureChanged?.Invoke(this, EventArgs.Empty);
        }

    }

    internal sealed class AnalysisOptionItemView
        : AnalysisInspectorItemView, IAnalysisInspectorEditable
    {
        readonly AnalysisOptionDraft draft;
        readonly IDictionary<AttributeKey, AnalysisOptionDraft> allOptions;
        readonly Func<AttributeKey, bool> attributesAvailable;
        NSTextField valueField;
        NSTextField errorField;
        NSTextField validationLabel;
        NSButton fromAttributesButton;
        NSColor defaultTextColor;
        bool allowsFromAttributes;
        nfloat contentHeight;

        public event EventHandler StructureChanged;

        public bool HasValidInput => !IsApplicable || draft.IsValid;
        public bool IsApplicable => draft.IsApplicable;

        public AnalysisOptionItemView(
            AnalysisOptionDraft draft,
            IDictionary<AttributeKey, AnalysisOptionDraft> allOptions,
            Func<AttributeKey, bool> attributesAvailable,
            bool showsDivider)
            : base(showsDivider)
        {
            this.draft = draft;
            this.allOptions = allOptions;
            this.attributesAvailable = attributesAvailable;

            var enabled = AnalysisInspectorDisplayCatalog.IsOptionEnabled(
                draft.Option.Key,
                allOptions);
            draft.IsApplicable = enabled;

            if (draft.Option.Key == AttributeKey.NumberOfSites1
                || draft.Option.Key == AttributeKey.NumberOfSites2)
            {
                BuildStoichiometry(enabled);
            }
            else
            {
                switch (draft.Option.Key.GetProperties().Type)
                {
                    case ExperimentAttribute.AttributeType.Bool:
                        BuildBoolean(enabled);
                        break;
                    case ExperimentAttribute.AttributeType.Enum:
                        BuildEnum(enabled);
                        break;
                    case ExperimentAttribute.AttributeType.Int:
                        BuildInteger(enabled);
                        break;
                    case ExperimentAttribute.AttributeType.Double:
                        BuildDouble(enabled);
                        break;
                    case ExperimentAttribute.AttributeType.Parameter:
                    case ExperimentAttribute.AttributeType.ParameterAffinity:
                    case ExperimentAttribute.AttributeType.ParameterConcentration:
                        BuildParameter(enabled);
                        break;
                    case ExperimentAttribute.AttributeType.String:
                        BuildString(enabled);
                        break;
                    case ExperimentAttribute.AttributeType.ReferenceExperiment:
                        BuildExperimentReference(enabled);
                        break;
                    default:
                        BuildReadOnly();
                        break;
                }
            }

            var hasExplanation = AddExplanation();
            FinishItem();
            if (hasExplanation)
                UseContentDrivenHeight();
            else
                SetFixedContentHeight(contentHeight);
        }

        bool AddExplanation()
        {
            var properties = draft.Option.Key.GetProperties();
            var explanation = properties?.ToolTip;
            if (string.IsNullOrWhiteSpace(explanation)
                || explanation == properties.Type.ToString())
                return false;

            AddFullWidthArrangedSubview(CreateExplanationLabel(explanation));
            return true;
        }

        void BuildBoolean(bool enabled)
        {
            contentHeight = 22;
            var row = CreateHorizontalRow();

            if (draft.Option.Key == AttributeKey.UseSyringeActiveFraction)
            {
                var label = CreateTitle(
                    AnalysisInspectorDisplayCatalog.OptionTitle(draft.Option),
                    null,
                    enabled,
                    medium: true);
                label.ToolTip = draft.Option.Key.GetProperties().ToolTip;
                label.SetContentHuggingPriorityForOrientation(
                    1000,
                    NSLayoutConstraintOrientation.Horizontal);
                row.AddArrangedSubview(label);

                var metadata = CreateParameterMetadata(
                    AnalysisInspectorDisplayCatalog.OptionSymbol(draft.Option.Key),
                    AnalysisInspectorDisplayCatalog.OptionUnit(draft.Option.Key),
                    enabled);
                metadata.ToolTip = label.ToolTip;
                row.AddArrangedSubview(metadata);

                var toggle = new NSSwitch
                {
                    State = (int)(draft.Option.BoolValue
                        ? NSCellStateValue.On
                        : NSCellStateValue.Off),
                    ControlSize = NSControlSize.Small,
                    ToolTip = label.ToolTip,
                    Enabled = enabled,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                toggle.SetContentHuggingPriorityForOrientation(
                    1000,
                    NSLayoutConstraintOrientation.Horizontal);
                toggle.Activated += (_, _) =>
                {
                    draft.Option.BoolValue = toggle.State == (int)NSCellStateValue.On;
                    draft.IsValid = true;
                    StructureChanged?.Invoke(this, EventArgs.Empty);
                };
                row.AddArrangedSubview(toggle);
            }
            else
            {
                var checkbox = new NSButton
                {
                    Title = AnalysisInspectorDisplayCatalog.OptionTitle(draft.Option),
                    State = draft.Option.BoolValue
                        ? NSCellStateValue.On
                        : NSCellStateValue.Off,
                    ControlSize = NSControlSize.Regular,
                    Font = NSFont.SystemFontOfSize(
                        NSFont.SystemFontSize,
                        NSFontWeight.Medium),
                    ToolTip = draft.Option.Key.GetProperties().ToolTip,
                    Enabled = enabled,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                checkbox.SetButtonType(NSButtonType.Switch);
                checkbox.SetContentHuggingPriorityForOrientation(
                    249,
                    NSLayoutConstraintOrientation.Horizontal);
                checkbox.Activated += (_, _) =>
                {
                    draft.Option.BoolValue = checkbox.State == NSCellStateValue.On;
                    draft.IsValid = true;
                    if (draft.Option.Key == AttributeKey.LockDuplicateParameter)
                        StructureChanged?.Invoke(this, EventArgs.Empty);
                };
                row.AddArrangedSubview(checkbox);
            }

            AddFullWidthArrangedSubview(row);
        }

        void BuildStoichiometry(bool enabled)
        {
            contentHeight = 43;
            AddTitleRow(enabled);

            var row = CreateHorizontalRow();
            var popup = new NSPopUpButton(CGRect.Empty, false)
            {
                ControlSize = NSControlSize.Regular,
                Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                BezelStyle = NSBezelStyle.TexturedRounded,
                Enabled = enabled,
                ToolTip = enabled
                    ? draft.Option.Key.GetProperties().ToolTip
                    : AnalysisInspectorDisplayCatalog.DisabledOptionToolTip(
                        draft.Option.Key,
                        allOptions),
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            StoichiometryPopupBuilder.Populate(popup);
            StoichiometryPopupBuilder.Select(
                popup,
                draft.Option.DoubleValue > 0
                    ? draft.Option.DoubleValue
                    : Math.Max(1, draft.Option.IntValue));
            popup.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            popup.Activated += (_, _) =>
            {
                draft.Option.DoubleValue =
                    StoichiometryPopupBuilder.GetSelected(popup).Factor;
                draft.IsValid = true;
            };
            row.AddArrangedSubview(popup);
            AddFullWidthArrangedSubview(row);
        }

        void BuildEnum(bool enabled)
        {
            contentHeight = 43;
            AddTitleRow(enabled);
            var row = CreateHorizontalRow();
            var popup = new NSPopUpButton(CGRect.Empty, false)
            {
                ControlSize = NSControlSize.Regular,
                Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                BezelStyle = NSBezelStyle.TexturedRounded,
                Enabled = enabled,
                ToolTip = draft.Option.Key.GetProperties().ToolTip,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            var enumOptions = draft.Option.EnumOptions.ToList();
            popup.AddItems(enumOptions.Select(option => option.Item2).ToArray());
            var selected = enumOptions.FindIndex(option => option.Item1 == draft.Option.IntValue);
            popup.SelectItem(selected >= 0 ? selected : 0);
            popup.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            popup.Activated += (_, _) =>
            {
                var index = (int)popup.IndexOfSelectedItem;
                if (index >= 0 && index < enumOptions.Count)
                    draft.Option.IntValue = enumOptions[index].Item1;
            };
            row.AddArrangedSubview(popup);
            AddFullWidthArrangedSubview(row);
        }

        void BuildInteger(bool enabled)
        {
            BuildSingleNumeric(
                enabled,
                draft.Option.IntValue.ToString(CultureInfo.CurrentCulture),
                value =>
                {
                    if (Math.Abs(value - Math.Round(value)) > 1e-9) return false;
                    draft.Option.IntValue = (int)Math.Round(value);
                    return true;
                });
        }

        void BuildDouble(bool enabled)
        {
            BuildSingleNumeric(
                enabled,
                AnalysisInspectorDisplayCatalog.FormatNumber(draft.Option.DoubleValue),
                value =>
                {
                    draft.Option.DoubleValue = value;
                    return !double.IsNaN(value) && !double.IsInfinity(value);
                });
        }

        void BuildSingleNumeric(
            bool enabled,
            string initialValue,
            Func<double, bool> apply)
        {
            contentHeight = 43;
            AddTitleRow(enabled);
            var row = CreateHorizontalRow();
            valueField = CreateExpandingNumericField(
                usesParameterStyle: true);
            valueField.Enabled = enabled;
            if (draft.ValueText == null) draft.ValueText = initialValue;
            valueField.StringValue = draft.ValueText;
            defaultTextColor = valueField.TextColor;
            valueField.Changed += (_, _) =>
            {
                draft.ValueText = valueField.StringValue;
                draft.IsValid = AnalysisInspectorDisplayCatalog.TryParseNumber(
                    draft.ValueText,
                    out var value)
                    && apply(value);
                UpdateNumericValidation();
            };
            row.AddArrangedSubview(valueField);
            AddUnitOrExpansionSpacer(row, string.Empty);
            AddFullWidthArrangedSubview(row);
        }

        void BuildParameter(bool enabled)
        {
            allowsFromAttributes =
                draft.Option.Key == AttributeKey.PreboundLigandConc
                || draft.Option.Key == AttributeKey.EquilibriumConstant
                || draft.Option.Key == AttributeKey.Percentage;
            contentHeight = allowsFromAttributes ? 85 : 43;
            AddTitleRow(enabled);

            if (allowsFromAttributes)
            {
                var sourceRow = CreateHorizontalRow();
                fromAttributesButton = new NSButton
                {
                    Title = "Use experiment attribute",
                    State = draft.Option.BoolValue
                        ? NSCellStateValue.On
                        : NSCellStateValue.Off,
                    ControlSize = NSControlSize.Regular,
                    Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                    ToolTip = "Use the value stored on each experiment.",
                    Enabled = enabled,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                fromAttributesButton.SetButtonType(NSButtonType.Switch);
                fromAttributesButton.SetContentHuggingPriorityForOrientation(
                    249,
                    NSLayoutConstraintOrientation.Horizontal);
                fromAttributesButton.Activated += (_, _) =>
                {
                    draft.Option.BoolValue =
                        fromAttributesButton.State == NSCellStateValue.On;
                    UpdateParameterOptionState();
                };
                sourceRow.AddArrangedSubview(fromAttributesButton);
                AddFullWidthArrangedSubview(sourceRow);
            }

            var editor = CreateHorizontalRow();
            valueField = CreateExpandingNumericField(
                usesParameterStyle: true);
            errorField = CreateNumericField(82, usesParameterStyle: true);

            var display = AnalysisInspectorDisplayCatalog.OptionDisplayValue(draft.Option);
            if (draft.ValueText == null)
                draft.ValueText = AnalysisInspectorDisplayCatalog.FormatNumber(display.value);
            if (draft.ErrorText == null)
                draft.ErrorText = display.error == 0
                    ? string.Empty
                    : AnalysisInspectorDisplayCatalog.FormatNumber(display.error);

            valueField.StringValue = draft.ValueText;
            errorField.StringValue = draft.ErrorText;
            valueField.PlaceholderString = "Value";
            errorField.PlaceholderString = "SD";
            defaultTextColor = valueField.TextColor;
            valueField.Changed += ParameterValueChanged;
            errorField.Changed += ParameterValueChanged;

            editor.AddArrangedSubview(valueField);
            var plusMinus = CreateUnitLabel("±");
            plusMinus.SetContentHuggingPriorityForOrientation(
                1000,
                NSLayoutConstraintOrientation.Horizontal);
            editor.AddArrangedSubview(plusMinus);
            editor.AddArrangedSubview(errorField);
            AddFullWidthArrangedSubview(editor);

            if (allowsFromAttributes)
            {
                validationLabel = new NSTextField
                {
                    StringValue = "Missing from one or more experiments",
                    Hidden = true,
                    Bordered = false,
                    Editable = false,
                    DrawsBackground = false,
                    TextColor = NSColor.SystemRed,
                    Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                    ControlSize = NSControlSize.Small,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                validationLabel.AddConstraint(NSLayoutConstraint.Create(
                    validationLabel,
                    NSLayoutAttribute.Height,
                    NSLayoutRelation.Equal,
                    1,
                    14));
                validationLabel.SetContentHuggingPriorityForOrientation(
                    249,
                    NSLayoutConstraintOrientation.Horizontal);
                AddFullWidthArrangedSubview(validationLabel);
            }

            UpdateParameterOptionState();
        }

        void ParameterValueChanged(object sender, EventArgs e)
        {
            draft.ValueText = valueField.StringValue;
            draft.ErrorText = errorField.StringValue;
            draft.IsValid = AnalysisInspectorDisplayCatalog.TrySetOptionParameter(
                draft,
                draft.ValueText,
                draft.ErrorText);
            UpdateNumericValidation();
        }

        void UpdateParameterOptionState()
        {
            var fromAttributes =
                allowsFromAttributes
                && fromAttributesButton?.State == NSCellStateValue.On;
            var manualEnabled = draft.IsApplicable && !fromAttributes;
            if (valueField != null) valueField.Enabled = manualEnabled;
            if (errorField != null) errorField.Enabled = manualEnabled;

            if (fromAttributes)
            {
                var available = attributesAvailable?.Invoke(draft.Option.Key) ?? false;
                draft.IsValid = available;
                validationLabel.Hidden = available;      
            }
            else
            {
                if (validationLabel != null)
                    validationLabel.StringValue = string.Empty;
                draft.IsValid = AnalysisInspectorDisplayCatalog.TrySetOptionParameter(
                    draft,
                    draft.ValueText,
                    draft.ErrorText);
            }

            UpdateNumericValidation();
            NeedsLayout = true;
        }

        void UpdateNumericValidation()
        {
            if (valueField == null) return;

            var validOrInactive = !draft.IsApplicable
                || (allowsFromAttributes && draft.Option.BoolValue)
                || draft.IsValid;
            valueField.TextColor = validOrInactive
                ? defaultTextColor
                : NSColor.SystemRed;
            if (errorField != null)
                errorField.TextColor = validOrInactive
                    ? defaultTextColor
                    : NSColor.SystemRed;
        }

        void BuildString(bool enabled)
        {
            contentHeight = 43;
            AddTitleRow(enabled);
            var row = CreateHorizontalRow();
            var field = CreateTextField(usesParameterStyle: true);
            field.StringValue = draft.Option.StringValue ?? string.Empty;
            field.Enabled = enabled;
            field.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            field.Changed += (_, _) => draft.Option.StringValue = field.StringValue;
            row.AddArrangedSubview(field);
            AddFullWidthArrangedSubview(row);
        }

        void BuildExperimentReference(bool enabled)
        {
            contentHeight = 43;
            AddTitleRow(enabled);
            var row = CreateHorizontalRow();
            var popup = new NSPopUpButton(CGRect.Empty, false)
            {
                ControlSize = NSControlSize.Regular,
                Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                BezelStyle = NSBezelStyle.TexturedRounded,
                Enabled = enabled,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            var references = draft.Option.ExperimentReferenceOptions.ToList();
            popup.AddItems(references.Select(option => option.Item2).ToArray());
            var selected = references.FindIndex(option => option.Item4 == draft.Option.StringValue);
            popup.SelectItem(selected >= 0 ? selected : 0);
            popup.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            popup.Activated += (_, _) =>
            {
                var index = (int)popup.IndexOfSelectedItem;
                if (index >= 0 && index < references.Count)
                    draft.Option.StringValue = references[index].Item4;
            };
            row.AddArrangedSubview(popup);
            AddFullWidthArrangedSubview(row);
        }

        void BuildReadOnly()
        {
            contentHeight = 43;
            AddTitleRow(false);
            var row = CreateHorizontalRow();
            var label = CreateUnitLabel("Read-only: " + draft.Option.GetDisplayValue());
            row.AddArrangedSubview(label);
            AddFullWidthArrangedSubview(row);
        }

        void AddTitleRow(bool enabled)
        {
            var row = CreateHorizontalRow();
            var label = CreateTitle(
                AnalysisInspectorDisplayCatalog.OptionTitle(draft.Option),
                null,
                enabled,
                medium: true);
            label.ToolTip = enabled
                ? draft.Option.Key.GetProperties().ToolTip
                : AnalysisInspectorDisplayCatalog.DisabledOptionToolTip(
                    draft.Option.Key,
                allOptions);
            row.AddArrangedSubview(label);

            var symbol = AnalysisInspectorDisplayCatalog.OptionSymbol(
                draft.Option.Key);
            var unit = AnalysisInspectorDisplayCatalog.OptionUnit(
                draft.Option.Key);
            if (!string.IsNullOrWhiteSpace(symbol)
                || !string.IsNullOrWhiteSpace(unit))
            {
                label.SetContentHuggingPriorityForOrientation(
                    1000,
                    NSLayoutConstraintOrientation.Horizontal);
                var metadata = CreateParameterMetadata(symbol, unit, enabled);
                metadata.ToolTip = label.ToolTip;
                row.AddArrangedSubview(metadata);
            }

            AddFullWidthArrangedSubview(row);
        }

        public void FocusInput()
        {
            if (fromAttributesButton?.State == NSCellStateValue.On)
                fromAttributesButton.Window?.MakeFirstResponder(fromAttributesButton);
            else
                valueField?.Window?.MakeFirstResponder(valueField);
        }

    }

    internal static class AnalysisInspectorRowFactory
    {
        public static AnalysisParameterItemView Parameter(
            Parameter parameter,
            AnalysisParameterDraft draft,
            IDictionary<AttributeKey, AnalysisOptionDraft> options,
            bool showSiteIndex)
        {
            return new AnalysisParameterItemView(
                parameter,
                draft,
                options,
                showSiteIndex);
        }

        public static AnalysisConstraintItemView Constraint(
            ParameterType key,
            IReadOnlyList<VariableConstraint> options,
            AnalysisInspectorDraft draft,
            bool showSiteIndex,
            bool showsDivider)
        {
            return new AnalysisConstraintItemView(
                key,
                options,
                draft,
                showSiteIndex,
                showsDivider);
        }

        public static AnalysisOptionItemView Option(
            AnalysisOptionDraft draft,
            IDictionary<AttributeKey, AnalysisOptionDraft> options,
            Func<AttributeKey, bool> attributesAvailable,
            bool showsDivider)
        {
            return new AnalysisOptionItemView(
                draft,
                options,
                attributesAvailable,
                showsDivider);
        }
    }
}
