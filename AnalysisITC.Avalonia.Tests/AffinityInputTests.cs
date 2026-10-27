using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AffinityInputTests
{
    [AvaloniaTheory]
    [InlineData(ConcentrationUnit.µM, 1, 10)]
    [InlineData(ConcentrationUnit.nM, 1000, 10000)]
    public void EditorsUseKdDespiteObsoleteStoredPreference(ConcentrationUnit unit, double initialKd, double enteredKd)
    {
        var previous = PreferencesState.FromSettings();
        var previousStore = PlatformServices.SettingsStore;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var store = new InMemorySettingsStore();
            store.SetBool("InputAffinityAsDissociationConstant", false);
            PlatformServices.RegisterSettingsStore(store);
            AppSettings.Load();
            AppSettings.DefaultConcentrationUnit = unit;
            var parameter = new Parameter(ParameterType.Affinity1, 6);
            parameter.Limits[0] = 3;
            parameter.Limits[1] = 9;
            var row = AnalysisParameterRowBuilder.Build(parameter,
                (_, value, locked) => parameter.Update(value, locked), _ => { }, _ => { }, () => false);
            var field = Assert.Single(row.GetLogicalDescendants().OfType<TextBox>());
            Assert.Equal(initialKd.ToString(), field.Text);
            Assert.Contains(row.GetLogicalDescendants().OfType<TextBlock>(), label => label.Text == unit.GetName());
            var range = AnalysisParameterRowBuilder.FormatValueAndLimits(parameter.Key, 6, 3, 9);
            var bounds = range[(range.IndexOf('[') + 1)..].TrimEnd(']').Split(',');
            Assert.Equal(initialKd / 1000, double.Parse(bounds[0]), 12);
            Assert.Equal(initialKd * 1000, double.Parse(bounds[1]), 12);
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(6, parameter.Value); // Applying untouched text preserves the original coordinate.
            field.Text = enteredKd.ToString();
            Dispatcher.UIThread.RunJobs();
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(5, parameter.Value, 12); // 10 µM = 10000 nM = 10^-5 M.
            foreach (var invalid in new[] { "0", "-1" })
            {
                field.Text = invalid;
                Dispatcher.UIThread.RunJobs();
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.Equal(5, parameter.Value, 12);
            }

            parameter.Update(6);
            row = AnalysisParameterRowBuilder.BuildDesigner(parameter,
                (_, value) => parameter.Update(value), _ => { }, () => false);
            field = Assert.Single(row.GetLogicalDescendants().OfType<TextBox>());
            Assert.Equal(initialKd.ToString(), field.Text);
            field.Text = enteredKd.ToString();
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(5, parameter.Value, 12);
            var slider = Assert.Single(row.GetLogicalDescendants().OfType<Slider>());
            slider.Value = 0;
            Assert.Equal(9, parameter.Value, 12);
            Assert.Equal(initialKd / 1000, double.Parse(field.Text!), 12);
            slider.Value = 1;
            Assert.Equal(3, parameter.Value, 12);
            Assert.Equal(initialKd * 1000, double.Parse(field.Text!), 12);
        }
        finally
        {
            previous.ApplyToSettings();
            PlatformServices.RegisterSettingsStore(previousStore);
            CultureInfo.CurrentCulture = culture;
        }
    }
}
