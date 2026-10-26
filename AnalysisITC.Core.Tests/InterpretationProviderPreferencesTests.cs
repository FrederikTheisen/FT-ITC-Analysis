using AnalysisITC.Core.Application;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class InterpretationProviderPreferencesTests
{
    [Fact]
    public void ProviderRoundTripsThroughPreferencesAndSettingsStorage()
    {
        var original=PreferencesState.FromSettings(); var originalStore=PlatformServices.SettingsStore;
        try
        {
            PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
            var state=PreferencesState.FromSettings(); state.InterpretationProvider="mistral"; state.ApplyToSettings(); AppSettings.Save();
            AppSettings.InterpretationProvider="openai"; AppSettings.Load();
            Assert.Equal("mistral",AppSettings.InterpretationProvider);
            Assert.Equal("mistral",PreferencesState.FromSettings().InterpretationProvider);
        }
        finally { PlatformServices.RegisterSettingsStore(originalStore); original.ApplyToSettings(); }
    }

    [Theory]
    [InlineData("default","Provider default")]
    [InlineData("none","none")]
    [InlineData("high","high")]
    [InlineData(null,null)]
    public void ProviderDefaultReasoningHasADisplayLabelThatMapsBack(string? value,string? label)
    {
        Assert.Equal(label,InterpretationProviderPresentation.ReasoningLabel(value!));
        Assert.Equal(value,InterpretationProviderPresentation.ReasoningValue(label!));
        // Converting twice leaves the result unchanged.
        Assert.Equal(label,InterpretationProviderPresentation.ReasoningLabel(label!));
        Assert.Equal(value,InterpretationProviderPresentation.ReasoningValue(value!));
    }
}
