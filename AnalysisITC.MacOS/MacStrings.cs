using AppKit;
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
    public static class MacStrings
	{
        const float SuperSubOffset = 3;

        static NSMutableAttributedString PlainText(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            s.AddAttributes(new NSStringAttributes { Font = font }, new NSRange(0, s.Length));
            return s;
        }

        static NSMutableAttributedString CursiveText(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            s.AddAttributes(new NSStringAttributes { Font = font }, new NSRange(0, s.Length));
            s.ApplyFontTraits(NSFontTraitMask.Italic, new NSRange(0, s.Length));
            return s;
        }

        static NSMutableAttributedString BoldText(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            s.AddAttributes(new NSStringAttributes { Font = font }, new NSRange(0, s.Length));
            s.ApplyFontTraits(NSFontTraitMask.Bold, new NSRange(0, s.Length));
            return s;
        }

        static NSMutableAttributedString Header1Text(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            font = NSFont.FromDescription(font.FontDescriptor, font.PointSize * 1.2f);
            var paragraphStyle = new NSMutableParagraphStyle();
            paragraphStyle.ParagraphSpacingBefore = 5;
            s.AddAttributes(new NSStringAttributes { Font = font, ParagraphStyle = paragraphStyle }, new NSRange(0, s.Length));
            s.ApplyFontTraits(NSFontTraitMask.Bold, new NSRange(0, s.Length));
            return s;
        }

        static NSMutableAttributedString Header2Text(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            font = NSFont.FromDescription(font.FontDescriptor, font.PointSize * 1.8f);
            var paragraphStyle = new NSMutableParagraphStyle();
            paragraphStyle.ParagraphSpacingBefore = 5;
            s.AddAttributes(new NSStringAttributes { Font = font, ParagraphStyle = paragraphStyle }, new NSRange(0, s.Length));
            s.ApplyFontTraits(NSFontTraitMask.Bold, new NSRange(0, s.Length));
            return s;
        }

        static NSMutableAttributedString SmallText(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            font = NSFont.FromDescription(font.FontDescriptor, font.PointSize * 0.8f);
            s.AddAttributes(new NSStringAttributes { Font = font }, new NSRange(0, s.Length));
            return s;
        }

        static NSMutableAttributedString SubscriptText(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            font = NSFont.FromFontName(font.FontName, font.PointSize * 0.7f);
            s.AddAttributes(new NSStringAttributes { Font = font }, new NSRange(0, s.Length));
            var attributes = new NSMutableDictionary();
            var subscriptOffset = new NSNumber(-SuperSubOffset);
            var range = new NSRange(0, s.Length);
            attributes.Add(NSStringAttributeKey.BaselineOffset, subscriptOffset);

            s.AddAttributes(attributes, range);

            return s;
        }

        static NSMutableAttributedString SubscriptText2(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            font = NSFont.FromFontName(font.FontName, font.PointSize * 0.7f);
            s.AddAttributes(new NSStringAttributes { Font = font }, new NSRange(0, s.Length));
            s.AddAttribute(NSStringAttributeKey.Superscript, NSNumber.FromInt32(-1), new NSRange(0, s.Length));
            return s;
        }

        static NSMutableAttributedString SuperscriptText(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            font = NSFont.FromFontName(font.FontName, font.PointSize * 0.7f);
            s.AddAttributes(new NSStringAttributes { Font = font }, new NSRange(0, s.Length));
            var attributes = new NSMutableDictionary();
            var subscriptOffset = new NSNumber(SuperSubOffset);
            var range = new NSRange(0, s.Length);
            attributes.Add(NSStringAttributeKey.BaselineOffset, subscriptOffset);
            s.AddAttributes(attributes, range);

            return s;
        }

        static NSMutableAttributedString SuperscriptText2(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            font = NSFont.FromFontName(font.FontName, font.PointSize * 0.7f);
            s.AddAttributes(new NSStringAttributes { Font = font }, new NSRange(0, s.Length));
            s.AddAttribute(NSStringAttributeKey.Superscript, NSNumber.FromInt32(1), new NSRange(0, s.Length));
            return s;
        }

        public static NSMutableAttributedString CursiveSubscript(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            s.AddAttributes(new NSStringAttributes { Font = font, ForegroundColor = NSColor.ControlText }, new NSRange(0, s.Length));
            s.ApplyFontTraits(NSFontTraitMask.Italic, new NSRange(0, 1));
            var attributes = new NSMutableDictionary();
            var subscriptOffset = new NSNumber(-2);
            var range = new NSRange(1, 1);
            attributes.Add(NSStringAttributeKey.BaselineOffset, subscriptOffset);
            s.AddAttributes(attributes, range);

            return s;
        }

		public static NSAttributedString DissociationConstant(NSFont font)
		{
            var s = FromMarkDownString(MarkdownStrings.DissociationConstant, font);

            return s;
        }

        public static NSAttributedString AssociationConstant(NSFont font)
        {
            var s = FromMarkDownString(MarkdownStrings.AssociationnConstant, font);

            return s;
        }

        public static NSAttributedString AnalysisItemTitle(
            string title,
            string symbol,
            float fontSize,
            bool enabled = true)
        {
            var result = new NSMutableAttributedString();
            var titleFont = NSFont.SystemFontOfSize(fontSize, NSFontWeight.Semibold);
            var symbolFont = NSFont.SystemFontOfSize(fontSize);

            var titleText = FromMarkDownString(title ?? string.Empty, titleFont);
            var titleColor = enabled ? NSColor.Label : NSColor.DisabledControlText;
            var symbolColor = enabled ? NSColor.SecondaryLabel : NSColor.DisabledControlText;
            titleText.AddAttribute(NSStringAttributeKey.ForegroundColor, titleColor,
                new NSRange(0, titleText.Length));
            result.Append(titleText);

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var separator = PlainText(" — ", symbolFont);
                separator.AddAttribute(NSStringAttributeKey.ForegroundColor, symbolColor,
                    new NSRange(0, separator.Length));
                result.Append(separator);

                var symbolText = FromMarkDownString(symbol, symbolFont);
                symbolText.AddAttribute(NSStringAttributeKey.ForegroundColor, symbolColor,
                    new NSRange(0, symbolText.Length));
                result.Append(symbolText);
            }

            return result;
        }

        public static NSAttributedString AnalysisInspectorItemTitle(
            string title,
            string symbol,
            float fontSize,
            bool enabled = true,
            bool bold = false,
            bool medium = false)
        {
            var result = new NSMutableAttributedString();
            var titleFont = bold
                ? NSFont.BoldSystemFontOfSize(fontSize)
                : medium
                    ? NSFont.SystemFontOfSize(fontSize, NSFontWeight.Medium)
                : NSFont.SystemFontOfSize(fontSize);
            var symbolFont = NSFont.SystemFontOfSize(fontSize * 0.82f);
            var titleColor = enabled ? NSColor.Label : NSColor.DisabledControlText;
            var symbolColor = enabled ? NSColor.SecondaryLabel : NSColor.DisabledControlText;

            var titleText = FromMarkDownString(title ?? string.Empty, titleFont);
            titleText.AddAttribute(
                NSStringAttributeKey.ForegroundColor,
                titleColor,
                new NSRange(0, titleText.Length));
            result.Append(titleText);

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                result.Append(new NSAttributedString("\n"));
                var symbolText = FromMarkDownString(symbol, symbolFont);
                symbolText.AddAttribute(
                    NSStringAttributeKey.ForegroundColor,
                    symbolColor,
                    new NSRange(0, symbolText.Length));
                result.Append(symbolText);
            }

            return result;
        }

        public static string ParameterSymbol(ParameterType key, bool includeSiteIndex = false, bool correctionFactor = false)
        {
            if (correctionFactor) return "α";

            var symbol = key.GetProperties().SymbolName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(symbol)
                && key.GetProperties().ParentType == ParameterType.Affinity1)
                symbol = "*K*{d}";
            if (!includeSiteIndex) return symbol;

            if (ThermodynamicParameterSlots.TryResolve(key, out var slot, out _))
                return symbol + "{" + slot.Index + "}";

            switch (key)
            {
                case ParameterType.Nvalue1:
                    return symbol + "{1}";
                case ParameterType.Nvalue2:
                    return symbol + "{2}";
                default:
                    return symbol;
            }
        }

        public static NSMutableAttributedString FromMarkDownString(string str, NSFont font, bool iscg = false)
        {
            var segments = MarkdownProcessor.GetSegments(str);

            var attstr = new NSMutableAttributedString();
            var style = new NSMutableParagraphStyle()
            {
                Alignment = NSTextAlignment.Center,
                MaximumLineHeight = 12
            };

            foreach (var segment in segments)
            {
                switch (segment.Property) 
                {
                    default:
                    case MarkdownProperty.Plain: attstr.Append(PlainText(segment.Text, font)); break;
                    case MarkdownProperty.Cursive: attstr.Append(CursiveText(segment.Text, font)); break;
                    case MarkdownProperty.Bold: attstr.Append(BoldText(segment.Text, font)); break;
                    case MarkdownProperty.Subscript: attstr.Append(SubscriptText(segment.Text, font)); break;
                    case MarkdownProperty.Superscript: attstr.Append(SuperscriptText(segment.Text, font)); break;
                    case MarkdownProperty.Header1: attstr.Append(Header1Text(segment.Text, font)); break;
                    case MarkdownProperty.Header2: attstr.Append(Header2Text(segment.Text, font)); break;
                    case MarkdownProperty.Small: attstr.Append(SmallText(segment.Text, font)); break;
                }
            }

            //attstr.AddAttribute(NSStringAttributeKey.ParagraphStyle, style, new NSRange(0, attstr.Length));

            if (iscg) attstr.AddAttributes(new CoreText.CTStringAttributes() //Necessary for correct textbox text color...
            {
                ForegroundColorFromContext = true
            }, new NSRange(0, attstr.Length));

            return attstr;
        }
    }
}
