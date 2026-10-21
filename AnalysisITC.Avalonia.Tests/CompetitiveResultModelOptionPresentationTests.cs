using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class CompetitiveResultModelOptionPresentationTests
{
    [AvaloniaTheory]
    [InlineData(AttributeKey.PreboundLigandConc, true, "Unavailable")]
    [InlineData(AttributeKey.PreboundLigandEnthalpy, true, "Unavailable")]
    [InlineData(AttributeKey.PreboundLigandAffinity, true, "Unavailable")]
    [InlineData(AttributeKey.PreboundLigandConc, false, "0.0 µM")]
    [InlineData(AttributeKey.PreboundLigandEnthalpy, false, "0.0 kJ/mol")]
    [InlineData(AttributeKey.PreboundLigandAffinity, false, "1000000.0 µM")]
    public void ModelPageDistinguishesMissingValuesFromLegitimateZeros(AttributeKey key, bool missing, string expected)
    {
        var originalUnit = AppSettings.DefaultConcentrationUnit;
        var originalFamily = AppSettings.EnergyUnitFamily;
        var originalStyle = AppSettings.UncertaintyDisplayStyle;
        var originalPrecision = AppSettings.NumberPrecision;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            AppSettings.DefaultConcentrationUnit = ConcentrationUnit.µM;
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
            AppSettings.NumberPrecision = NumberPrecision.SingleDecimal;
            AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
            var model = CreateModel("missing-option", 2e-6, -32000);
            foreach (var option in model.ModelOptions.Values) option.BoolValue = false;
            model.ModelOptions[key].ParameterValue = missing ? FloatWithError.NaN : new FloatWithError(0);
            model.Solution = SolutionInterface.FromModel(model, Convergence());
            var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            var text = TextFrom(workspace.ModelPanelForTesting);

            Assert.Contains(expected, text);
            Assert.Equal(missing, FloatWithError.IsNaN(model.ModelOptions[key].ParameterValue));
        }
        finally
        {
            AppSettings.DefaultConcentrationUnit = originalUnit;
            AppSettings.EnergyUnitFamily = originalFamily;
            AppSettings.UncertaintyDisplayStyle = originalStyle;
            AppSettings.NumberPrecision = originalPrecision;
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [AvaloniaTheory]
    [InlineData(0, 7, "0", "7")]
    [InlineData(0.2, double.NaN, "0.2", "Unavailable")]
    [InlineData(0.25, 1, "One to tetramer (1:4)", "One to one (1:1)")]
    public void ModelPageShowsSavedStoichiometryWithoutDefaultPresetSubstitution(
        double firstValue, double secondValue, string firstExpected, string secondExpected)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var data = new ExperimentData("stored-stoichiometry.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
            };
            data.Injections.Add(new InjectionData(data, volume: 1e-6)
            {
                IsIntegrated = true,
                Ratio = 1,
                ActualCellConcentration = 10e-6,
                ActualTitrantConcentration = 10e-6,
            });
            var model = new TwoSetsOfSites(data);
            model.InitializeParameters(data);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            var first = model.ModelOptions[AttributeKey.NumberOfSites1];
            var second = model.ModelOptions[AttributeKey.NumberOfSites2];
            first.DoubleValue = firstValue;
            second.DoubleValue = secondValue;
            first.IntValue = second.IntValue = 7;
            model.Solution = SolutionInterface.FromModel(model, Convergence());
            var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));

            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            var text = TextFrom(workspace.ModelPanelForTesting);

            Assert.Contains(firstExpected, text);
            Assert.Contains(secondExpected, text);
            if (firstExpected != "One to one (1:1)" && secondExpected != "One to one (1:1)")
                Assert.DoesNotContain("One to one (1:1)", text);
            Assert.Equal(firstValue, first.DoubleValue);
            Assert.Equal(secondValue, second.DoubleValue);
            Assert.Equal(7, first.IntValue);
            Assert.Equal(7, second.IntValue);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModelPagePreservesIntervalsAndRefreshesDisplayPreferences(bool fromAttributes)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var originalUnit = AppSettings.DefaultConcentrationUnit;
            var originalFamily = AppSettings.EnergyUnitFamily;
            var originalStyle = AppSettings.UncertaintyDisplayStyle;
            var originalPrecision = AppSettings.NumberPrecision;
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                AppSettings.DefaultConcentrationUnit = ConcentrationUnit.µM;
                AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
                AppSettings.NumberPrecision = NumberPrecision.SingleDecimal;
                var models = new List<Model>
                {
                    CreateModel("saved-a", 1e-6, -32000),
                    CreateModel("saved-b", 1e-7, -16000),
                };
                for (var index = 0; index < models.Count; index++)
                {
                    var options = models[index].ModelOptions;
                    options[AttributeKey.PreboundLigandAffinity].BoolValue = fromAttributes;
                    // log10 Ka 6 [5, 7] => Kd 1 [0.1, 10] µM; the next member is tenfold tighter.
                    options[AttributeKey.PreboundLigandAffinity].ParameterValue = new FloatWithError(6 + index, .1, 5 + index, 7 + index);
                    options[AttributeKey.PreboundLigandEnthalpy].BoolValue = fromAttributes;
                    options[AttributeKey.PreboundLigandEnthalpy].ParameterValue = index == 0
                        ? new FloatWithError(-32000, 2000, -35000, -20000)
                        : new FloatWithError(-16000, 1000, -18000, -10000);
                    models[index].Solution = SolutionInterface.FromModel(models[index], Convergence());
                }
                var global = new GlobalModel(models) { ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
                var solution = new GlobalSolution(new GlobalSolver { Model = global },
                    models.Select(model => model.Solution).ToList(), Convergence());
                global.Solution = solution;
                var workspace = new AnalysisResultWorkspaceControl { Result = new AnalysisResult(solution) };
                var cases = new[]
                {
                    (UncertaintyDisplayStyle.ConfidenceInterval, "1.0 [0.1, 10.0] µM", "-32.0 [-35.0, -20.0] kJ/mol"),
                    (UncertaintyDisplayStyle.Automatic, "1.0 [0.1, 10.0] µM", "-32.0 [-35.0, -20.0] kJ/mol"),
                    (UncertaintyDisplayStyle.StandardDeviation, "1.0 ± 0.2 µM", "-32.0 ± 2.0 kJ/mol"),
                    (UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, "1.0 ± 0.2 [0.1, 10.0] µM", "-32.0 ± 2.0 [-35.0, -20.0] kJ/mol"),
                    (UncertaintyDisplayStyle.None, "1.0 µM", "-32.0 kJ/mol"),
                };
                foreach (var (style, affinity, enthalpy) in cases)
                {
                    AppSettings.UncertaintyDisplayStyle = style;
                    workspace.Refresh();
                    var sections = workspace.ModelPanelForTesting.Children.Select(TextFrom).ToArray();
                    Assert.Equal(fromAttributes ? 2 : 0, sections[0].Count(value => value == "From experiment attribute"));
                    Assert.Contains(affinity, sections[fromAttributes ? 1 : 0]);
                    Assert.Contains(enthalpy, sections[fromAttributes ? 1 : 0]);
                    if (fromAttributes && style == UncertaintyDisplayStyle.ConfidenceInterval)
                    {
                        Assert.Contains("0.1 [0.0, 1.0] µM", sections[2]);
                        Assert.Contains("-16.0 [-18.0, -10.0] kJ/mol", sections[2]);
                        CheckLayout(workspace);
                    }
                }
                AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.ConfidenceInterval;
                AppSettings.DefaultConcentrationUnit = ConcentrationUnit.nM;
                AppSettings.NumberPrecision = NumberPrecision.AllDecimals;
                workspace.Refresh();
                Assert.Contains("1000 [100, 10000] nM", TextFrom(workspace.ModelPanelForTesting));
                Assert.Equal(5, models[0].ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue.Lower);
            }
            finally
            {
                AppSettings.DefaultConcentrationUnit = originalUnit;
                AppSettings.EnergyUnitFamily = originalFamily;
                AppSettings.UncertaintyDisplayStyle = originalStyle;
                AppSettings.NumberPrecision = originalPrecision;
                CultureInfo.CurrentCulture = originalCulture;
            }
        });
    }

    static void CheckLayout(AnalysisResultWorkspaceControl workspace)
    {
        var panel = workspace.ModelPanelForTesting;
        var originalParent = Assert.IsType<Border>(panel.Parent);
        originalParent.Child = null;
        var host = new Border { Padding = new Thickness(12), Child = panel };
        var application = Application.Current!;
        var originalVariant = application.RequestedThemeVariant;
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        var colors = new ResourceInclude(new Uri("avares://FT-ITC Analysis/"))
        {
            Source = new Uri("avares://FT-ITC Analysis/Styling/AppTheme.axaml"),
        };
        application.Styles.Add(theme);
        application.Resources.MergedDictionaries.Add(colors);
        application.RequestedThemeVariant = ThemeVariant.Light;
        var window = new Window { Width = 330, Height = 900, Content = host };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(workspace.ModelPanelForTesting.IsEffectivelyVisible);
            var directory = Environment.GetEnvironmentVariable("FTITC_CR050_QA_DIRECTORY");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, "avalonia-model.png"));
            }
            foreach (var field in workspace.ModelPanelForTesting.GetLogicalDescendants().OfType<TextBlock>()
                .Where(field => field.Text?.Contains('[') == true))
            {
                Assert.True(field.Bounds.Width > 0, $"Unlaid interval row: {field.Text}; bounds {field.Bounds}");
                Assert.True(field.Bounds.Height >= field.FontSize, $"Clipped interval row: {field.Text}; bounds {field.Bounds}");
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
            host.Child = null;
            originalParent.Child = panel;
            application.RequestedThemeVariant = originalVariant;
            application.Styles.Remove(theme);
            application.Resources.MergedDictionaries.Remove(colors);
        }
    }

    [AvaloniaFact]
    public void ModelPageShowsSavedCompetitorValuesForEachExperiment()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var originalUnit = AppSettings.DefaultConcentrationUnit;
            var originalEnergyFamily = AppSettings.EnergyUnitFamily;
            try
            {
                AppSettings.DefaultConcentrationUnit = ConcentrationUnit.µM;
                AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
                var models = new List<Model>
                {
                    CreateModel("competitor-a", 2e-6, -32000),
                    CreateModel("competitor-b", 5e-6, -24000),
                };
                var solutions = models.Select(model =>
                {
                    model.Solution = SolutionInterface.FromModel(model, Convergence());
                    return model.Solution;
                }).ToList();
                var global = new GlobalModel(models)
                {
                    ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
                };
                var globalSolution = new GlobalSolution(
                    new GlobalSolver { Model = global }, solutions, Convergence());
                global.Solution = globalSolution;

                var workspace = new AnalysisResultWorkspaceControl
                {
                    Result = new AnalysisResult(globalSolution),
                };
                var sections = workspace.ModelPanelForTesting.Children
                    .Select(child => TextFrom(child))
                    .ToArray();

                Assert.Equal(2, sections[0].Count(value => value == "From experiment attribute"));
                Assert.Contains("2 µM", sections[1]);
                Assert.Contains("-32 kJ/mol", sections[1]);
                Assert.Contains("5 µM", sections[2]);
                Assert.Contains("-24 kJ/mol", sections[2]);
            }
            finally
            {
                AppSettings.DefaultConcentrationUnit = originalUnit;
                AppSettings.EnergyUnitFamily = originalEnergyFamily;
            }
        });
    }

    static Model CreateModel(string name, double kd, double enthalpy)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name,
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
        };
        data.Injections.Add(new InjectionData(data, volume: 1e-6)
        {
            IsIntegrated = true,
            Ratio = 1,
            ActualCellConcentration = 10e-6,
            ActualTitrantConcentration = 10e-6,
        });
        var model = new CompetitiveBinding(data);
        model.InitializeParameters(data);
        model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
        model.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = true;
        model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue = new FloatWithError(-System.Math.Log10(kd));
        model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue = true;
        model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue = new FloatWithError(enthalpy);
        return model;
    }

    static SolverConvergence Convergence() => SolverConvergence.FromSnapshot(
        new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 0.1,
        });

    static string[] TextFrom(Control root) => root
        .GetLogicalDescendants()
        .OfType<TextBlock>()
        .Select(text => text.Text ?? string.Empty)
        .ToArray();
}
