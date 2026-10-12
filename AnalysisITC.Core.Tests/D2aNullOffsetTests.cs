using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection("OffsetSolverTests")]
public sealed class D2aNullOffsetTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(SolverAlgorithm.LevenbergMarquardt, true)]
    [InlineData(SolverAlgorithm.LevenbergMarquardt, false)]
    [InlineData(SolverAlgorithm.NelderMead, true)]
    [InlineData(SolverAlgorithm.NelderMead, false)]
    public async Task SavedExperimentsRecoverIndependentLeastSquaresOffsets(SolverAlgorithm algorithm, bool weighted)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "nonbinding_d2a.ftxtc"));
        var experiments = (await FTXTCReader.ReadStream(stream)).OfType<ExperimentData>().ToArray();
        Assert.Equal(3, experiments.Length);
        foreach (var experiment in experiments)
        {
            var points = experiment.Injections.Where(point => point.Include).ToArray();
            // Direct minimizer of sum((q - offset*m)^2 / variance), independent of the optimizer.
            double Variance(InjectionData point) => weighted && point.PeakAreaError > 0
                ? point.PeakAreaError * point.PeakAreaError : 1;
            var expected = points.Sum(point => point.InjectionMass * point.PeakArea.Value / Variance(point))
                / points.Sum(point => point.InjectionMass * point.InjectionMass / Variance(point));
            var solution = experiment.Model.Solution;
            NullModelComparisonCalculator.Calculate(solution, weighted, algorithm, 5000, 1);
            var comparison = solution.NullComparison;
            var member = Assert.Single(comparison.Members);
            output.WriteLine($"{experiment.FileName}: expected={expected:G17}, actual={member.Offset:G17}, termination={member.Convergence.Termination}");
            Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
            // NM stops on the objective tolerance: 0.05 J/mol is well below data precision.
            Assert.InRange(Math.Abs(expected - member.Offset), 0,
                algorithm == SolverAlgorithm.NelderMead ? 0.05 : 0.001);
        }
    }
}
