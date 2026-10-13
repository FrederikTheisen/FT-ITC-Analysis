global using Avalonia.Headless.XUnit;

using Avalonia;
using Avalonia.Headless;
using System;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(AnalysisITC.Avalonia.Tests.TestAppBuilder))]
// Preserve the shared application used by the existing UI collection. Its tests
// run serially because they also change Core settings and document state.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace AnalysisITC.Avalonia.Tests;

[CollectionDefinition("Avalonia UI", DisableParallelization = true)]
public sealed class AvaloniaUiCollection { }

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<HeadlessTestApplication>();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FTITC_PROJECT_INFORMATION_QA_DIRECTORY")))
            return builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    sealed class HeadlessTestApplication : Application { }
}
