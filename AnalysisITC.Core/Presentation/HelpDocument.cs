using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.Core.Presentation
{
    public sealed class HelpDocument
    {
        public IReadOnlyList<HelpTopic> Topics { get; }

        public HelpDocument(IReadOnlyList<HelpTopic> topics)
        {
            Topics = topics ?? Array.Empty<HelpTopic>();
        }
    }

    public sealed class HelpTopic
    {
        public string Title { get; }
        public int Level { get; }
        public HelpTopic Parent { get; }
        public List<HelpTopic> Children { get; } = new List<HelpTopic>();
        public string RawMarkdown { get; internal set; } = "";

        public HelpTopic(string title, int level, HelpTopic parent = null)
        {
            Title = title ?? "";
            Level = level;
            Parent = parent;
        }
    }

    public static class HelpDocumentParser
    {
        public static HelpDocument Parse(string text)
        {
            var topics = new List<HelpTopic>();
            var lines = (text ?? "")
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
            var sectionLines = new List<string>();
            HelpTopic currentSection = null;

            void FinishSection()
            {
                if (currentSection == null) return;

                currentSection.RawMarkdown = string.Join(Environment.NewLine, sectionLines).Trim();
                sectionLines.Clear();
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("## "))
                {
                    FinishSection();

                    currentSection = new HelpTopic(line.Substring(3).Trim(), 2);
                    topics.Add(currentSection);
                    sectionLines.Add(line);
                    continue;
                }

                if (line.StartsWith("# ") && currentSection != null)
                    currentSection.Children.Add(new HelpTopic(line.Substring(2).Trim(), 1, currentSection));

                if (currentSection == null)
                {
                    currentSection = new HelpTopic("Overview", 2);
                    topics.Add(currentSection);
                }

                sectionLines.Add(line);
            }

            FinishSection();

            return new HelpDocument(topics
                .Where(topic => !string.IsNullOrWhiteSpace(topic.RawMarkdown))
                .ToList());
        }
    }
}
