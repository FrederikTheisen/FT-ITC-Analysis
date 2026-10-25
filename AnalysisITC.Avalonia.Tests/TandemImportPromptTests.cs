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

    [AvaloniaFact]
    public async Task MicroCalRemainsAvailableAfterInvalidDeadVolume()
    {
        var defaults = TandemConcatenation.BackMixingSettings.MicroCalDefault();
        using var fixture = new Prompt(defaults);
        fixture.Field<TextBox>("deadVolumeBox").Text = "NaN";
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
