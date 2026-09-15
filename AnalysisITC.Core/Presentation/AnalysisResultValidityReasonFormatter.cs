using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    public static class AnalysisResultValidityReasonFormatter
    {
        const int CompressionThreshold = 2;

        static readonly Category[] Categories =
        {
            new Category(
                "Baseline or integration regions were changed",
                new[]
                {
                    "baseline correction changed",
                    "integration windows changed",
                    "processed heat values changed"
                }),
            new Category(
                "Experiment properties were changed",
                new[]
                {
                    "injection bookkeeping changed",
                    "cell concentration changed",
                    "syringe concentration changed",
                    "cell volume changed",
                    "injection volumes changed",
                    "injection concentration state changed"
                }),
            new Category(
                "Injection inclusion was changed",
                new[] { "injection inclusion changed" }),
            new Category(
                "Experiment attributes were changed",
                new[]
                {
                    "ligand concentration attribute changed",
                    "buffer subtraction settings changed",
                    "fit-relevant experiment attributes changed"
                }),
            new Category(
                "experiments are missing",
                new[] { "experiment missing" })
        };

        public static IReadOnlyList<string> Format(AnalysisResult result)
        {
            return Format(
                result?.ValidityReport,
                result?.ValiditySnapshot?.Experiments?.Count
                    ?? result?.Solution?.Solutions?.Count
                    ?? 0);
        }

        public static IReadOnlyList<string> Format(
            AnalysisResultValidityReport report,
            int experimentCount)
        {
            var reasons = report?.Reasons?.Where(reason => !string.IsNullOrWhiteSpace(reason)).ToList()
                ?? new List<string>();
            if (reasons.Count == 0 || experimentCount <= CompressionThreshold)
                return reasons;

            var entries = reasons.Select(Parse).ToList();
            var counts = Categories.ToDictionary(
                category => category,
                category => entries.Count(entry => entry.Categories.Contains(category)));
            var compressed = Categories
                .Where(category => counts[category] > CompressionThreshold)
                .ToList();

            if (compressed.Count == 0)
                return reasons;

            var output = new List<string>();
            var emitted = new HashSet<Category>();
            foreach (var entry in entries)
            {
                foreach (var category in Categories)
                {
                    if (!compressed.Contains(category)
                        || !entry.Categories.Contains(category)
                        || !emitted.Add(category)) continue;

                    output.Add(category.Message(counts[category], experimentCount));
                }

                var remaining = entry.Offenses
                    .Where(offense => !compressed.Any(category => category.Matches(offense)))
                    .ToList();
                if (remaining.Count == 0)
                {
                    if (entry.Offenses.Count == 0)
                        output.Add(entry.Original);
                    continue;
                }

                output.Add(entry.Rebuild(remaining));
            }

            return output;
        }

        static Entry Parse(string reason)
        {
            if (reason.StartsWith("Experiment missing:", StringComparison.OrdinalIgnoreCase))
                return new Entry(reason, null, new[] { "experiment missing" });

            var separator = reason.IndexOf(": ", StringComparison.Ordinal);
            if (separator < 0)
                return new Entry(reason, null, Array.Empty<string>());

            var label = reason.Substring(0, separator);
            var offenses = reason.Substring(separator + 2)
                .TrimEnd('.')
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(offense => offense.Trim())
                .Where(offense => offense.Length > 0)
                .ToList();
            return new Entry(reason, label, offenses);
        }

        sealed class Category
        {
            readonly string[] offensePhrases;

            public string Label { get; }

            public Category(string label, string[] offensePhrases)
            {
                Label = label;
                this.offensePhrases = offensePhrases;
            }

            public bool Matches(string offense)
            {
                return offensePhrases.Any(phrase => string.Equals(
                    phrase,
                    offense,
                    StringComparison.OrdinalIgnoreCase));
            }

            public string Message(int changedCount, int experimentCount)
            {
                if (Label == "experiments are missing")
                    return $"{changedCount} of {experimentCount} {Label}.";

                return $"{Label} in {changedCount} of {experimentCount} experiments.";
            }
        }

        sealed class Entry
        {
            public string Original { get; }
            public string Label { get; }
            public IReadOnlyList<string> Offenses { get; }
            public IReadOnlyList<Category> Categories { get; }

            public Entry(string original, string label, IReadOnlyList<string> offenses)
            {
                Original = original;
                Label = label;
                Offenses = offenses;
                Categories = AnalysisResultValidityReasonFormatter.Categories
                    .Where(category => offenses.Any(category.Matches))
                    .ToList();
            }

            public string Rebuild(IReadOnlyList<string> offenses)
            {
                if (Label == null) return Original;
                return $"{Label}: {string.Join("; ", offenses)}.";
            }
        }
    }
}
