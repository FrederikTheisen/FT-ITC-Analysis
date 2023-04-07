using AppKit;
using Foundation;

namespace AnalysisITC.Utils
{
    public static class MacStrings
	{
        static NSMutableAttributedString CursiveText(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            s.AddAttributes(new NSStringAttributes { Font = font, ForegroundColor = NSColor.ControlText }, new NSRange(0, s.Length));
            s.ApplyFontTraits(NSFontTraitMask.Italic, new NSRange(0, s.Length));
            return s;
        }

        static NSMutableAttributedString SubscriptText(string str, NSFont font)
        {
            var s = new NSMutableAttributedString(str);
            font = NSFont.FromFontName(font.FontName, font.PointSize * 0.75f);
            s.AddAttributes(new NSStringAttributes { Font = font, ForegroundColor = NSColor.ControlText }, new NSRange(0, s.Length));
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

        public static NSAttributedString HeatCapacityChange(NSFont font)
        {
            var s = new NSMutableAttributedString("∆");
            s.Append(CursiveSubscript("Cp", font));

            return s;
        }

        public static NSAttributedString Enthalpy(NSFont font)
        {
            var s = new NSMutableAttributedString("∆H");
            s.ApplyFontTraits(NSFontTraitMask.Italic, new NSRange(1, 1));

            return s;
        }

        public static NSAttributedString FromString(string str, NSFont font)
        {
            switch (str)
            {
                case "Kd": return DissociationConstant(font);
                case "∆H": return Enthalpy(font);
                case "∆Cp": return HeatCapacityChange(font);
                default: return new NSAttributedString(str);
            }
        }

        public static NSAttributedString FromMarkDownString(string str, NSFont font)
        {
            var segments = MarkdownProcessor.GetSegments(str);

            var attstr = new NSMutableAttributedString();

            foreach (var segment in segments)
            {
                switch (segment.Property) 
                {
                    default:
                    case MarkdownProperty.Plain: attstr.Append(new NSAttributedString(segment.Text, new NSStringAttributes { Font = font, ForegroundColor = NSColor.ControlText })); break;
                    case MarkdownProperty.Cursive: attstr.Append(CursiveText(segment.Text, font)); break;
                    case MarkdownProperty.Subscript: attstr.Append(SubscriptText(segment.Text, font)); break;
                }
            }

            return attstr;
        }
    }
}

