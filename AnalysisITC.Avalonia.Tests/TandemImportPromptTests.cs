using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AnalysisITC.Core.Processing;
using AnalysisITC.Platform.Avalonia;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class TandemImportPromptTests
{
    [AvaloniaTheory]
    [InlineData("en-US", "80.5")]
    [InlineData("da-DK", "80,5")]
    public async Task InvalidDeadVolumeRetainsTheDialogAndDraftsUntilCorrected(string cultureName, string positive)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var defaults = TandemConcatenation.BackMixingSettings.MicroCalDefault();
            using var fixture = new Prompt(defaults);
            fixture.Field<TextBox>("mixingFractionBox").Text = "25";
            fixture.Field<CheckBox>("removeOverflowCheck").IsChecked = false;
            var dead = fixture.Field<TextBox>("deadVolumeBox");
            foreach (var text in new[] { "", "abc", "80 uL", "-1", "0", "NaN", "Infinity", "∞", "-∞", "1e999", "1e-320" })
            {
                dead.Text = text;
                fixture.Click("Use Back-Mixing Compensation");
                Assert.False(fixture.Completion.IsCompleted);
                Assert.True(fixture.Dialog.IsVisible);
                Assert.Equal(text, dead.Text);
                Assert.Equal("25", fixture.Field<TextBox>("mixingFractionBox").Text);
                Assert.False(fixture.Field<CheckBox>("removeOverflowCheck").IsChecked);
                Assert.True(fixture.Field<TextBlock>("errorText").IsVisible);
                Assert.Contains("Dead volume", fixture.Field<TextBlock>("errorText").Text);
                Assert.Equal(80e-6, defaults.DeadVolume);
                Assert.False(defaults.UseBackMixingMethod);
            }
            dead.Text = positive;
            fixture.Click("Use Back-Mixing Compensation");
            var result = await fixture.Completion;
            Assert.Equal(80.5e-6, result.DeadVolume, 15);
            Assert.Equal(.25, result.MixingFraction);
            Assert.False(result.DidRemoveOverflow);
            Assert.True(result.UseBackMixingMethod);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [AvaloniaTheory]
    [InlineData("en-US", "25.5")]
    [InlineData("da-DK", "25,5")]
    public async Task InvalidMixingFractionRetainsAllDraftsUntilCorrected(string cultureName, string positive)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var defaults = TandemConcatenation.BackMixingSettings.MicroCalDefault();
            using var fixture = new Prompt(defaults);
            fixture.Field<TextBox>("deadVolumeBox").Text = "65";
            fixture.Field<CheckBox>("removeOverflowCheck").IsChecked = false;
            var mixing = fixture.Field<TextBox>("mixingFractionBox");
            foreach (var text in new[] { "NaN", "Infinity", "-Infinity", "∞", "-∞", "1e999", "-1e999", "-1", "101", "", "abc", "25%" })
            {
                mixing.Text = text;
                fixture.Click("Use Back-Mixing Compensation");
                Assert.False(fixture.Completion.IsCompleted);
                Assert.True(fixture.Dialog.IsVisible);
                Assert.Equal(text, mixing.Text);
                Assert.Equal("65", fixture.Field<TextBox>("deadVolumeBox").Text);
                Assert.False(fixture.Field<CheckBox>("removeOverflowCheck").IsChecked);
                Assert.True(fixture.Field<TextBlock>("errorText").IsVisible);
                Assert.Contains("Mixing fraction", fixture.Field<TextBlock>("errorText").Text);
                Assert.Equal(80e-6, defaults.DeadVolume);
                Assert.Equal(0, defaults.MixingFraction);
                Assert.True(defaults.DidRemoveOverflow);
                Assert.False(defaults.UseBackMixingMethod);
            }
            mixing.Text = positive;
            fixture.Click("Use Back-Mixing Compensation");
            var result = await fixture.Completion;
            Assert.Equal(.255, result.MixingFraction);
            Assert.Equal(65e-6, result.DeadVolume, 15);
            Assert.False(result.DidRemoveOverflow);
            Assert.True(result.UseBackMixingMethod);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [AvaloniaTheory]
    [InlineData("0", 0)]
    [InlineData("100", 1)]
    public async Task MixingFractionEndpointsAreAccepted(string text, double expected)
    {
        using var fixture = new Prompt(TandemConcatenation.BackMixingSettings.MicroCalDefault());
        fixture.Field<TextBox>("mixingFractionBox").Text = text;
        fixture.Click("Use Back-Mixing Compensation");
        Assert.Equal(expected, (await fixture.Completion).MixingFraction);
    }

    [AvaloniaTheory]
    [InlineData("deadVolumeBox")]
    [InlineData("mixingFractionBox")]
    public async Task MicroCalRemainsAvailableAfterInvalidInput(string field)
    {
        var defaults = TandemConcatenation.BackMixingSettings.MicroCalDefault();
        using var fixture = new Prompt(defaults);
        fixture.Field<TextBox>(field).Text = "NaN";
        fixture.Click("Use Back-Mixing Compensation");
        Assert.False(fixture.Completion.IsCompleted);
        fixture.Click("Use MicroCal Concat");
        Assert.Same(defaults, await fixture.Completion);
    }

    sealed class Prompt : IDisposable
    {
        readonly Window owner = new();
        public Window Dialog { get; }
        public Task<TandemConcatenation.BackMixingSettings> Completion { get; }
        public Prompt(TandemConcatenation.BackMixingSettings defaults)
        {
            var type = typeof(AvaloniaTandemImportPromptService)
                .GetNestedType("TandemImportPromptWindow", BindingFlags.NonPublic)!;
            Dialog = (Window)Activator.CreateInstance(type, new object[] { "tandem.itc", 3, defaults })!;
            owner.Show();
            Completion = Dialog.ShowDialog<TandemConcatenation.BackMixingSettings>(owner);
        }
        public T Field<T>(string name) => (T)Dialog.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Dialog)!;
        public void Click(string text) => Dialog.GetLogicalDescendants()
            .OfType<Button>().Single(button => Equals(button.Content, text))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        public void Dispose() { Dialog.Close(); owner.Close(); }
    }
}
