using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Avalonia.Details;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class MolarRmsdPresentationTests
{
    public MolarRmsdPresentationTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Theory]
    [InlineData(EnergyUnitFamily.Joules, 2500, "2.5 kJ/mol")]
    [InlineData(EnergyUnitFamily.Calories, 2500, "0.598 kcal/mol")]
    [InlineData(EnergyUnitFamily.Joules, 25, "25 J/mol")]
    [InlineData(EnergyUnitFamily.Calories, 25, "5.98 cal/mol")]
    public void ResultSummaryDisplaysPersistedMolarRmsdInSelectedFamily(
        EnergyUnitFamily family,
        double molarRmsdJoulesPerMole,
        string expectedValue)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var originalFamily = AppSettings.EnergyUnitFamily;
            try
            {
                AppSettings.EnergyUnitFamily = family;
                var workspace = new AnalysisResultWorkspaceControl
                {
                    Result = CreateResult(molarRmsdJoulesPerMole),
                };
                var details = new AnalysisResultDetailsWindow(workspace.Result);

                var text = TextFrom(workspace.SummaryPanelForTesting);
                Assert.Contains("Molar RMSD", text);
                var localizedExpected = expectedValue.Replace(
                    ".",
                    CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
                Assert.Equal(new[] { localizedExpected }, text.Where(value => value.Contains("/mol")).ToArray());
                Assert.Contains("Molar RMSD", TextFrom(details));
                Assert.Contains(localizedExpected, TextFrom(details));

                var label = workspace.SummaryPanelForTesting
                    .GetLogicalDescendants()
                    .OfType<TextBlock>()
                    .Single(value => value.Text == "Molar RMSD");
                Assert.Contains("not used for optimisation", ToolTip.GetTip(label)?.ToString());
            }
            finally
            {
                AppSettings.EnergyUnitFamily = originalFamily;
            }
        });
    }

    [Fact]
    public void ResultSummaryOmitsMolarRmsdWhenItWasNotCaptured()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var workspace = new AnalysisResultWorkspaceControl
            {
                Result = CreateResult(null),
            };
            var details = new AnalysisResultDetailsWindow(workspace.Result);

            Assert.DoesNotContain("Molar RMSD", TextFrom(workspace.SummaryPanelForTesting));
            Assert.DoesNotContain("Molar RMSD", TextFrom(details));
        });
    }

    static AnalysisResult CreateResult(double? molarRmsdJoulesPerMole)
    {
        var experiment = new ExperimentData("molar-rmsd.itc")
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
        };
        experiment.SetID(Guid.NewGuid().ToString());
        var injection = new InjectionData(experiment, volume: 2e-6)
        {
            IsIntegrated = true,
            Ratio = 1,
        };
        injection.SetPeakArea(new FloatWithError(-1e-6));
        experiment.Injections.Add(injection);

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 1,
            MolarRmsdJoulesPerMole = molarRmsdJoulesPerMole,
        });
        model.Solution = SolutionInterface.FromModel(model, convergence);

        var solver = new Solver { Model = model };
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(solver));
    }

    static string[] TextFrom(Control root) => root
        .GetLogicalDescendants()
        .OfType<TextBlock>()
        .Select(text => text.Text ?? string.Empty)
        .ToArray();
}
