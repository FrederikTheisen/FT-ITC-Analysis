using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CoreText;

namespace AnalysisITC.Utils
{
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

            int currentIndex = 0;

            while (currentIndex < input.Length)
            {
                // Check for cursive text
                Match cursiveMatch = cursiveRegex.Match(input, currentIndex);
                if (cursiveMatch.Success)
                {
                    if (cursiveMatch.Index > currentIndex)
                    {
                        // Add the plain text segment before the cursive text
                        string plainText = input.Substring(currentIndex, cursiveMatch.Index - currentIndex);
                        segments.Add(new Segment(plainText, MarkdownProperty.Plain));
                    }

                    // Add the cursive text segment
                    string cursiveText = cursiveMatch.Groups[1].Value;
                    segments.Add(new Segment(cursiveText, MarkdownProperty.Cursive));

                    // Update the current index to the end of the cursive text
                    currentIndex = cursiveMatch.Index + cursiveMatch.Length;
                    continue;
                }

                // Check for subscript text
                Match subscriptMatch = subscriptRegex.Match(input, currentIndex);
                if (subscriptMatch.Success)
                {
                    if (subscriptMatch.Index > currentIndex)
                    {
                        // Add the plain text segment before the subscript text
                        string plainText = input.Substring(currentIndex, subscriptMatch.Index - currentIndex);
                        segments.Add(new Segment(plainText, MarkdownProperty.Plain));
                    }

                    // Add the subscript text segment
                    string subscriptText = subscriptMatch.Groups[1].Value;
                    segments.Add(new Segment(subscriptText, MarkdownProperty.Subscript));

                    // Update the current index to the end of the subscript text
                    currentIndex = subscriptMatch.Index + subscriptMatch.Length;
                    continue;
                }

                // If no markdown was found, add the rest of the plain text as a segment
                string remainingText = input.Substring(currentIndex);
                segments.Add(new Segment(remainingText, MarkdownProperty.Plain));
                break;
            }

            return segments;
        }
    }

    public enum MarkdownProperty
    {
        Plain,
        Cursive,
        Subscript
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