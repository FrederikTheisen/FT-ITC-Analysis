using System;
using System.IO;

namespace AnalysisITC.Avalonia.Help;

internal static class AvaloniaHelpResourceLoader
{
    public static string LoadText(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("Help resource name is missing.", nameof(resourceName));

        using var stream = AppAssetLoader.Open("Resources/" + resourceName.TrimStart('/'));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
