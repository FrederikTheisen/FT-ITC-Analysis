using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class LinearDependenceEnvelopeTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoBufferGraphAndReportRetainTheSameCenterAndCornerEnvelope(bool uncertainty)
    {
        var previousFamily = AppSettings.EnergyUnitFamily;
        try
        {
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
            var result = CreateResult(saltSeries: false);
            result.ProtonationAnalysis.RestoreResult(
                uncertainty ? new FloatWithError(1000, 250, 500, 1500) : new FloatWithError(1000),
                uncertainty ? new FloatWithError(0.3, 0.05, 0.2, 0.4) : new FloatWithError(0.3),
                100, DateTime.UtcNow, ErrorEstimationMethod.BootstrapResiduals);
            var graph = new ResultDependenceGraphControl { Result = result, Mode = ResultAnalysisViewMode.Protonation };
            var fit = GraphFit(graph);
            // MES/Hepes at 25 C: midpoint x = 17600 J/mol, displayed in kJ/mol.
            AssertCoordinate(Points(fit, "Line")[150], 17.6, 6.28);
            if (uncertainty)
            {
                AssertCoordinate(Points(fit, "Lower")[150], 17.6, 4.02);
                AssertCoordinate(Points(fit, "Upper")[150], 17.6, 8.54);
            }
            else
            {
                Assert.Empty(Points(fit, "Lower"));
                Assert.Empty(Points(fit, "Upper"));
            }
            var line = ReportLine(result, AnalysisReportAdvancedSectionKind.Protonation, "Protonation dependence");
            var midpoint = line.Points[40];
            Near(midpoint.X, 17.6);
            Near(midpoint.Y, 6.28);
            if (uncertainty)
            {
                Near(midpoint.Lower!.Value, 4.02);
                Near(midpoint.Upper!.Value, 8.54);
            }
            else Assert.All(line.Points, point => { Assert.Null(point.Lower); Assert.Null(point.Upper); });
            AssertFinite(fit, line);
        }
        finally { AppSettings.EnergyUnitFamily = previousFamily; }
    }

    [AvaloniaFact]
    public void CounterIonGraphAndReportCombineBoundsCorrectlyAtNegativeActivityCoordinates()
    {
        var result = CreateResult(saltSeries: true);
        result.ElectrostaticsAnalysis.RestoreResult(
            new IonicStrengthDependenceFit(new(1e-6), new(2), new(0)),
            new LinearFitWithError(new FloatWithError(0.5, 0.1, 0.4, 0.6),
                new FloatWithError(-10, 0.2, -10.4, -9.6), 0),
            100, 100, DateTime.UtcNow, ErrorEstimationMethod.BootstrapResiduals);
        var graph = new ResultDependenceGraphControl
        {
            Result = result, Mode = ResultAnalysisViewMode.Salt,
            SaltMode = ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease,
        };
        var fit = GraphFit(graph);
        AssertCoordinate(Points(fit, "Line")[150], -4, -12);
        AssertCoordinate(Points(fit, "Lower")[150], -4, -12.8);
        AssertCoordinate(Points(fit, "Upper")[150], -4, -11.2);
        var line = ReportLine(result, AnalysisReportAdvancedSectionKind.CounterIonRelease, "Counter-ion release");
        var midpoint = line.Points[40];
        Near(midpoint.X, -4);
        Near(midpoint.Y, -12);
        Near(midpoint.Lower!.Value, -12.8);
        Near(midpoint.Upper!.Value, -11.2);
        AssertFinite(fit, line);
    }

    static object GraphFit(ResultDependenceGraphControl graph)
    {
        var series = (IEnumerable)typeof(ResultDependenceGraphControl)
            .GetField("cachedSeries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph)!;
        var item = Assert.Single(series.Cast<object>());
        return item.GetType().GetProperty("Fit")!.GetValue(item)!;
    }

    static List<(double X, double Y)> Points(object fit, string property) =>
        ((IEnumerable)fit.GetType().GetProperty(property)!.GetValue(fit)!).Cast<object>()
            .Select(point => ((double)point.GetType().GetProperty("X")!.GetValue(point)!,
                (double)point.GetType().GetProperty("Y")!.GetValue(point)!)).ToList();

    static AnalysisReportPlotSeries ReportLine(AnalysisResult result, AnalysisReportAdvancedSectionKind kind, string title)
    {
        var options = new AnalysisReportOptions { EnergyUnitOverride = EnergyUnit.KiloJoule };
        options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(kind));
        var plot = AnalysisReportBuilder.Build(result, options).Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportPlotBlock>()).Single(plot => plot.Title == title);
        return Assert.Single(plot.Series, series => series.Kind == AnalysisReportPlotSeriesKind.Line);
    }

    static void AssertFinite(object fit, AnalysisReportPlotSeries line)
    {
        foreach (var property in new[] { "Line", "Lower", "Upper" })
            Assert.All(Points(fit, property), point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y)));
        Assert.All(line.Points, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y)
            && (!point.Lower.HasValue || double.IsFinite(point.Lower.Value))
            && (!point.Upper.HasValue || double.IsFinite(point.Upper.Value))));
    }

    static void AssertCoordinate((double X, double Y) actual, double x, double y) { Near(actual.X, x); Near(actual.Y, y); }
    static void Near(double actual, double expected) => Assert.InRange(Math.Abs(actual - expected), 0, 1e-10);

    static AnalysisResult CreateResult(bool saltSeries)
    {
        var models = new List<Model>();
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        var buffers = new[] { Buffer.MES, Buffer.Hepes };
        for (var index = 0; index < (saltSeries ? 3 : 2); index++)
        {
            var data = new ExperimentData("envelope-" + index + ".itc")
            {
                MeasuredTemperature = 25, TargetTemperature = 25, CellVolume = 1.4e-3,
                CellConcentration = new(30e-6), SyringeConcentration = new(400e-6),
            };
            var attribute = ExperimentAttribute.FromKey(saltSeries ? AttributeKey.Salt : AttributeKey.Buffer);
            attribute.IntValue = saltSeries ? (int)Salt.NaCl : (int)buffers[index];
            attribute.DoubleValue = 7;
            attribute.ParameterValue = new(saltSeries ? Math.Exp(index - 3) : 0.05);
            data.Attributes.Add(attribute);
            var model = new OneSetOfSites(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, saltSeries ? -25000 : (index == 0 ? 5440 : 7120));
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            model.Solution = SolutionInterface.FromModel(model, convergence);
            models.Add(model);
        }
        var global = new GlobalModel(models) { ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        global.Solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(model => model.Solution).ToList(), convergence);
        return new AnalysisResult(global.Solution);
    }
}
