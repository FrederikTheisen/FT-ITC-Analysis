using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AnalysisITC.Core.Presentation
{
    public sealed class HelpContentBlock
    {
        public string Text { get; }
        public bool IsEquation { get; }

        internal HelpContentBlock(string text, bool isEquation)
        {
            Text = text;
            IsEquation = isEquation;
        }
    }

    public static class HelpContent
    {
        public const double EquationFontSize = 14;
        public const double EquationLineHeight = 22;
        public const double EquationSpacing = 8;

        // Display equations use $$ on separate lines; surrounding text is preserved verbatim.
        static readonly Regex EquationFence = new Regex(@"(?m)^[\t ]*\$\$[\t ]*(?:\r?\n|$)");

        public static IReadOnlyList<HelpContentBlock> Parse(string text)
        {
            text = text ?? "";
            var blocks = new List<HelpContentBlock>();
            var fences = EquationFence.Matches(text);
            var offset = 0;
            for (var index = 0; index + 1 < fences.Count; index += 2)
            {
                var opening = fences[index];
                var closing = fences[index + 1];
                if (opening.Index > offset)
                    blocks.Add(new HelpContentBlock(text.Substring(offset, opening.Index - offset), false));

                var start = opening.Index + opening.Length;
                var equation = text.Substring(start, closing.Index - start).Trim();
                if (equation.Length > 0) blocks.Add(new HelpContentBlock(equation, true));
                offset = closing.Index + closing.Length;
            }

            if (offset < text.Length)
                blocks.Add(new HelpContentBlock(text.Substring(offset), false));
            return blocks;
        }
    }
}
