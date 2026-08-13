using System;
using System.IO;

namespace AnalysisITC.Avalonia;

internal static class AppAssetLoader
{
    const string ResourcePrefix = "AnalysisITC.Avalonia.";

    public static Stream Open(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            throw new ArgumentException("Asset path is missing.", nameof(assetPath));

        var normalizedPath = assetPath.TrimStart('/').Replace('\\', '/');
        if (normalizedPath.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Asset path cannot contain parent-directory segments.", nameof(assetPath));

        var resourceName = ResourcePrefix + normalizedPath.Replace('/', '.');
        return typeof(AppAssetLoader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Bundled application asset '{normalizedPath}' was not found.", resourceName);
    }
}
