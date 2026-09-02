using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

/// <summary>
/// Produces inspectable profile-likelihood graphs when PROFILE_LIKELIHOOD_EXPORT_DIR is set.
/// Without the environment variable, the same export path is exercised in a temporary directory.
/// </summary>
public sealed class ProfileLikelihoodVisualExportTests
{
    readonly ITestOutputHelper output;

    public ProfileLikelihoodVisualExportTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    [Trait("Category", "VisualExport")]
    public void DiagnosticProfilesExportAsSvgAndCsv()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("PROFILE_LIKELIHOOD_EXPORT_DIR");
        var retainArtifacts = !string.IsNullOrWhiteSpace(configuredDirectory);
        var exportDirectory = retainArtifacts
            ? Path.GetFullPath(configuredDirectory)
            : Path.Combine(Path.GetTempPath(), "ft-itc-profile-likelihood-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exportDirectory);

        try
        {
            var scenarios = new[]
            {
                new Scenario("symmetric-quadratic", "Symmetric quadratic profile",
                    CreateProbe(new[] { -1000d, 1000d, -1000d, 1000d }, value => value)),
                new Scenario("asymmetric-quadratic", "Asymmetric profile",
                    CreateProbe(new[] { -1000d, 1000d, -1000d, 1000d },
                        value => value < 0 ? .55 * value : value)),
                new Scenario("finite-bound-censoring", "Finite bounds before threshold",
                    CreateBoundedProbe()),
                new Scenario("nonmonotonic-reentry", "Non-monotonic profile with re-entry",
                    CreateNonmonotonicProbe()),
            };

            foreach (var scenario in scenarios)
            {
                var trace = new List<ProfileLikelihoodTracePoint>();
                var run = ProfileLikelihoodEstimator.RunWithTraceForTesting(
                    scenario.Model, SolverAlgorithm.NelderMead, false, 10,
                    point =>
                    {
                        lock (trace) trace.Add(point);
                    });

                var coordinate = Assert.Single(run.Coordinates);
                Assert.NotEmpty(trace);
                Assert.Contains(trace, point => point.Phase == ProfileLikelihoodTracePhase.BestFit);
                Assert.Contains(trace, point => point.Phase == ProfileLikelihoodTracePhase.Expansion);

                var csvPath = Path.Combine(exportDirectory, scenario.FileStem + ".csv");
                var svgPath = Path.Combine(exportDirectory, scenario.FileStem + ".svg");
                WriteCsv(csvPath, scenario, run, coordinate, trace);
                WriteSvg(svgPath, scenario, run, coordinate, trace);

                Assert.StartsWith("scenario,parameter", File.ReadLines(csvPath).First());
                var svg = File.ReadAllText(svgPath);
                Assert.Contains("<svg", svg);
                Assert.Contains("95% target", svg);
            }

            output.WriteLine(retainArtifacts
                ? $"Profile-likelihood diagnostic graphs retained in: {exportDirectory}"
                : "Profile-likelihood diagnostic graph export validated in a temporary directory. "
                    + "Set PROFILE_LIKELIHOOD_EXPORT_DIR to retain SVG and CSV files.");
        }
        finally
        {
            if (!retainArtifacts && Directory.Exists(exportDirectory))
                Directory.Delete(exportDirectory, true);
        }
    }

