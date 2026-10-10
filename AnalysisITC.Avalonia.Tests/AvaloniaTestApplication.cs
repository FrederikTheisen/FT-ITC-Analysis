global using Avalonia.Headless.XUnit;

using Avalonia;
using Avalonia.Headless;
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
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<HeadlessTestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    sealed class HeadlessTestApplication : Application { }
}
