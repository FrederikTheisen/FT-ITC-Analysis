using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class BoundaryWarningPresentationTests
{
    public BoundaryWarningPresentationTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, "One or more bootstrap fits reached a parameter boundary.")]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, "One or more leave-one-out fits reached a parameter boundary.")]
    public void ExperimentInspectorShowsSharedBoundaryMessagesWithoutChangingParameterTable(
        ErrorEstimationMethod method,
        string errorEstimationMessage)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateBoundaryResult(method);
            var workspace = new AnalysisResultWorkspaceControl { Result = result };

            var experimentText = TextFrom(workspace.ExperimentsPanelForTesting);
            Assert.Contains("Best fit reached a parameter boundary.", experimentText);
            Assert.Contains(errorEstimationMessage, experimentText);
            Assert.Contains("Warning", TextFrom(workspace.SummaryPanelForTesting));

            var parameterText = TextFrom(workspace.ParameterTableHostForTesting);
            Assert.DoesNotContain("reached a parameter boundary", parameterText);
        });
    }

    static AnalysisResult CreateBoundaryResult(ErrorEstimationMethod method)
    {
        var experiment = new ExperimentData("boundary-warning.itc")
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
        };
        experiment.SetID("boundary-warning");
        experiment.Injections.Add(new InjectionData(experiment, volume: 1e-6)
        {
            IsIntegrated = true,
            Ratio = 1,
        });

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = method };
        var convergence = BoundaryConvergence(experiment);
        model.Solution = SolutionInterface.FromModel(model, convergence);
        model.Solution.ErrorMethod = method;

        var bootstrapModel = new OneSetOfSites(experiment);
        bootstrapModel.InitializeParameters(experiment);
        bootstrapModel.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = method };
        var bootstrap = SolutionInterface.FromModel(bootstrapModel, BoundaryConvergence(experiment));
        model.Solution.SetBootstrapSolutions(new List<SolutionInterface> { bootstrap });

        var solver = new Solver { Model = model, ErrorEstimationMethod = method };
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(solver));
    }

    static SolverConvergence BoundaryConvergence(ExperimentData experiment)
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        convergence.SetParameterBoundaryContacts(new[]
        {
            new ParameterBoundaryContact(
                ParameterType.Offset,
                ParameterBoundaryScope.Local,
                experiment.UniqueID,
                experiment.Name,
                ParameterBoundarySide.Upper,
                30000,
                30000),
        });
        return convergence;
    }

    static string[] TextFrom(Control root) => root
        .GetLogicalDescendants()
        .OfType<TextBlock>()
        .Select(text => text.Text ?? string.Empty)
        .ToArray();
}
