using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests;

/// <summary>
/// No binding detected and Inconclusive standard figures draw the fitted Offset on the current
/// experiment through the ordinary figure builder.
/// </summary>
[Collection("AutoSaveManager")]
public sealed class ClassifiedNullFigureTests
{
    const double Offset = 1234; // J/mol
    const double LossValue = 2.5e-7;

    // Independent molar deviations from the Offset (J/mol) and peak-area SDs (J).
    static double Deviation(int index) => 40 * (index % 3 - 1) + 5 * index;
    static double PeakSd(int index) => 1e-9 * (index + 1);

    public enum Axis { MolarRatio, Concentration, InjectionNumber }

    [Theory]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, Axis.MolarRatio, true)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, Axis.MolarRatio, false)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, Axis.Concentration, true)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, Axis.Concentration, false)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, Axis.InjectionNumber, true)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, Axis.MolarRatio, true)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, Axis.Concentration, false)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, Axis.InjectionNumber, false)]
    public void ClassifiedFigureMatchesOrdinaryOffsetFigure(BindingAssessmentOutcome outcome, Axis axis, bool corrected)
    {
        var result = SingleResult(outcome, out var member);
        var data = member.Data;
        ApplyAxis(data, axis);
        PublicationFigureOptions Options() => new()
        {
            DrawFitOffsetCorrected = corrected,
            EnergyUnitFamily = EnergyUnitFamily.Joules,
            ShowFitParameters = true,
            DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Offset,
        };

        var classified = Classified(data, member, result, Options());
        AssertSameFigure(Ordinary(data, Offset, Converged(), Options()), classified);

        var expectedTitle = axis switch
        {
            Axis.MolarRatio => AnalysisXAxisType.MolarRatio.GetEnumDescription(),
            Axis.Concentration => AnalysisXAxisType.TitrantConcentration.GetEnumDescription(),
            _ => AnalysisXAxisType.ID.GetEnumDescription(),
        };
        Assert.Equal(expectedTitle, classified.FitPanel.XAxis.Title);
        if (axis == Axis.MolarRatio)
            Assert.Equal(data.Injections.Select(injection => injection.Ratio), classified.FitPanel.Points.Select(point => point.X));
        if (axis == Axis.Concentration)
        {
            // 2 µM is plotted at 2 on the concentration axis.
            Assert.Equal(2, classified.FitPanel.Points[1].X, 12);
            for (var index = 0; index < data.Injections.Count; index++)
                Assert.Equal(index + 1, classified.FitPanel.Points[index].X, 12);
        }
        Assert.DoesNotContain(classified.Panels.SelectMany(panel => panel.AnnotationBoxes).SelectMany(box => box.Lines),
            line => line.Contains("Saved", StringComparison.Ordinal) || line.Contains("Current", StringComparison.Ordinal)
                || line == NullModelComparisonPresentation.OutcomeText(outcome));
    }

    [Theory]
    [InlineData(false, EnergyUnit.Joule, 1d)]
    [InlineData(true, EnergyUnit.Joule, 1d)]
    [InlineData(false, EnergyUnit.KCal, 1d / 4184d)]
    [InlineData(true, EnergyUnit.Cal, 1d / 4.184d)]
    public void ClassifiedFigureUsesCurrentObservationsAndFittedOffset(bool corrected, EnergyUnit unit, double scale)
    {
        var result = SingleResult(BindingAssessmentOutcome.NoBindingDetected, out var member);
        var data = member.Data;
        var figure = Classified(data, member, result, new PublicationFigureOptions
        {
            DrawFitOffsetCorrected = corrected,
            EnergyUnitFamily = EnergyUnitResolver.FamilyOf(unit),
            EnergyUnitOverride = unit,
        });

        var fit = Assert.IsType<PublicationFigurePanel>(figure.FitPanel);
        Assert.Equal(unit, figure.ResolvedEnergyUnit);
        Assert.Equal(data.Injections.Count, fit.Points.Count);
        var line = Assert.Single(fit.Series);
        var residuals = Assert.IsType<PublicationFigurePanel>(figure.ResidualPanel);
        for (var index = 0; index < data.Injections.Count; index++)
        {
            var mass = data.Injections[index].InjectionMass;
            var observed = (Offset + Deviation(index)) * mass; // J
            var predicted = Offset * mass;                     // J, q = b × m
            var shown = corrected ? observed / mass - Offset : observed / mass;
            var sd = PeakSd(index) / mass;
            Assert.Equal(shown * scale, fit.Points[index].Y, 6);
            Assert.Equal((shown - sd) * scale, fit.Points[index].LowerY, 6);
            Assert.Equal((shown + sd) * scale, fit.Points[index].UpperY, 6);
            Assert.Equal((corrected ? 0 : predicted / mass) * scale, line.Points[index].Y, 6);
            Assert.Equal((observed - predicted) / mass * scale, residuals.Points[index].Y, 6);
        }
        Assert.Empty(fit.Bands);
    }

    public static IEnumerable<object[]> DisplayCases() => new[]
    {
        "excluded-hidden", "excluded-shown", "custom-titles", "explicit-limits", "no-error-bars",
        "no-fit-line", "no-residuals", "no-parameters", "model-and-offset", "experiment-details",
        "kcal-override", "fit-panel-hidden",
    }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(DisplayCases))]
    public void ClassifiedFigureHonorsOrdinaryDisplayControls(string name)
    {
        var result = SingleResult(BindingAssessmentOutcome.NoBindingDetected, out var member);
        var data = member.Data;
        if (name.StartsWith("excluded", StringComparison.Ordinal)) data.Injections[1].Include = false;
        PublicationFigureOptions Options()
        {
            var options = new PublicationFigureOptions
            {
                EnergyUnitFamily = EnergyUnitFamily.Joules,
                DisplayParameters = FinalFigureDisplayParameters.Offset,
            };
            switch (name)
            {
                case "excluded-hidden": options.ShowBadData = false; break;
                case "excluded-shown": options.ShowBadData = true; options.ShowBadDataErrorBars = true; break;
                case "custom-titles":
                    options.XAxisTitle = "Custom x";
                    options.EnthalpyAxisTitle = "Heat (<unit>)";
                    break;
                case "explicit-limits":
                    options.FitXAxisMinimum = -1; options.FitXAxisMaximum = 9;
                    options.FitYAxisMinimum = -5000; options.FitYAxisMaximum = 5000;
                    options.ResidualYAxisMinimum = -300; options.ResidualYAxisMaximum = 300;
                    break;
                case "no-error-bars": options.ShowErrorBars = false; break;
                case "no-fit-line": options.ShowFitLine = false; break;
                case "no-residuals": options.ShowResiduals = false; break;
                case "no-parameters": options.ShowFitParameters = false; break;
                case "model-and-offset":
                    options.DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Offset;
                    break;
                case "experiment-details":
                    options.ShowExperimentDetails = true;
                    options.DisplayParameters = FinalFigureDisplayParameters.Concentrations | FinalFigureDisplayParameters.Temperature;
                    break;
                case "kcal-override": options.EnergyUnitFamily = EnergyUnitFamily.Calories; options.EnergyUnitOverride = EnergyUnit.KCal; break;
                case "fit-panel-hidden": options.ShowFitPanel = false; break;
            }
            return options;
        }

        var classified = Classified(data, member, result, Options());
        AssertSameFigure(Ordinary(data, Offset, Converged(), Options()), classified);

        var lines = classified.Panels.SelectMany(panel => panel.AnnotationBoxes).SelectMany(box => box.Lines).ToList();
        switch (name)
        {
            case "excluded-hidden":
                Assert.Equal(data.Injections.Count - 1, classified.FitPanel.Points.Count);
                break;
            case "excluded-shown":
                Assert.False(classified.FitPanel.Points[1].Included);
                break;
            case "custom-titles":
                Assert.Equal("Custom x", classified.FitPanel.XAxis.Title);
                Assert.Equal("Heat (" + classified.ResolvedEnergyUnit.GetUnit() + "/mol)", classified.FitPanel.YAxis.Title);
                break;
            case "explicit-limits":
                Assert.Equal(-1, classified.FitPanel.XAxis.Minimum);
                Assert.Equal(9, classified.FitPanel.XAxis.Maximum);
                Assert.Equal(-5000, classified.FitPanel.YAxis.Minimum);
                Assert.Equal(300, classified.ResidualPanel.YAxis.Maximum);
                break;
            case "no-error-bars":
                Assert.All(classified.FitPanel.Points, point => Assert.Equal(point.Y, point.UpperY));
                break;
            case "no-fit-line": Assert.Empty(classified.FitPanel.Series); break;
            case "no-residuals": Assert.Null(classified.ResidualPanel); break;
            case "no-parameters": Assert.Empty(lines); break;
            case "model-and-offset":
                Assert.Equal("Offset | RMSD = " + LossValue.ToString("G3"), lines[0]);
                Assert.Contains(lines, line => line.StartsWith("Offset = ", StringComparison.Ordinal));
                break;
            case "kcal-override": Assert.Equal(EnergyUnit.KCal, classified.ResolvedEnergyUnit); break;
            case "fit-panel-hidden": Assert.Null(classified.FitPanel); break;
        }
    }

    [Fact]
    public async Task ReopenedProjectDrawsTheSameFigureAndFollowsLaterEdits()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        model.Data.Model = model;
        SetCurrentObservations(model.Data);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        var member = result.Solution.Solutions[0];
        Assert.True(result.NullComparison.NullFitSucceeded, result.NullComparison.NullFitReason);
        PublicationFigureOptions Options() => new()
        {
            EnergyUnitFamily = EnergyUnitFamily.Joules,
            EnergyUnitOverride = EnergyUnit.Joule,
            DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Offset,
        };
        var before = Classified(model.Data, member, result, Options());

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data }, new[] { result });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
        var restoredMember = restored.Solution.Solutions[0];
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, restored.BindingAssessment.EffectiveOutcome);
        var reopened = Classified(restoredMember.Data, restoredMember, restored, Options());
        AssertSameFigure(before, reopened);

        foreach (var data in new[] { model.Data, restoredMember.Data })
        {
            data.Injections[0].SetPeakArea(new FloatWithError(data.Injections[0].PeakArea.Value * 1.5, 7e-9));
            data.Injections[2].Include = false;
            data.SyringeConcentration = new FloatWithError(data.SyringeConcentration.Value * 1.25);
        }
        var editedLive = Classified(model.Data, member, result, Options());
        var editedReopened = Classified(restoredMember.Data, restoredMember, restored, Options());
        AssertSameFigure(editedLive, editedReopened);
        var offset = result.NullComparison.Members.Single().Offset;
        AssertSameFigure(Ordinary(model.Data, offset, member.NullComparison.NullSolutions.Single().Convergence, Options()),
            editedLive);
        var edited = model.Data.Injections[0];
        Assert.Equal(edited.PeakArea.Value / edited.InjectionMass - offset, editedLive.FitPanel.Points[0].Y, 8);
        Assert.Equal(edited.PeakArea.SD / edited.InjectionMass, editedLive.FitPanel.Points[0].UpperY - editedLive.FitPanel.Points[0].Y, 8);
        Assert.False(editedLive.FitPanel.Points[2].Included);
    }

    [Fact]
    public void IndependentMembersUseTheirOwnOutcomeAndOffset()
    {
        var result = IndependentResult(out var members);
        Restore(result, members[0], MemberComparison(members[0].Data, 7, 900));
        Restore(result, members[1], MemberComparison(members[1].Data, 12, 1500));
        Assert.Equal(BindingAssessmentOutcome.Inconclusive, result.GetMemberBindingAssessment(members[0]).EffectiveOutcome);
        Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.GetMemberBindingAssessment(members[1]).EffectiveOutcome);
        PublicationFigureOptions Options() => new() { EnergyUnitFamily = EnergyUnitFamily.Joules };

        var nullFigure = Classified(members[0].Data, members[0], result, Options());
        AssertSameFigure(Ordinary(members[0].Data, 900, Converged(), Options()), nullFigure);

        var bindingFigure = Classified(members[1].Data, members[1], result, Options());
        AssertSameFigure(PublicationFigureBuilder.Build(new PublicationFigureSource(members[1].Data, members[1]), Options()),
            bindingFigure);

        var diagnostic = PublicationFigureBuilder.Build(new PublicationFigureSource(members[0].Data, members[0], result,
            ResultOutputPurpose.Diagnostic), Options());
        Assert.Equal(PublicationFigureBuilder.Build(new PublicationFigureSource(members[0].Data, members[0]), Options())
            .FitPanel.Series.Single().Points.Select(point => point.Y), diagnostic.FitPanel.Series.Single().Points.Select(point => point.Y));
        Assert.Contains("Binding-fit diagnostics", diagnostic.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines));
    }

    [Fact]
    public void PooledMembersMatchTheirOwnExperimentInTheResultComparison()
    {
        var first = PreparedModel("First");
        var second = PreparedModel("Second");
        var globalModel = new GlobalModel(new List<Model> { first, second }) { Parameters = new GlobalModelParameters() };
        globalModel.Parameters.AddIndivdualParameter(first.Parameters);
        globalModel.Parameters.AddIndivdualParameter(second.Parameters);
        globalModel.Parameters.GlobalTable[ParameterType.Enthalpy1] = new Parameter(ParameterType.Enthalpy1, -40000);
        Assert.False(globalModel.ShouldFitIndividually);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel },
            new List<SolutionInterface> { first.Solution, second.Solution },
            SolverConvergence.FromMultiExperimentAnalysis(new List<SolverConvergence> { first.Solution.Convergence, second.Solution.Convergence }),
            reconstructBootstrap: false);
        globalModel.Solution = global;
        var result = new AnalysisResult(global);
        Assert.False(result.IsIndependentAssessmentCollection);
        var comparison = MemberComparison(second.Data, 3, 1700);
        comparison.Members.Insert(0, Member(first.Data.UniqueID, 800));
        SetResultComparison(result, comparison);
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        PublicationFigureOptions Options() => new() { EnergyUnitFamily = EnergyUnitFamily.Joules };

        AssertSameFigure(Ordinary(first.Data, 800, Converged(), Options()),
            Classified(first.Data, result.Solution.Solutions[0], result, Options()));
        AssertSameFigure(Ordinary(second.Data, 1700, Converged(), Options()),
            Classified(second.Data, result.Solution.Solutions[1], result, Options()));
    }

    public static IEnumerable<object[]> UnavailableCases() => new[]
    {
        "no-comparison", "failed-fit", "no-matching-member", "duplicate-member", "non-finite-offset",
    }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(UnavailableCases))]
    public void UnavailableNullFitDrawsCurrentObservationsWithoutPrediction(string name)
    {
        var result = SingleResult(BindingAssessmentOutcome.NoBindingDetected, out var member);
        var data = member.Data;
        var comparison = MemberComparison(data, 3, Offset);
        switch (name)
        {
            case "no-comparison": comparison = null; break;
            case "failed-fit": comparison.NullFitSucceeded = false; break;
            case "no-matching-member": comparison.Members[0].ExperimentId = "another-experiment"; break;
            case "duplicate-member": comparison.Members.Add(Member(data.UniqueID, Offset + 10)); break;
            case "non-finite-offset": comparison.Members[0].Offset = double.NaN; break;
        }
        SetResultComparison(result, comparison);

        var figure = Classified(data, member, result, new PublicationFigureOptions
        {
            EnergyUnitFamily = EnergyUnitFamily.Joules,
            EnergyUnitOverride = EnergyUnit.Joule,
            DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Offset,
        });

        var fit = Assert.IsType<PublicationFigurePanel>(figure.FitPanel);
        Assert.Equal(new[] { "Offset fit unavailable" }, fit.AnnotationBoxes.SelectMany(box => box.Lines));
        Assert.Empty(fit.Series);
        Assert.Empty(fit.Bands);
        Assert.Null(figure.ResidualPanel);
        Assert.Equal(data.Injections.Select((_, index) => Offset + Deviation(index)),
            fit.Points.Select(point => Math.Round(point.Y, 6)));
        Assert.Contains("Model: ", figure.MetadataKeywords);
        Assert.DoesNotContain(figure.MetadataKeywords, keyword => keyword.StartsWith("Offset", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingConvergenceOmitsRmsdWithoutInventingAValue()
    {
        var result = SingleResult(BindingAssessmentOutcome.NoBindingDetected, out var member);
        var comparison = MemberComparison(member.Data, 3, Offset);
        comparison.Members[0].Convergence = null;
        SetResultComparison(result, comparison);
        var options = new PublicationFigureOptions
        {
            EnergyUnitFamily = EnergyUnitFamily.Joules,
            DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Offset,
        };

        var figure = Classified(member.Data, member, result, options);

        var lines = figure.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines).ToList();
        Assert.Equal("Offset", lines[0]);
        Assert.DoesNotContain(lines, line => line.Contains("RMSD", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("Offset = ", StringComparison.Ordinal));
        Assert.Contains("Loss: ", figure.MetadataKeywords);
        Assert.Contains("Model: Offset", figure.MetadataKeywords);
        Assert.NotNull(figure.ResidualPanel);
    }

    [Fact]
    public void NonFiniteBindingParametersAreNotUsedForTheOffsetFigure()
    {
        var result = SingleResult(BindingAssessmentOutcome.NoBindingDetected, out var member);
        foreach (var key in member.Parameters.Keys.ToList()) member.Parameters[key] = FloatWithError.NaN;
        foreach (var parameter in member.Model.Parameters.Table.Values) parameter.Update(double.NaN);
        PublicationFigureOptions Options() => new()
        {
            EnergyUnitFamily = EnergyUnitFamily.Joules,
            EnergyUnitOverride = EnergyUnit.Joule,
            DrawFitOffsetCorrected = false,
            DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Offset,
        };

        var figure = Classified(member.Data, member, result, Options());

        AssertSameFigure(Ordinary(member.Data, Offset, Converged(), Options()), figure);
        Assert.All(figure.FitPanel.Points.Concat(figure.ResidualPanel.Points), point => Assert.True(double.IsFinite(point.Y)));
        Assert.All(figure.FitPanel.Series.Single().Points, point => Assert.Equal(Offset, point.Y, 8));
    }

    [Fact]
    public void UnavailableCanvasCellDoesNotChangeOptionsForLaterCells()
    {
        var unavailable = SingleResult(BindingAssessmentOutcome.NoBindingDetected, out var unavailableMember);
        SetResultComparison(unavailable, null);
        var valid = SingleResult(BindingAssessmentOutcome.NoBindingDetected, out var validMember);
        var plainModel = PreparedModel("Plain");
        var options = new PublicationFigureOptions
        {
            EnergyUnitFamily = EnergyUnitFamily.Joules,
            ShowFitParameters = true,
            DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Offset,
        };
        var before = (options.ShowFitParameters, options.ShowResiduals, options.ShowFitLine, options.ShowConfidenceBand,
            options.DrawFitOffsetCorrected, options.DisplayParameters, options.ShowErrorBars);

        var canvas = PublicationFigureCanvasBuilder.BuildSources(new[]
        {
            new PublicationFigureSource(unavailableMember.Data, unavailableMember, unavailable),
            new PublicationFigureSource(validMember.Data, validMember, valid),
            new PublicationFigureSource(plainModel.Data, plainModel.Solution),
        }, options, new PublicationFigureCanvasOptions { Columns = 3, Rows = 1 });
        var figures = canvas.Cells.Select(cell => PublicationFigureBuilder.Build(cell.Source, canvas.FigureOptions)).ToList();

        Assert.Contains("Offset fit unavailable", figures[0].FitPanel.AnnotationBoxes.SelectMany(box => box.Lines));
        PublicationFigureOptions Fresh() => new()
        {
            EnergyUnitFamily = EnergyUnitFamily.Joules,
            ShowFitParameters = true,
            DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Offset,
        };
        AssertSameFigure(Ordinary(validMember.Data, Offset, Converged(), Fresh()), figures[1]);
        AssertSameFigure(PublicationFigureBuilder.Build(new PublicationFigureSource(plainModel.Data, plainModel.Solution), Fresh()),
            figures[2]);
        Assert.NotEmpty(figures[1].FitPanel.Series);
        Assert.NotNull(figures[1].ResidualPanel);
        Assert.Equal(before, (options.ShowFitParameters, options.ShowResiduals, options.ShowFitLine, options.ShowConfidenceBand,
            options.DrawFitOffsetCorrected, options.DisplayParameters, options.ShowErrorBars));
    }

    [Fact]
    public void RenderingLeavesAnalysisStateUntouched()
    {
        var model = PreparedModel("State");
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        var member = result.Solution.Solutions[0];
        var data = member.Data;
        var comparison = result.NullComparison;
        var nullSolution = comparison.NullSolutions.Single();
        var attached = data.Model;
        var attachedSolution = attached.Solution;
        var bindingTable = attached.Parameters.Table.ToDictionary(item => item.Key, item => item.Value.Value);
        var memberParameters = member.Parameters.ToDictionary(item => item.Key, item => item.Value);
        var nullModel = nullSolution.Model;
        var nullParameters = nullSolution.Parameters.ToDictionary(item => item.Key, item => item.Value);
        var nullModelOffset = nullModel.Parameters.Table[ParameterType.Offset].Value;
        var memberOffset = comparison.Members.Single().Offset;
        var points = comparison.Members.Single().Points.Select(point => (point.ObservedHeatJoules, point.PredictedHeatJoules, point.Ratio)).ToList();
        var criteria = (comparison.BindingInformationCriteria, comparison.NullInformationCriteria, comparison.DeltaAicc);
        var assessment = result.BindingAssessment;
        var peakAreas = data.Injections.Select(injection => injection.PeakArea).ToList();

        var figure = Classified(data, member, result, new PublicationFigureOptions
        {
            DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Offset,
        });
        Assert.NotEmpty(figure.FitPanel.Series);

        Assert.Same(attached, data.Model);
        Assert.Same(attachedSolution, data.Model.Solution);
        Assert.Equal(AnalysisXAxisType.MolarRatio, data.AxisType);
        Assert.Equal(bindingTable, attached.Parameters.Table.ToDictionary(item => item.Key, item => item.Value.Value));
        Assert.Equal(memberParameters, member.Parameters.ToDictionary(item => item.Key, item => item.Value));
        Assert.Same(nullSolution, comparison.NullSolutions.Single());
        Assert.Same(nullModel, nullSolution.Model);
        Assert.Equal(nullParameters, nullSolution.Parameters.ToDictionary(item => item.Key, item => item.Value));
        Assert.Equal(nullModelOffset, nullModel.Parameters.Table[ParameterType.Offset].Value);
        Assert.Equal(memberOffset, comparison.Members.Single().Offset);
        Assert.Equal(points, comparison.Members.Single().Points.Select(point => (point.ObservedHeatJoules, point.PredictedHeatJoules, point.Ratio)));
        Assert.Equal(criteria, (comparison.BindingInformationCriteria, comparison.NullInformationCriteria, comparison.DeltaAicc));
        Assert.Same(assessment, result.BindingAssessment);
        Assert.Same(comparison, result.NullComparison);
        Assert.Equal(peakAreas, data.Injections.Select(injection => injection.PeakArea));
    }

    [Fact]
    public void DisplaySolutionPreservesStoredNullUncertaintyAndWeighting()
    {
        var result = SingleResult(BindingAssessmentOutcome.NoBindingDetected, out var member);
        var comparison = MemberComparison(member.Data, 3, Offset);
        var attachedSolution = member.Data.Model.Solution;
        var stored = OffsetSolution(member.Data, Offset, Converged());
        stored.Parameters[ParameterType.Offset] = new FloatWithError(Offset, 12);
        stored.UseWeightedFitting = true;
        comparison.NullSolutions.Add(stored);
        SetResultComparison(result, comparison);

        var source = new PublicationFigureSource(member.Data, member, result);
        Assert.True(PublicationFigureBuilder.TryResolveNullFit(source, out var fitMember, out var nullSolution, out var weighted));
        var display = PublicationFigureBuilder.CreateNullDisplaySolution(member.Data, fitMember, nullSolution, weighted);

        Assert.Same(stored, nullSolution);
        Assert.NotSame(stored, display);
        Assert.Equal(12, display.Parameters[ParameterType.Offset].SD);
        Assert.Equal(Offset, display.Model.Parameters.Table[ParameterType.Offset].Value);
        Assert.True(display.UseWeightedFitting);
        Assert.Same(stored.Convergence, display.Convergence);
        Assert.Same(member.Data, display.Data);
        Assert.Same(attachedSolution, member.Data.Model.Solution);
    }

    // Fixtures

    static PublicationFigureDocument Classified(ExperimentData data, SolutionInterface member, AnalysisResult result,
        PublicationFigureOptions options)
        => PublicationFigureBuilder.Build(new PublicationFigureSource(data, member, result, ResultOutputPurpose.Standard), options);

    static PublicationFigureDocument Ordinary(ExperimentData data, double offset, SolverConvergence convergence,
        PublicationFigureOptions options)
        => PublicationFigureBuilder.Build(new PublicationFigureSource(data, OffsetSolution(data, offset, convergence)), options);

    static SolutionInterface OffsetSolution(ExperimentData data, double offset, SolverConvergence convergence)
    {
        var model = new Offset(data) { ReuseAttachedSolutionInitialValues = false };
        model.InitializeParameters(data);
        model.Parameters.Table[ParameterType.Offset].Update(offset);
        model.Solution = SolutionInterface.FromModel(model, convergence);
        return model.Solution;
    }

    static SolverConvergence Converged() => SolverConvergence.FromSnapshot(ConvergedSnapshot());

    static SolverConvergenceSnapshot ConvergedSnapshot() => new()
    {
        Algorithm = SolverAlgorithm.LevenbergMarquardt,
        Termination = SolverTermination.Converged,
        Loss = LossValue,
    };

    static Model PreparedModel(string name)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        model.Data.Name = name;
        model.Data.Model = model;
        SetCurrentObservations(model.Data);
        return model;
    }

    static void SetCurrentObservations(ExperimentData data)
    {
        for (var index = 0; index < data.Injections.Count; index++)
        {
            var injection = data.Injections[index];
            injection.SetPeakArea(new FloatWithError((Offset + Deviation(index)) * injection.InjectionMass, PeakSd(index)));
        }
    }

    static AnalysisResult SingleResult(BindingAssessmentOutcome outcome, out SolutionInterface member)
    {
        var model = PreparedModel("Classified");
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        member = result.Solution.Solutions[0];
        var comparison = MemberComparison(model.Data, outcome == BindingAssessmentOutcome.Inconclusive ? 7 : 0, Offset);
        SetResultComparison(result, comparison);
        typeof(AnalysisResult).GetProperty(nameof(AnalysisResult.BindingAssessment))!.GetSetMethod(true)!
            .Invoke(result, new object[] { BindingAssessmentState.FromComparison(comparison) });
        Assert.Equal(outcome, result.BindingAssessment.EffectiveOutcome);
        return result;
    }

    static AnalysisResult IndependentResult(out SolutionInterface[] members)
    {
        var models = new[] { PreparedModel("First"), PreparedModel("Second") };
        var globalModel = new GlobalModel(models.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        members = models.Select(model => model.Solution).ToArray();
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, members.ToList(),
            SolverConvergence.FromMultiExperimentAnalysis(members.Select(solution => solution.Convergence).ToList()),
            reconstructBootstrap: false);
        globalModel.Solution = global;
        var result = new AnalysisResult(global);
        Assert.True(result.IsIndependentAssessmentCollection);
        return result;
    }

    static void Restore(AnalysisResult result, SolutionInterface member, NullModelComparison comparison)
        => result.RestoreMemberComparisonAndAssessment(member.Guid, comparison, BindingAssessmentState.FromComparison(comparison));

    static NullModelComparison MemberComparison(ExperimentData data, double deltaAicc, double offset) => new()
    {
        BindingFitSucceeded = true,
        NullFitSucceeded = true,
        BindingInformationCriteria = Criteria(100),
        NullInformationCriteria = Criteria(100 + deltaAicc),
        DeltaAicc = deltaAicc,
        Members = new List<NullModelComparisonMember> { Member(data.UniqueID, offset) },
    };

    static NullModelComparisonMember Member(string experimentId, double offset) => new()
    {
        ExperimentId = experimentId,
        Offset = offset,
        Scope = "local",
        Convergence = ConvergedSnapshot(),
        // Historical evidence that must not be drawn.
        Points = new List<NullModelComparisonPoint>
        {
            new() { InjectionId = 0, InjectionMass = 1e-9, Ratio = 999,
                ObservedHeatJoules = 9e-3, PredictedHeatJoules = 8e-3, Included = true },
        },
    };

    static FitInformationCriteria Criteria(double aicc) => FitInformationCriteria.Restore(
        observationCount: 20, fittedParameterCount: 2, likelihoodParameterCount: 3,
        likelihoodMode: GaussianLikelihoodMode.EstimatedCommonVariance, minusTwoLogLikelihood: 90,
        aic: aicc - 4, aicc: aicc, isAicAvailable: true, isAiccAvailable: true,
        aicUnavailableReason: "", aiccUnavailableReason: "", rawResidualSumOfSquares: 1,
        residualRmsdMicrojoules: 1, standardizedResidualSumOfSquares: 1, logSigmaSquaredSum: 1);

    static void SetResultComparison(AnalysisResult result, NullModelComparison comparison)
        => typeof(AnalysisResult).GetProperty(nameof(AnalysisResult.NullComparison), BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(true)!.Invoke(result, new object[] { comparison });

    static void ApplyAxis(ExperimentData data, Axis axis)
    {
        switch (axis)
        {
            case Axis.Concentration:
                data.CellConcentration = new FloatWithError(0);
                for (var index = 0; index < data.Injections.Count; index++)
                    data.Injections[index].ActualTitrantConcentration = (index + 1) * 1e-6;
                break;
            case Axis.InjectionNumber:
                data.CellConcentration = new FloatWithError(0);
                data.SyringeConcentration = new FloatWithError(0);
                break;
        }
    }

    static void AssertSameFigure(PublicationFigureDocument expected, PublicationFigureDocument actual)
    {
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.ResolvedEnergyUnit, actual.ResolvedEnergyUnit);
        Assert.Equal(expected.ResolvedHeatCapacityUnit, actual.ResolvedHeatCapacityUnit);
        Assert.Equal(expected.MetadataKeywords, actual.MetadataKeywords);
        AssertSamePanel(expected.ThermogramPanel, actual.ThermogramPanel);
        AssertSamePanel(expected.FitPanel, actual.FitPanel);
        AssertSamePanel(expected.ResidualPanel, actual.ResidualPanel);
    }

    static void AssertSamePanel(PublicationFigurePanel expected, PublicationFigurePanel actual)
    {
        if (expected == null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        Assert.Equal(expected.Kind, actual.Kind);
        AssertSameAxis(expected.XAxis, actual.XAxis);
        AssertSameAxis(expected.YAxis, actual.YAxis);
        Assert.Equal(expected.DrawZeroLine, actual.DrawZeroLine);
        Assert.Equal(expected.AutoAnnotationBoxUpper, actual.AutoAnnotationBoxUpper);
        Assert.Equal(expected.Points.Select(point => (point.X, point.Y, point.LowerY, point.UpperY, point.Included)),
            actual.Points.Select(point => (point.X, point.Y, point.LowerY, point.UpperY, point.Included)));
        Assert.Equal(expected.Series.Count, actual.Series.Count);
        for (var index = 0; index < expected.Series.Count; index++)
        {
            Assert.Equal(expected.Series[index].Role, actual.Series[index].Role);
            Assert.Equal(expected.Series[index].Points.Select(point => (point.X, point.Y)),
                actual.Series[index].Points.Select(point => (point.X, point.Y)));
        }
        Assert.Equal(expected.Bands.Count, actual.Bands.Count);
        Assert.Equal(expected.AnnotationBoxes.Select(box => (box.Placement, string.Join("\n", box.Lines))),
            actual.AnnotationBoxes.Select(box => (box.Placement, string.Join("\n", box.Lines))));
    }

    static void AssertSameAxis(PublicationAxis expected, PublicationAxis actual)
    {
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Minimum, actual.Minimum);
        Assert.Equal(expected.Maximum, actual.Maximum);
        Assert.Equal(expected.MajorTicks, actual.MajorTicks);
    }
}
