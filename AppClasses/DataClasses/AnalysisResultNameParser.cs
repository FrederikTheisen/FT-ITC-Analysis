using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.Analysis2.Models;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC
{
	public static class AnalysisResultNameParser
	{
        /// <summary>
        /// Builds a descriptive AnalysisResult name based on what is constant across experiments in this result
        /// and what varies across all loaded experiments.
        ///
        /// Rules:
        /// - Temperature/buffer/salt tags are only included if:
        ///   (1) the value is identical across all experiments in this AnalysisResult, AND
        ///   (2) there exists other loaded ExperimentData with a different value (so the tag is informative).
        /// - A "variant" token is attempted from common tokens in the experiment names (and comments if present).
        /// </summary>
        public static string GenerateSuggestedName(GlobalSolution solution)
        {
            try
            {
                var solutions = solution?.Solutions;
                if (solutions == null || solutions.Count == 0) return null;
                if (solutions.Count == DataManager.Data.Count) return null;

                var modelInfo = solution?.Model?.Solution?.SolutionName;
                if (string.IsNullOrWhiteSpace(modelInfo)) modelInfo = solution.Model.ModelType.ToString();

                // Experiments included in this result
                var resultData = solutions
                    .Select(s => s.Data)
                    .Where(d => d != null)
                    .Distinct()
                    .ToList();

                if (resultData.Count == 0) return null;

                // All loaded experiments (for determining whether a tag is discriminating)
                var allData = DataManager.Data ?? new List<ExperimentData>();

                var parts = new List<string>();

                // Variant / label token
                var variant = TryGetVariantToken(resultData, allData);
                if (!string.IsNullOrWhiteSpace(variant)) parts.Add(variant);

                // Temperature (only if constant within result AND different exists in loaded data)
                if (TryGetCommonTemperature(solutions, out double tempC))
                {
                    if (HasDifferent(allData.Select(d => d.MeasuredTemperature), tempC, AppSettings.MinimumTemperatureSpanForFitting))
                        parts.Add(FormatTemperature(tempC));
                }

                // Buffer (only if constant within result AND different exists in loaded data)
                if (TryGetCommonBuffer(resultData, out int bufferId))
                {
                    if (HasDifferentBuffer(allData, bufferId))
                        parts.Add(FormatBuffer(bufferId));
                }

                // Salt (only if constant within result AND different exists in loaded data)
                if (TryGetCommonSalt(resultData, out string saltDescriptor))
                {
                    if (HasDifferentSalt(allData, saltDescriptor))
                        parts.Add(saltDescriptor);
                }

                if (parts.Count == 0) return null;

                return string.Join(" | ", parts) + " | " + modelInfo;
            }
            catch
            {
                return null;
            }
        }

        static bool TryGetCommonTemperature(IList<SolutionInterface> sols, out double tempC)
        {
            tempC = double.NaN;
            if (sols == null || sols.Count == 0) return false;

            var t0 = sols[0].Temp;
            for (int i = 1; i < sols.Count; i++)
                if (Math.Abs(sols[i].Temp - t0) > 0.15) return false;

            tempC = t0;
            return true;
        }

        static string FormatTemperature(double tempC)
        {
            if (Math.Abs(tempC - Math.Round(tempC)) < 0.05) return $"{Math.Round(tempC):0}C";
            return $"{tempC:0.#}C";
        }

        static bool TryGetCommonBuffer(IList<ExperimentData> data, out int bufferId)
        {
            bufferId = -1;
            if (data == null || data.Count == 0) return false;

            // Require exactly one buffer attribute per experiment for a clean label
            int? id = null;

            foreach (var d in data)
            {
                var bufs = d.Attributes.Where(a => a.Key == AttributeKey.Buffer).ToList();
                if (bufs.Count != 1) return false;

                if (id == null) id = bufs[0].IntValue;
                else if (bufs[0].IntValue != id.Value) return false;
            }

            bufferId = id ?? -1;
            return bufferId != -1;
        }

        static string FormatBuffer(int bufferId)
        {
            try
            {
                var name = ((Buffer)bufferId).GetProperties().AttributedName;
                if (string.IsNullOrWhiteSpace(name)) return ((Buffer)bufferId).ToString();
                return name.Replace("{", "").Replace("}", "");
            }
            catch
            {
                return $"Buffer#{bufferId}";
            }
        }

        static bool HasDifferentBuffer(IEnumerable<ExperimentData> all, int bufferId)
        {
            if (all == null) return false;

            foreach (var d in all)
            {
                var bufs = d.Attributes.Where(a => a.Key == AttributeKey.Buffer).ToList();
                if (bufs.Count != 1) continue;

                if (bufs[0].IntValue != bufferId) return true;
            }

            return false;
        }

        static bool TryGetCommonSalt(IList<ExperimentData> data, out string descriptor)
        {
            descriptor = null;
            if (data == null || data.Count == 0) return false;

            string first = null;

            foreach (var d in data)
            {
                var s = CanonicalSaltDescriptor(d);
                if (string.IsNullOrWhiteSpace(s)) return false; // require explicit salt for now

                if (first == null) first = s;
                else if (!string.Equals(first, s, StringComparison.Ordinal)) return false;
            }

            descriptor = first;
            return !string.IsNullOrWhiteSpace(descriptor);
        }

        static bool HasDifferentSalt(IEnumerable<ExperimentData> all, string descriptor)
        {
            if (all == null || string.IsNullOrWhiteSpace(descriptor)) return false;

            foreach (var d in all)
            {
                var s = CanonicalSaltDescriptor(d);
                if (string.IsNullOrWhiteSpace(s)) return true; // some experiments have no salt set
                if (!string.Equals(s, descriptor, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        static string CanonicalSaltDescriptor(ExperimentData data)
        {
            try
            {
                var salts = data.Attributes.Where(a => a.Key == AttributeKey.Salt).OrderBy(a => a.IntValue).ToList();
                if (salts.Count == 0) return null;

                var parts = new List<string>();
                foreach (var s in salts)
                {
                    var name = ((Salt)s.IntValue).GetProperties().Name;
                    if (string.IsNullOrWhiteSpace(name)) name = ((Salt)s.IntValue).ToString();

                    double conc = s.ParameterValue; // FloatWithError supports implicit conversion in the code base
                    parts.Add($"{name} {FormatConcentration(conc)}");
                }

                return string.Join("+", parts);
            }
            catch
            {
                return null;
            }
        }

        static string FormatConcentration(double value)
        {
            // Heuristic: if stored in M (e.g., 0.15), convert to mM; otherwise treat as mM.
            double mM = value < 5 ? value * 1000.0 : value;

            if (Math.Abs(mM - Math.Round(mM)) < 0.05) return $"{Math.Round(mM):0} mM";
            if (mM >= 100) return $"{mM:0.#} mM";
            if (mM >= 10) return $"{mM:0.##} mM";
            return $"{mM:0.###} mM";
        }

        static bool HasDifferent(IEnumerable<double> values, double reference, double tol)
        {
            if (values == null) return false;

            foreach (var v in values)
            {
                if (Math.Abs(v - reference) > tol) return true;
            }

            return false;
        }

        static string TryGetVariantToken(IList<ExperimentData> selected, IList<ExperimentData> all)
        {
            if (selected == null || selected.Count == 0) return null;

            // Tokenize names for selected
            var selectedSets = selected.Select(Tokenize).ToList();
            var common = new HashSet<string>(selectedSets[0]);
            foreach (var s in selectedSets.Skip(1)) common.IntersectWith(s);

            common.RemoveWhere(t => StopTokens.Contains(t));
            if (common.Count == 0) return null;

            // Tokenize all loaded experiments to determine discriminating tokens
            var allSets = (all ?? selected).Select(Tokenize).ToList();
            int total = allSets.Count;

            string best = null;
            double bestScore = double.NegativeInfinity;

            foreach (var tok in common)
            {
                int freq = allSets.Count(s => s.Contains(tok));
                if (freq >= total) continue; // not discriminating

                double score = 0;
                score += Math.Min(tok.Length, 16) / 16.0;
                score += LooksLikeVariant(tok) ? 2.0 : 0.0;
                score += (1.0 - (freq / (double)total)); // rarer is better

                if (score > bestScore)
                {
                    bestScore = score;
                    best = tok;
                }
            }

            if (string.IsNullOrWhiteSpace(best)) return null;
            return FormatVariant(best);
        }

        static HashSet<string> Tokenize(ExperimentData d)
        {
            // Use FileName; include Comments if present on ITCDataContainer without hard dependency
            var name = "";
            try
            {
                if (!string.IsNullOrWhiteSpace(d?.FileName))
                    name = System.IO.Path.GetFileNameWithoutExtension(d.FileName);

                //var prop = d?.GetType().GetProperty("Comments");
                //if (prop != null)
                //{
                //    var c = prop.GetValue(d) as string;
                //    if (!string.IsNullOrWhiteSpace(c)) name += " " + c;
                //}
            }
            catch { }

            var s = (name ?? "");//.ToLowerInvariant();

            string delimiter = @"[\s_]+";
            if (!s.Contains('_') && !s.Contains(' ')) //If name does not contain '_' or ' ', we expand seperator lookup
                delimiter = @"[\s_-.+]+";

            s = s.Replace("(", "").Replace(")", "");
            s = s.Replace("[", "").Replace("]", "");
            s = s.Replace("{", "").Replace("}", "");

            var tokens = Regex.Split(s, delimiter)
                .Where(t => t.Length >= 2)
                .Where(t => !Regex.IsMatch(t, @"^\d+$"))      // drop pure numbers
                .Where(t => t.Any(char.IsLetter))             // Only keep tokens that contain letters
                .Where(t => !Regex.IsMatch(t, @"^\d"))        // drop tokens starting with digits (often replicate/temperature)
                .Where(t => !LooksLikeDateToken(t))
                .Select(t => StripTrailingReplicateTag(t))    // TEST 
                .ToList();

            return tokens.ToHashSet();
        }

        static string StripTrailingReplicateTag(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;

            // Remove extension first if caller didn't
            name = name.Trim();

            // Examples removed:
            // "MyExp (1)" -> "MyExp"
            // "MyExp(2)"  -> "MyExp"
            // "MyExp (a)" -> "MyExp"
            // "MyExp (ii)"-> "MyExp"
            //
            // Keeps longer parentheses like "(mutant)" because that may be meaningful.
            name = Regex.Replace(name, @"\s*\(\s*[A-Za-z0-9]{1,3}\s*\)\s*$", "");

            // Collapse extra whitespace
            name = Regex.Replace(name, @"\s{2,}", " ").Trim();

            return name;
        }

        static bool LooksLikeVariant(string t)
        {
            // e.g. wt, mutant, e45k, r273h, k12a, etc.
            if (t == "wt" || t == "mut" || t == "mutant") return true;
            if (Regex.IsMatch(t, @"^[a-z]{1,3}\d{1,5}[a-z]{0,3}$")) return true;
            if (Regex.IsMatch(t, @"^\d{1,5}[a-z]{1,3}$")) return true;
            return false;
        }

        static bool LooksLikeDateToken(string t)
        {
            if (!Regex.IsMatch(t, @"^\d+$")) return false;
            if (t.Length == 8 && t.StartsWith("20")) return true;
            return false;
        }

        static string FormatVariant(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return t;
            if (t.Length <= 4) return t.ToUpperInvariant();
            return char.ToUpperInvariant(t[0]) + t.Substring(1);
        }

        static readonly HashSet<string> StopTokens = new HashSet<string>
        {
            "itc","analysis","result","fit","global","model","data","exp","experiment",
            "run","rep","repeat","trial","cell","syringe","inj","injection",
            "buffer","salt","ph","deg","temp","temperature","("
        };
    }
}

