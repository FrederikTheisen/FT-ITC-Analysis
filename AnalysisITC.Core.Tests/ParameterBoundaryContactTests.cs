using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection("AutoSaveManager")]
    public sealed class ParameterBoundaryContactTests
    {
        [Fact]
        public void DetectorReportsLowerAndUpperContactsUsingScaleAwareTolerance()
        {
            var data = new ExperimentData("boundary-test.itc");
            data.SetID("experiment-id");
            var model = new Model(data);

            const double lower = -30000;
            const double upper = 30000;
            var tolerance = 1e-6 * (upper - lower);

            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, lower + tolerance * 0.5);
            var lowerContact = Assert.Single(ParameterBoundaryDetector.Detect(model));
            Assert.Equal(ParameterBoundarySide.Lower, lowerContact.Side);
            Assert.Equal(lower + tolerance * 0.5, lowerContact.FinalValue);
            Assert.Equal(lower, lowerContact.BoundValue);
            Assert.Equal("experiment-id", lowerContact.ExperimentIdentity);
            Assert.Equal("boundary-test", lowerContact.ExperimentName);

            model.Parameters.Table[ParameterType.Offset].Update(upper - tolerance * 0.5);
            var upperContact = Assert.Single(ParameterBoundaryDetector.Detect(model));
            Assert.Equal(ParameterBoundarySide.Upper, upperContact.Side);
            Assert.Equal(upper, upperContact.BoundValue);

            model.Parameters.Table[ParameterType.Offset].Update(lower + tolerance * 1.01);
            Assert.Empty(ParameterBoundaryDetector.Detect(model));
        }

        [Fact]
        public void DetectorIgnoresLockedParametersAndReportsSharedIdentity()
        {
            var data = new ExperimentData("shared-test.itc");
            var model = new Model(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, -30000, islocked: true);
            Assert.Empty(ParameterBoundaryDetector.Detect(model));

            var global = new GlobalModel();
            global.Parameters.AddorUpdateGlobalParameter(
                ParameterType.Offset,
                -30000,
                limits: new[] { -30000d, 30000d });
            var contact = Assert.Single(ParameterBoundaryDetector.Detect(global));
            Assert.Equal(ParameterBoundaryScope.Shared, contact.Scope);
            Assert.Null(contact.ExperimentIdentity);
            Assert.Equal(ParameterType.Offset.ToString(), contact.ParameterIdentity);
        }

        [Fact]
        public void CopyPreservesContactsButSnapshotDoesNotSerializeThem()
        {
            var original = SolverConvergence.ReportFailed(DateTime.UtcNow);
            original.SetParameterBoundaryContacts(new[]
            {
                new ParameterBoundaryContact(
                    ParameterType.Offset,
                    ParameterBoundaryScope.Local,
                    "exp-id",
                    "experiment",
                    ParameterBoundarySide.Upper,
                    30000,
                    30000),
            });

            var copy = original.Copy();
            var copyContact = Assert.Single(copy.ParameterBoundaryContacts);
            Assert.NotSame(original.ParameterBoundaryContacts[0], copyContact);
            Assert.Equal(ParameterBoundarySide.Upper, copyContact.Side);
            Assert.Equal("exp-id", copyContact.ExperimentIdentity);

            var restored = SolverConvergence.FromSnapshot(original.ToSnapshot());
            Assert.Empty(restored.ParameterBoundaryContacts);
        }

        [Fact]
        public void FormatterUsesExperimentNamesAndAggregatesAfterThree()
        {
            var contacts = Enumerable.Range(1, 4)
                .Select(index => new ParameterBoundaryContact(
                    ParameterType.Offset,
                    ParameterBoundaryScope.Local,
                    $"id-{index}",
                    $"experiment-{index}",
                    ParameterBoundarySide.Lower,
                    -30000,
                    -30000))
                .ToArray();

            var warning = ParameterBoundaryWarningFormatter.Format(contacts);
            Assert.Contains("Warning", warning);
            Assert.Contains("experiment-1", warning);
            Assert.Contains("experiment-3", warning);
            Assert.Contains("and 1 more", warning);
            Assert.DoesNotContain("experiment-4", warning);
        }

        [Fact]
        public void SolutionBoundaryFlagAssignsSharedAndMatchingLocalContactsOnly()
        {
            var first = CreateModel("first");
            var second = CreateModel("second");

            var shared = ConvergenceWith(new ParameterBoundaryContact(
                ParameterType.Offset, ParameterBoundaryScope.Shared, null, null,
                ParameterBoundarySide.Upper, 30000, 30000));
            Assert.True(SolutionInterface.FromModel(first, shared).ParameterBoundaryHit);
            Assert.True(SolutionInterface.FromModel(second, shared).ParameterBoundaryHit);

            var local = ConvergenceWith(new ParameterBoundaryContact(
                ParameterType.Offset, ParameterBoundaryScope.Local, "first", "first",
                ParameterBoundarySide.Lower, -30000, -30000));
            Assert.True(SolutionInterface.FromModel(first, local).ParameterBoundaryHit);
            Assert.False(SolutionInterface.FromModel(second, local).ParameterBoundaryHit);

            var unrelated = ConvergenceWith(new ParameterBoundaryContact(
                ParameterType.Offset, ParameterBoundaryScope.Local, "other", "other",
                ParameterBoundarySide.Lower, -30000, -30000));
            Assert.False(SolutionInterface.FromModel(first, unrelated).ParameterBoundaryHit);
            Assert.False(SolutionInterface.FromModel(second, unrelated).ParameterBoundaryHit);
        }

        [Fact]
        public void SilentSolverRecordsBoundaryFlagOnCompletedSolution()
        {
            var previous = AppSettings.ParameterLimitSetting;
            try
            {
                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
                var data = new ExperimentData("silent-boundary.itc");
                for (var id = 0; id < 4; id++)
                {
                    var injection = new InjectionData(data, id, 1, 1, true);
                    injection.SetPeakArea(new FloatWithError(60000, 1));
                    data.Injections.Add(injection);
                }
                var model = new BoundaryProbeModel(data);
                model.InitializeParameters(data);
                model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
                data.Model = model;
                var solver = new Solver
                {
                    Model = model,
                    SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                    ErrorEstimationMethod = ErrorEstimationMethod.None,
                    MaxOptimizerIterations = 200,
                    Silent = true,
                };

                var convergence = solver.Solve();

                Assert.True(convergence.IsUsableForErrorEstimation, convergence.Message);
                Assert.NotEmpty(convergence.ParameterBoundaryContacts);
                Assert.True(model.Solution.ParameterBoundaryHit);
            }
            finally
            {
                AppSettings.ParameterLimitSetting = previous;
            }
        }

        [Fact]
        public void BoundaryAndNearBoundaryBootstrapSolutionsRemainUnderEveryLimitSetting()
        {
            var previous = AppSettings.ParameterLimitSetting;
            try
            {
                foreach (var setting in Enum.GetValues<ParameterLimitSetting>())
                {
                    AppSettings.ParameterLimitSetting = setting;
                    var limits = new Parameter(ParameterType.Offset, 0).Limits;
                    var span = limits[1] - limits[0];
                    var primary = new TestSolution("primary", 0);
                    var boundary = new TestSolution("boundary", limits[0]);
                    var nearBoundary = new TestSolution("near-boundary", limits[0] + span * 0.005);
                    boundary.RestoreParameterBoundaryHit(true);

                    primary.SetBootstrapSolutions(new List<SolutionInterface> { boundary, nearBoundary });

                    Assert.Equal(2, primary.BootstrapSolutions.Count);
                    Assert.Equal(new[] { limits[0], limits[0] + span * 0.005 }, primary.LastDistribution);
                    Assert.True(primary.BootstrapParameterBoundaryHit);
                }
            }
            finally
            {
                AppSettings.ParameterLimitSetting = previous;
            }
        }

        [Fact]
        public void BootstrapValidationRejectsNullAndMismatchedShapesOnly()
        {
            var primary = new TestSolution("primary", 0);
            var matching = new TestSolution("matching", 30000);
            var mismatched = new TestSolution("mismatched", 1, ParameterType.Nvalue1);

            primary.SetBootstrapSolutions(new List<SolutionInterface> { null, matching, mismatched });

            Assert.Same(matching, Assert.Single(primary.BootstrapSolutions));
        }

        [Theory]
        [InlineData(SolverTermination.Failed)]
        [InlineData(SolverTermination.Cancelled)]
        [InlineData(SolverTermination.InvalidValues)]
        public void FailedCancelledAndInvalidFitsRemainIneligibleForErrorEstimation(
            SolverTermination termination)
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Termination = termination,
            });

            Assert.False(convergence.IsUsableForErrorEstimation);
        }

        [Theory]
        [InlineData(SolverTermination.IterationLimit)]
        [InlineData(SolverTermination.EvaluationLimit)]
        [InlineData(SolverTermination.TimeLimit)]
        public void LimitTerminatedFitsRemainIneligibleButCanStartErrorEstimation(
            SolverTermination termination)
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Termination = termination,
            });

            Assert.True(convergence.MaxIterationsReached);
            Assert.False(convergence.IsUsableForErrorEstimation);
            Assert.True(convergence.CanRunErrorEstimation);
        }

        [Fact]
        public void LimitTerminatedRefitsAreReportedByTheSharedWarningFormatter()
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
            convergence.ApplyErrorEstimationResult(
                ErrorEstimationMethod.BootstrapResiduals,
                failures: 2,
                succeeded: 3,
                TimeSpan.FromSeconds(1),
                limitTerminated: 2);

            var solution = SolutionInterface.FromModel(CreateModel("limit-warning"), convergence);
            var warnings = ParameterBoundaryWarningFormatter.MessagesFor(
                solution,
                ErrorEstimationMethod.BootstrapResiduals);

            Assert.Contains(
                ParameterBoundaryWarningFormatter.BootstrapLimitMessage,
                warnings);
            Assert.Contains("limit-terminated=2", convergence.ErrorEstimationSummary);
            Assert.Equal(2, convergence.ErrorEstimationLimitTerminations);
            Assert.True(convergence.HasErrorEstimationLimitWarnings);

            var restored = SolverConvergence.FromSnapshot(convergence.ToSnapshot());
            Assert.Equal(2, restored.ErrorEstimationLimitTerminations);
            Assert.Contains(
                ParameterBoundaryWarningFormatter.BootstrapLimitMessage,
                ParameterBoundaryWarningFormatter.MessagesFor(
                    SolutionInterface.FromModel(CreateModel("restored-limit-warning"), restored),
                    ErrorEstimationMethod.BootstrapResiduals));
        }

        [Fact]
        public void SharedWarningMessagesDistinguishBootstrapAndLeaveOneOut()
        {
            var solution = new TestSolution("warnings", 0);
            solution.RestoreParameterBoundaryHit(true);
            var replicate = new TestSolution("replicate", 0);
            replicate.RestoreParameterBoundaryHit(true);
            solution.SetBootstrapSolutions(new List<SolutionInterface> { replicate });

            Assert.Equal(new[]
            {
                "Best fit reached a parameter boundary.",
                "One or more bootstrap fits reached a parameter boundary.",
            }, ParameterBoundaryWarningFormatter.MessagesFor(
                solution, ErrorEstimationMethod.BootstrapResiduals));
            Assert.Equal(new[]
            {
                "Best fit reached a parameter boundary.",
                "One or more leave-one-out fits reached a parameter boundary.",
            }, ParameterBoundaryWarningFormatter.MessagesFor(
                solution, ErrorEstimationMethod.LeaveOneOut));
        }

        [Fact]
        public async Task WarningHealthRemainsValidAndValidityStatesTakePrecedence()
        {
            using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc"));
            var result = Assert.Single((await FTITCReader.ReadStream(source)).OfType<AnalysisResult>());
            result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(result.Solution));
            var member = result.Solution.Solutions[0];
            member.RestoreParameterBoundaryHit(true);

            Assert.Equal(AnalysisResultHealth.Warning, result.Health);
            Assert.True(result.IsValidForCurrentData);
            Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);

            member.Data.CellConcentration = new FloatWithError(member.Data.CellConcentration.Value * 1.1);
            Assert.Equal(AnalysisResultValidity.PartialInvalid, result.ValidityReport.Status);
            Assert.Equal(AnalysisResultHealth.PartialInvalid, result.Health);

            foreach (var solution in result.Solution.Solutions.Skip(1))
                solution.Data.CellConcentration = new FloatWithError(solution.Data.CellConcentration.Value * 1.1);
            Assert.Equal(AnalysisResultValidity.Invalid, result.ValidityReport.Status);
            Assert.Equal(AnalysisResultHealth.Invalid, result.Health);

            result.SetValiditySnapshot(null);
            Assert.Equal(AnalysisResultHealth.Unknown, result.Health);
        }

        static Model CreateModel(string id)
        {
            var data = new ExperimentData(id + ".itc");
            data.SetID(id);
            var model = new Model(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            return model;
        }

        static SolverConvergence ConvergenceWith(ParameterBoundaryContact contact)
        {
            var convergence = SolverConvergence.ReportFailed(DateTime.UtcNow);
            convergence.SetParameterBoundaryContacts(new[] { contact });
            return convergence;
        }

        sealed class TestSolution : SolutionInterface
        {
            public double[] LastDistribution { get; private set; } = Array.Empty<double>();

            public TestSolution(string id, double value, ParameterType key = ParameterType.Offset)
            {
                var data = new ExperimentData(id + ".itc");
                data.SetID(id);
                Model = new Model(data);
                Model.Parameters.AddOrUpdateParameter(key, value);
                Parameters.Add(key, new FloatWithError(value));
                BootstrapSolutions = new List<SolutionInterface>();
            }

            public override void ComputeErrorsFromBootstrapSolutions()
            {
                LastDistribution = BootstrapSolutions
                    .Select(solution => solution.Parameters.Values.Single().Value)
                    .ToArray();
            }
        }

        sealed class BoundaryProbeModel : Model
        {
            public BoundaryProbeModel(ExperimentData data) : base(data)
            {
            }

            public override double Evaluate(int injectionindex, bool withoffset = true) =>
                Parameters.Table[ParameterType.Offset].Value;
        }

    }
}
