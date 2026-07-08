using System;
using System.IO;

using Avalonia.Platform;

namespace AnalysisITC.Avalonia.Help;

internal static class AvaloniaHelpResourceLoader
{
    const string ResourceRoot = "avares://AnalysisITC.Avalonia/Resources/";

    public static string LoadText(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("Help resource name is missing.", nameof(resourceName));

        var uri = new Uri(ResourceRoot + resourceName.TrimStart('/'));
        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
