using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CoreText;

namespace AnalysisITC.Utils
{
    public static class MarkdownStrings
    {
        public const string DissociationConstant = "*K*{d}";
        public const string DissociationConstantTrans = "*K*{d,trans}";
        public const string DissociationConstantCis = "*K*{d,cis}";
        public const string ApparantDissociationConstant = "*K*{d,app}";
        public const string IsomerizationEquilibriumConstant = "*K*{eq}";
        public const string Enthalpy = "∆*H*";
        public const string GibbsFreeEnergy = "∆*G*";
        public const string EntropyContribution = "-*T*∆*S*";
        public const string ProtonationEnthalpy = "∆*H*{buffer}";
    }

    /// <summary>
    /// Method and markdown languange 100% invented and written by ChatGPT
    /// </summary>
    public class MarkdownProcessor
    {
        public static List<Segment> GetSegments(string input)
        {
            List<Segment> segments = new List<Segment>();

            Regex cursiveRegex = new Regex(@"\*([^*]+)\*");
            Regex subscriptRegex = new Regex(@"\{([^}]+)\}");
            Regex boldRegex = new Regex(@"\*\*(.*?)\*\*");
            Regex header1Regex = new Regex(@"^# (.+)$", RegexOptions.Multiline);
            Regex header2Regex = new Regex(@"^## (.+)$", RegexOptions.Multiline);

            int currentIndex = 0;

            while (currentIndex < input.Length)
            {
                Match header2Match = header2Regex.Match(input, currentIndex);
                Match header1Match = header1Regex.Match(input, currentIndex);
                Match boldMatch = boldRegex.Match(input, currentIndex);
                Match cursiveMatch = cursiveRegex.Match(input, currentIndex);
                Match subscriptMatch = subscriptRegex.Match(input, currentIndex);

                Match match = null;
                int firstidx = int.MaxValue;
                MarkdownProperty type = MarkdownProperty.Plain;
                type = CheckMatch(header2Match, ref match, ref firstidx, MarkdownProperty.Header2) ?? type;
                type = CheckMatch(header1Match, ref match, ref firstidx, MarkdownProperty.Header1) ?? type;
                type = CheckMatch(boldMatch, ref match, ref firstidx, MarkdownProperty.Bold) ?? type;
                type = CheckMatch(cursiveMatch, ref match, ref firstidx, MarkdownProperty.Cursive) ?? type;
                type = CheckMatch(subscriptMatch, ref match, ref firstidx, MarkdownProperty.Subscript) ?? type;

                if (match != null)
                {
                    currentIndex = AddSegment(input, segments, currentIndex, match, type);
                    continue;
                }

                //if (cursiveMatch.Success)
                //{
                //    if (cursiveMatch.Index > currentIndex)
                //    {
                //        // Add the plain text segment before the cursive text
                //        string plainText = input.Substring(currentIndex, cursiveMatch.Index - currentIndex);
                //        segments.Add(new Segment(plainText, MarkdownProperty.Plain));
                //    }

                //    // Add the cursive text segment
                //    string cursiveText = cursiveMatch.Groups[1].Value;
                //    segments.Add(new Segment(cursiveText, MarkdownProperty.Cursive));

                //    // Update the current index to the end of the cursive text
                //    currentIndex = cursiveMatch.Index + cursiveMatch.Length;
                //    continue;
                //}

                //// Check for subscript text

                //if (subscriptMatch.Success)
                //{
                //    if (subscriptMatch.Index > currentIndex)
                //    {
                //        // Add the plain text segment before the subscript text
                //        string plainText = input.Substring(currentIndex, subscriptMatch.Index - currentIndex);
                //        segments.Add(new Segment(plainText, MarkdownProperty.Plain));
                //    }

                //    // Add the subscript text segment
                //    string subscriptText = subscriptMatch.Groups[1].Value;
                //    segments.Add(new Segment(subscriptText, MarkdownProperty.Subscript));

                //    // Update the current index to the end of the subscript text
                //    currentIndex = subscriptMatch.Index + subscriptMatch.Length;
                //    continue;
                //}

                // If no markdown was found, add the rest of the plain text as a segment
                string remainingText = input.Substring(currentIndex);
                segments.Add(new Segment(remainingText, MarkdownProperty.Plain));
                break;
            }

            return segments;

            static MarkdownProperty? CheckMatch(Match test, ref Match match, ref int firstidx, MarkdownProperty type)
            {
                if (test.Success && test.Index < firstidx) { firstidx = test.Index; match = test; return type; } return null;
            }
        }

        private static int AddSegment(string input, List<Segment> segments, int currentIndex, Match match, MarkdownProperty type)
        {
            if (match.Index > currentIndex)
            {
                // Add the plain text segment before the cursive text
                string plainText = input.Substring(currentIndex, match.Index - currentIndex);
                segments.Add(new Segment(plainText, MarkdownProperty.Plain));
            }

            string text = match.Groups[1].Value;
            segments.Add(new Segment(text, type));

            // Update the current index to the end of the cursive text
            currentIndex = match.Index + match.Length;
            return currentIndex;
        }
    }

    public enum MarkdownProperty
    {
        Plain,
        Cursive,
        Subscript,
        Bold,
        Header1,
        Header2
    }

    public class Segment
    {
        public string Text { get; set; }
        public MarkdownProperty Property { get; set; }

        public Segment(string text, MarkdownProperty property)
        {
            Text = text;
            Property = property;
        }
    }
}