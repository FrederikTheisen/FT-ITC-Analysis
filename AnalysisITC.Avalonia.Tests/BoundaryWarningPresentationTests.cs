using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

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

            var summaryText = TextFrom(workspace.SummaryPanelForTesting);
            Assert.Contains("Information criteria", summaryText);
            Assert.Contains("AIC", summaryText);
            Assert.Contains("Observations (n)", summaryText);
            Assert.Contains("Likelihood parameters (K)", summaryText);
            Assert.DoesNotContain("known observation sigmas", summaryText);
            Assert.DoesNotContain("includes estimated residual variance", summaryText);

            var parameterText = TextFrom(workspace.ParameterTableHostForTesting);
            Assert.DoesNotContain("reached a parameter boundary", parameterText);
        });
    }

    [Fact]
    public void ProfileSummaryUsesReadableFieldsWithoutRawDiagnostics()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateBoundaryResult(ErrorEstimationMethod.ProfileLikelihood);
            Assert.Equal(ErrorEstimationMethod.ProfileLikelihood, result.Solution.ErrorEstimationMethod);
            var member = result.Solution.Solutions[0];
            var id = new ProfileCoordinateId(ParameterType.Offset, ParameterBoundaryScope.Local,
                member.Data.UniqueID, 0);
            var coordinate = new ProfileCoordinateResult(id, 0, -10, 10,
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, -1),
                new ProfileSideResult(ProfileSideOutcome.BoundReachedBeforeCrossing,
                    warnings: new[] { "DistinctiveDiagnosticToken" }));
            var profile = new ProfileLikelihoodRunResult(.95,
                ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 4, 1, 1, 3,
                1, 2, SolverAlgorithm.NelderMead, false, 2, 20, 24, 40,
                TimeSpan.FromMilliseconds(1250), ErrorEstimationOutcome.PartialFailure,
                new[] { coordinate });
            var profileProperty = typeof(GlobalSolution).GetProperty(nameof(GlobalSolution.ProfileLikelihoodRun));
            var profileSetter = profileProperty?.GetSetMethod(nonPublic: true)
                ?? throw new InvalidOperationException("Global profile setter is unavailable.");
            profileSetter.Invoke(result.Solution, new object[] { profile });

            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            var summary = TextFrom(workspace.SummaryPanelForTesting);
            Assert.Contains("Profile likelihood", summary);
            Assert.Contains("Status", summary);
            Assert.Contains("95% CI endpoints", summary);
            Assert.Contains("Calculation time", summary);
            Assert.Contains("Unavailable", summary);
            Assert.Contains("1 of 2 found", summary);
            Assert.Contains(ProfileLikelihoodDisplayFormatter.Duration(TimeSpan.FromMilliseconds(1250)), summary);
            Assert.DoesNotContain("Diagnostics", summary);
            Assert.DoesNotContain("DistinctiveDiagnosticToken", summary);
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