    [Fact]
    [Trait("Category", "VisualExport")]
    [Trait("Data", "Real")]
    public async Task SeArgTPu1210TwoSiteDatasetProfilesExportWhenRequested()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("PROFILE_LIKELIHOOD_EXPORT_DIR");
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            output.WriteLine("Real-data profile export not requested. Set PROFILE_LIKELIHOOD_EXPORT_DIR to run it.");
            return;
        }

        var exportDirectory = Path.GetFullPath(configuredDirectory);
        Directory.CreateDirectory(exportDirectory);
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "two-sites.ftxtc");
        using var fixture = File.OpenRead(fixturePath);
        var containers = await FTXTCReader.ReadStream(fixture);
        var result = Assert.Single(containers.OfType<AnalysisResult>(), item => item.Name == "TwoSetsOfSites");
        var member = Assert.IsAssignableFrom<SolutionInterface>(result.Solution.Solutions.First());
        var model = member.Model;
        Assert.Equal(AnalysisModel.TwoSetsOfSites, model.ModelType);
        Assert.Equal(25, model.Data.Injections.Count);

        var trace = new List<ProfileLikelihoodTracePoint>();
        var algorithm = member.Convergence?.Algorithm ?? SolverAlgorithm.LevenbergMarquardt;
        var run = ProfileLikelihoodEstimator.RunWithTraceForTesting(
            model, algorithm, member.UseWeightedFitting, 300,
            point =>
            {
                lock (trace) trace.Add(point);
            });

        Assert.Equal(model.Parameters.GetFittedParameters().Length, run.Coordinates.Count);
        Assert.NotEmpty(run.Coordinates);
        foreach (var coordinate in run.Coordinates)
        {
            var coordinateTrace = trace.Where(point => point.Coordinate?.Equals(coordinate.Id) == true).ToList();
            Assert.NotEmpty(coordinateTrace);
            var parameterStem = coordinate.Id.Parameter.ToString().ToLowerInvariant();
            var scenario = new Scenario(
                "real-seargt-pu1210-dataset-1-" + parameterStem,
                $"SeArgT Pu1210 dataset 1 — {coordinate.Id.Parameter}", model);
            var csvPath = Path.Combine(exportDirectory, scenario.FileStem + ".csv");
            var svgPath = Path.Combine(exportDirectory, scenario.FileStem + ".svg");
            WriteCsv(csvPath, scenario, run, coordinate, coordinateTrace);
            WriteSvg(svgPath, scenario, run, coordinate, coordinateTrace);
        }

        var globalResult = Assert.Single(containers.OfType<AnalysisResult>(), item => item.Name == "Global.TwoSetsOfSites");
        var globalModel = globalResult.Model;
        Assert.Equal(2, globalModel.Models.Count);
        Assert.False(globalModel.ShouldFitIndividually);
        Assert.Equal(VariableConstraint.SameForAll,
            globalModel.Parameters.GetConstraintForParameter(ParameterType.Enthalpy1));
        Assert.Equal(VariableConstraint.SameForAll,
            globalModel.Parameters.GetConstraintForParameter(ParameterType.Enthalpy2));
        Assert.Equal(VariableConstraint.TemperatureDependent,
            globalModel.Parameters.GetConstraintForParameter(ParameterType.Affinity1));
        Assert.Equal(VariableConstraint.TemperatureDependent,
            globalModel.Parameters.GetConstraintForParameter(ParameterType.Affinity2));
        globalModel.Parameters.SetIndividualFromGlobal();

        var globalTrace = new List<ProfileLikelihoodTracePoint>();
        var globalAlgorithm = globalResult.Solution.Convergence?.Algorithm ?? SolverAlgorithm.LevenbergMarquardt;
        var globalRun = ProfileLikelihoodEstimator.RunWithTraceForTesting(
            globalModel, globalAlgorithm, globalResult.Solution.UseWeightedFitting, 300,
            point =>
            {
                lock (globalTrace) globalTrace.Add(point);
            });

        Assert.Contains(globalRun.Coordinates, coordinate => coordinate.Id.IsShared
            && coordinate.Id.Parameter == ParameterType.Enthalpy1);
        Assert.Contains(globalRun.Coordinates, coordinate => coordinate.Id.IsShared
            && coordinate.Id.Parameter == ParameterType.Enthalpy2);
        Assert.Contains(globalRun.Coordinates, coordinate => coordinate.Id.IsShared
            && coordinate.Id.Parameter == ParameterType.Gibbs1);
        Assert.Contains(globalRun.Coordinates, coordinate => coordinate.Id.IsShared
            && coordinate.Id.Parameter == ParameterType.Gibbs2);
        foreach (var coordinate in globalRun.Coordinates)
        {
            var coordinateTrace = globalTrace.Where(point => point.Coordinate?.Equals(coordinate.Id) == true).ToList();
            Assert.NotEmpty(coordinateTrace);
            var scopeStem = coordinate.Id.IsShared
                ? "shared"
                : "dataset-" + (globalModel.Models.FindIndex(item => item.Data.UniqueID == coordinate.Id.ExperimentIdentity) + 1);
            var parameterStem = coordinate.Id.Parameter.ToString().ToLowerInvariant();
            var scenario = new Scenario(
                $"real-seargt-pu1210-global-{scopeStem}-{parameterStem}",
                $"SeArgT Pu1210 global fit — {scopeStem} {coordinate.Id.Parameter}",
                globalModel.Models.First());
            var csvPath = Path.Combine(exportDirectory, scenario.FileStem + ".csv");
            var svgPath = Path.Combine(exportDirectory, scenario.FileStem + ".svg");
            WriteCsv(csvPath, scenario, globalRun, coordinate, coordinateTrace);
            WriteSvg(svgPath, scenario, globalRun, coordinate, coordinateTrace);
        }

        output.WriteLine($"Exported {run.Coordinates.Count} individual and {globalRun.Coordinates.Count} global real-data profile graphs to: {exportDirectory}");
    }

    static ProbeModel CreateBoundedProbe()
    {
        var model = CreateProbe(new[] { -1000d, 1000d, -1000d, 1000d }, value => value);
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { -100d, 100d });
        return model;
    }

    static ProbeModel CreateNonmonotonicProbe()
    {
        var model = CreateProbe(new[] { .1, -.1, .1, -.1 },
            value => Math.Sin(Math.PI * value / 200d));
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { 0d, 5000d });
        model.Parameters.Table[ParameterType.Offset].SetReducedStepSize();
        return model;
    }

    static ProbeModel CreateProbe(IReadOnlyList<double> observations, Func<double, double> prediction)
    {
        var data = new ExperimentData("profile-visual-export.itc");
        for (var i = 0; i < observations.Count; i++)
        {
            var injection = new InjectionData(data, i, 1e-6, 1e-9, include: true);
            injection.SetPeakArea(new FloatWithError(observations[i], 1));
            data.Injections.Add(injection);
        }

        var model = new ProbeModel(data, prediction);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        return model;
    }

    static void WriteCsv(string path, Scenario scenario, ProfileLikelihoodRunResult run,
        ProfileCoordinateResult coordinate, IEnumerable<ProfileLikelihoodTracePoint> trace)
    {
        var csv = new StringBuilder();
        csv.AppendLine("scenario,parameter,scope,experiment,side,phase,parameter_value,objective_increment,target_increment,centered_value,usable,side_outcome");
        foreach (var point in trace.OrderBy(point => point.ParameterValue).ThenBy(point => point.Phase))
        {
            var side = point.Direction < 0 ? "lower" : "upper";
            var outcome = point.Direction < 0 ? coordinate.Lower.Outcome : coordinate.Upper.Outcome;
            csv.Append(Csv(scenario.Title)).Append(',')
                .Append(Csv(point.Coordinate?.Parameter.ToString() ?? coordinate.Id.Parameter.ToString())).Append(',')
                .Append(Csv(point.Coordinate?.Scope.ToString() ?? coordinate.Id.Scope.ToString())).Append(',')
                .Append(Csv(point.Coordinate?.ExperimentIdentity ?? coordinate.Id.ExperimentIdentity ?? string.Empty)).Append(',')
                .Append(side).Append(',')
                .Append(point.Phase).Append(',')
                .Append(Number(point.ParameterValue)).Append(',')
                .Append(Number(point.ObjectiveIncrement)).Append(',')
                .Append(Number(run.TargetIncrement)).Append(',')
                .Append(Number(point.CenteredValue)).Append(',')
                .Append(point.IsUsable ? "true" : "false").Append(',')
                .Append(outcome).AppendLine();
        }
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
    }

    static void WriteSvg(string path, Scenario scenario, ProfileLikelihoodRunResult run,
        ProfileCoordinateResult coordinate, IReadOnlyList<ProfileLikelihoodTracePoint> trace)
    {
        const double width = 1000;
        const double height = 650;
        const double left = 90;
        const double right = 30;
        const double top = 70;
        const double bottom = 80;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;

        var usable = trace.Where(point => point.IsUsable
                && double.IsFinite(point.ParameterValue) && double.IsFinite(point.ObjectiveIncrement))
            .GroupBy(point => point.ParameterValue)
            .Select(group => group.OrderByDescending(point => point.Phase).First())
            .OrderBy(point => point.ParameterValue)
            .ToList();
        Assert.NotEmpty(usable);

        var finiteLocations = trace.Where(point => double.IsFinite(point.ParameterValue))
            .Select(point => point.ParameterValue).ToList();
        var xMin = finiteLocations.Min();
        var xMax = finiteLocations.Max();
        if (double.IsFinite(coordinate.Lower.Endpoint)) xMin = Math.Min(xMin, coordinate.Lower.Endpoint);
        if (double.IsFinite(coordinate.Upper.Endpoint)) xMax = Math.Max(xMax, coordinate.Upper.Endpoint);
        if (xMax <= xMin) { xMin -= 1; xMax += 1; }
        var xPadding = .05 * (xMax - xMin);
        xMin -= xPadding;
        xMax += xPadding;

        var yMin = Math.Min(0, usable.Min(point => point.ObjectiveIncrement));
        var yMax = Math.Max(run.TargetIncrement * 1.2, usable.Max(point => point.ObjectiveIncrement) * 1.05);
        if (yMax <= yMin) yMax = yMin + 1;

        double X(double value) => left + (value - xMin) / (xMax - xMin) * plotWidth;
        double Y(double value) => top + (yMax - value) / (yMax - yMin) * plotHeight;

        var svg = new StringBuilder();
        svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(Number(width))
            .Append("\" height=\"").Append(Number(height)).Append("\" viewBox=\"0 0 ")
            .Append(Number(width)).Append(' ').Append(Number(height)).AppendLine("\">");
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        svg.Append("<text x=\"").Append(Number(left)).Append("\" y=\"34\" font-family=\"sans-serif\" font-size=\"22\" font-weight=\"bold\">")
            .Append(WebUtility.HtmlEncode(scenario.Title)).AppendLine("</text>");
        svg.Append("<text x=\"").Append(Number(left)).Append("\" y=\"56\" font-family=\"sans-serif\" font-size=\"13\" fill=\"#555\">")
            .Append(WebUtility.HtmlEncode($"Outcome: {run.Outcome}; lower: {coordinate.Lower.Outcome}; upper: {coordinate.Upper.Outcome}"))
            .AppendLine("</text>");

        for (var i = 0; i <= 5; i++)
        {
            var value = yMin + i * (yMax - yMin) / 5;
            var y = Y(value);
            svg.Append("<line x1=\"").Append(Number(left)).Append("\" y1=\"").Append(Number(y))
                .Append("\" x2=\"").Append(Number(left + plotWidth)).Append("\" y2=\"").Append(Number(y))
                .AppendLine("\" stroke=\"#e5e7eb\" stroke-width=\"1\"/>");
            svg.Append("<text x=\"").Append(Number(left - 10)).Append("\" y=\"").Append(Number(y + 4))
                .Append("\" text-anchor=\"end\" font-family=\"sans-serif\" font-size=\"12\" fill=\"#444\">")
                .Append(ShortNumber(value)).AppendLine("</text>");
        }
        for (var i = 0; i <= 5; i++)
        {
            var value = xMin + i * (xMax - xMin) / 5;
            var x = X(value);
            svg.Append("<text x=\"").Append(Number(x)).Append("\" y=\"").Append(Number(top + plotHeight + 24))
                .Append("\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"12\" fill=\"#444\">")
                .Append(ShortNumber(value)).AppendLine("</text>");
        }

        svg.Append("<line x1=\"").Append(Number(left)).Append("\" y1=\"").Append(Number(Y(run.TargetIncrement)))
            .Append("\" x2=\"").Append(Number(left + plotWidth)).Append("\" y2=\"").Append(Number(Y(run.TargetIncrement)))
            .AppendLine("\" stroke=\"#dc2626\" stroke-width=\"2\" stroke-dasharray=\"8 6\"/>");
        svg.Append("<text x=\"").Append(Number(left + plotWidth - 5)).Append("\" y=\"").Append(Number(Y(run.TargetIncrement) - 8))
            .AppendLine("\" text-anchor=\"end\" font-family=\"sans-serif\" font-size=\"13\" fill=\"#dc2626\">95% target</text>");

        AddVertical(svg, X(coordinate.BestValue), top, plotHeight, "#111827", "5 4");
        if (coordinate.Lower.IsEndpointFound) AddVertical(svg, X(coordinate.Lower.Endpoint), top, plotHeight, "#059669", "3 4");
        if (coordinate.Upper.IsEndpointFound) AddVertical(svg, X(coordinate.Upper.Endpoint), top, plotHeight, "#059669", "3 4");

        svg.Append("<polyline fill=\"none\" stroke=\"#2563eb\" stroke-width=\"2.5\" points=\"");
        foreach (var point in usable)
            svg.Append(Number(X(point.ParameterValue))).Append(',').Append(Number(Y(point.ObjectiveIncrement))).Append(' ');
        svg.AppendLine("\"/>");

        foreach (var point in usable)
        {
            var fill = point.Phase == ProfileLikelihoodTracePhase.Refinement ? "#f59e0b"
                : point.Phase == ProfileLikelihoodTracePhase.BestFit ? "#111827" : "#2563eb";
            svg.Append("<circle cx=\"").Append(Number(X(point.ParameterValue))).Append("\" cy=\"")
                .Append(Number(Y(point.ObjectiveIncrement))).Append("\" r=\"4\" fill=\"").Append(fill)
                .AppendLine("\" stroke=\"white\" stroke-width=\"1\"/>");
        }

        foreach (var point in trace.Where(point => !point.IsUsable && double.IsFinite(point.ParameterValue)))
        {
            var x = X(point.ParameterValue);
            var y = top + plotHeight - 8;
            svg.Append("<path d=\"M ").Append(Number(x - 5)).Append(' ').Append(Number(y - 5)).Append(" L ")
                .Append(Number(x + 5)).Append(' ').Append(Number(y + 5)).Append(" M ")
                .Append(Number(x - 5)).Append(' ').Append(Number(y + 5)).Append(" L ")
                .Append(Number(x + 5)).Append(' ').Append(Number(y - 5))
                .AppendLine("\" stroke=\"#dc2626\" stroke-width=\"2\"/>");
        }

        svg.Append("<rect x=\"").Append(Number(left)).Append("\" y=\"").Append(Number(top)).Append("\" width=\"")
            .Append(Number(plotWidth)).Append("\" height=\"").Append(Number(plotHeight))
            .AppendLine("\" fill=\"none\" stroke=\"#111827\" stroke-width=\"1.5\"/>");
        svg.Append("<text x=\"").Append(Number(left + plotWidth / 2)).Append("\" y=\"").Append(Number(height - 22))
            .AppendLine("\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"15\">Parameter value</text>");
        svg.Append("<text x=\"24\" y=\"").Append(Number(top + plotHeight / 2))
            .AppendLine("\" transform=\"rotate(-90 24 320)\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"15\">Objective increment</text>");
        svg.AppendLine("</svg>");
        File.WriteAllText(path, svg.ToString(), new UTF8Encoding(false));
    }

    static void AddVertical(StringBuilder svg, double x, double top, double plotHeight, string color, string dash)
    {
        svg.Append("<line x1=\"").Append(Number(x)).Append("\" y1=\"").Append(Number(top))
            .Append("\" x2=\"").Append(Number(x)).Append("\" y2=\"").Append(Number(top + plotHeight))
            .Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"1.5\" stroke-dasharray=\"")
            .Append(dash).AppendLine("\"/>");
    }

    static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    static string ShortNumber(double value) => value.ToString("G4", CultureInfo.InvariantCulture);

    sealed class Scenario
    {
        public string FileStem { get; }
        public string Title { get; }
        public Model Model { get; }

        public Scenario(string fileStem, string title, Model model)
        {
            FileStem = fileStem;
            Title = title;
            Model = model;
        }
    }

    sealed class ProbeModel : Model
    {
        readonly Func<double, double> prediction;

        public ProbeModel(ExperimentData data, Func<double, double> prediction) : base(data)
        {
            this.prediction = prediction;
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
            => prediction(Parameters.Table[ParameterType.Offset].Value);

        internal override Model GenerateSyntheticModel(Random random, ModelCloneOptions options)
        {
            var clone = new ProbeModel(Data.GetSynthClone(options, random), prediction);
            SetSynthModelParameters(clone, random, options);
            return clone;
        }
    }
}
