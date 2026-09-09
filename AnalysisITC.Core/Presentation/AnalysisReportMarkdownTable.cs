using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AnalysisITC.Core.Presentation
{
    internal static class AnalysisReportMarkdownTable
    {
        public static bool TryRead(string[] lines, int start, out AnalysisReportTableBlock table, out int end)
        {
            table = null;
            end = start;
            if (start + 1 >= lines.Length || !lines[start].Contains("|")) return false;
            var headers = Cells(lines[start]);
            var separators = Cells(lines[start + 1]);
            if (headers.Count == 0 || headers.Count != separators.Count
                || separators.Any(cell => !Regex.IsMatch(cell, @"^:?-{3,}:?$"))) return false;
            var columns = headers.Select((header, index) => new AnalysisReportTableColumn(
                "markdown-" + index, header.Replace("**", "").Replace("*", ""),
                separators[index].EndsWith(":") ? (separators[index].StartsWith(":")
                    ? AnalysisResultColumnAlignment.Center : AnalysisResultColumnAlignment.Right)
                    : AnalysisResultColumnAlignment.Left)).ToList();
            var rows = new List<AnalysisReportTableRow>();
            end = start + 1;
            for (var i = start + 2; i < lines.Length && lines[i].Contains("|") && !string.IsNullOrWhiteSpace(lines[i]); i++)
            {
                var cells = Cells(lines[i]);
                // Preserve extra cells as ordinary text rather than silently dropping them.
                if (cells.Count > headers.Count) break;
                while (cells.Count < headers.Count) cells.Add("");
                rows.Add(new AnalysisReportTableRow(cells));
                end = i;
            }
            table = new AnalysisReportTableBlock("", columns, rows,
                AnalysisReportLayoutPolicy.None, fontSize: 9, inlineMarkdown: true);
            return true;
        }

        static List<string> Cells(string line)
        {
            var value = line.Trim();
            if (value.StartsWith("|")) value = value.Substring(1);
            if (value.EndsWith("|") && (value.Length < 2 || value[value.Length - 2] != '\\')) value = value.Substring(0, value.Length - 1);
            var cells = new List<string>();
            var cell = new StringBuilder();
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length && (value[i + 1] == '|' || value[i + 1] == '\\')) cell.Append(value[++i]);
                else if (value[i] == '|') { cells.Add(cell.ToString().Trim()); cell.Clear(); }
                else cell.Append(value[i]);
            }
            cells.Add(cell.ToString().Trim());
            return cells;
        }
    }
}
