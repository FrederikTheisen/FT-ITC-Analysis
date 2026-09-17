using System.Globalization;

namespace AnalysisITC.Web;

/// <summary>Renders untrusted values safely for an administrative terminal.</summary>
public static class TerminalText
{
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (IsUnsafe(character))
            {
                builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
            else builder.Append(character);
        }
        return builder.ToString();
    }

    public static bool ContainsUnsafe(string value) => value.Any(IsUnsafe);

    static bool IsUnsafe(char character) => char.IsControl(character) || character == '\u007f'
        || (character >= '\u0080' && character <= '\u009f')
        || (char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format && character != '\u00ad');

    public static string Color(string value, string ansiCode, bool enabled) =>
        enabled ? $"\u001b[{ansiCode}m{Escape(value)}\u001b[0m" : Escape(value);
}
