using System.Collections.Generic;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class PublicationFigureErrorBandTests
{
    [Fact]
    public void LeaveOneOutSavedRefitsProduceEnvelopeButProfileDoesNot()
    {
        var data = new ExperimentData("profile-envelope.itc")
        {
            CellConcentration = new FloatWithError(1e-3),
            SyringeConcentration = new FloatWithError(1e-3),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
        };
        for (var i = 0; i < 4; i++)
        {
            var injection = new InjectionData(data, i, 1e-6, 1e-9, include: true)
            {
                ActualCellConcentration = 10e-6,
                ActualTitrantConcentration = i * 2e-6,
            };
            injection.SetPeakArea(new FloatWithError(1e-6 * (i + 1), 1e-8));
            data.Injections.Add(injection);
        }

        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -10);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var primary = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        model.Solution = primary;
        data.UpdateSolution(model);
        SolutionInterface MakeRefit(double enthalpy)
        {
            var refitModel = new OneSetOfSites(data);
            refitModel.InitializeParameters(data);
            refitModel.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            refitModel.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, enthalpy);
            refitModel.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
            refitModel.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            return SolutionInterface.FromModel(refitModel, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        }
        primary.RestoreBootstrapSolutions(new List<SolutionInterface>
        {
            MakeRefit(-9), MakeRefit(-10), MakeRefit(-11),
        });

        primary.ErrorMethod = ErrorEstimationMethod.LeaveOneOut;
        var looDocument = PublicationFigureBuilder.Build(data, new PublicationFigureOptions
        {
            ShowThermogram = false,
            ShowResiduals = false,
            ShowFitParameters = false,
        });
        Assert.NotEmpty(looDocument.FitPanel.Bands);

        primary.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        var profileDocument = PublicationFigureBuilder.Build(data, new PublicationFigureOptions
        {
            ShowThermogram = false,
            ShowResiduals = false,
            ShowFitParameters = false,
        });
        Assert.Empty(profileDocument.FitPanel.Bands);
    }
}
