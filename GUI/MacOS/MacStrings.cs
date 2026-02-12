using AppKit;
using Foundation;

namespace AnalysisITC.Utilities
{
    public static class MacStrings
	{


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

        static NSMutableAttributedString SubscriptText(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            font = NSFont.FromFontName(font.FontName, font.PointSize * 0.75f);
            s.AddAttributes(new NSStringAttributes { Font = font }, new NSRange(0, s.Length));
            var attributes = new NSMutableDictionary();
            var subscriptOffset = new NSNumber(-2);
            var range = new NSRange(0, s.Length);
            attributes.Add(NSStringAttributeKey.BaselineOffset, subscriptOffset);
            s.AddAttributes(attributes, range);

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
            var s = CursiveSubscript("Kd", font);

            return s;
        }

        public static NSAttributedString AssociationConstant(NSFont font)
        {
            var s = CursiveSubscript("Ka", font);

            return s;
        }

        public static NSAttributedString FromMarkDownString(string str, NSFont font, bool iscg = false)
        {
            var segments = MarkdownProcessor.GetSegments(str);

            var attstr = new NSMutableAttributedString();

            foreach (var segment in segments)
            {
                switch (segment.Property) 
                {
                    default:
                    case MarkdownProperty.Plain: attstr.Append(PlainText(segment.Text, font)); break;
                    case MarkdownProperty.Cursive: attstr.Append(CursiveText(segment.Text, font)); break;
                    case MarkdownProperty.Bold: attstr.Append(BoldText(segment.Text, font)); break;
                    case MarkdownProperty.Subscript: attstr.Append(SubscriptText(segment.Text, font)); break;
                    case MarkdownProperty.Header1: attstr.Append(Header1Text(segment.Text, font)); break;
                    case MarkdownProperty.Header2: attstr.Append(Header2Text(segment.Text, font)); break;
                }
            }
            if (iscg) attstr.AddAttributes(new CoreText.CTStringAttributes() //Necessary for correct textbox text color...
            {
                ForegroundColorFromContext = true
            }, new NSRange(0, attstr.Length));

            return attstr;
        }
    }
}

