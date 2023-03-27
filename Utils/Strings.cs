using System;
using AppKit;
using CoreText;
using Foundation;

namespace AnalysisITC.Utils
{
	public static class Strings
	{
        static void CursiveK(float size)
        {

        }

        public static NSAttributedString CursiveSubscript(string str, float size)
        {
            var s = new NSMutableAttributedString(str);
            s.AddAttributes(new NSStringAttributes { Font = NSFont.SystemFontOfSize(size), ForegroundColor = NSColor.ControlText }, new NSRange(0, s.Length));
            s.ApplyFontTraits(NSFontTraitMask.Italic, new NSRange(0, 1));
            var attributes = new NSMutableDictionary();
            var subscriptOffset = new NSNumber(-2);
            var range = new NSRange(1, 1);
            attributes.Add(NSStringAttributeKey.BaselineOffset, subscriptOffset);
            s.AddAttributes(attributes, range);

            return s;
        }

		public static NSAttributedString DissociationConstant(float size)
		{
            return CursiveSubscript("Kd", size);
        }

        public static NSAttributedString AssociationConstant(float size)
        {
            return CursiveSubscript("Ka", size);
        }
    }
}

