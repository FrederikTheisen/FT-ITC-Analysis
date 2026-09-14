using System.Globalization;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class MolarRmsdConvergenceTests
    {
        [Fact]
        public void FitMetricTooltipsUseCultureAwareObjectiveAndAvailabilityText()
        {
            var available = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Loss = 1,
                Objective = 1234.567,
            });
            var unavailable = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Loss = 1,
            });

            Assert.Equal(
                "Unweighted root mean square deviation (RMSD) in µJ. Optimisation objective = 1234.57.",
                FitMetricTooltipPresentation.Rmsd(available, CultureInfo.InvariantCulture));
            Assert.Contains(
                "Optimisation objective unavailable.",
                FitMetricTooltipPresentation.Rmsd(unavailable, CultureInfo.InvariantCulture));
            Assert.Equal(
                "Calculated from residuals divided by injection mass; display-only diagnostic, not used for optimisation.",
                FitMetricTooltipPresentation.MolarRmsd);
        }

        [Fact]
        public void CopyAndSnapshotPreserveMolarRmsd()
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Algorithm = SolverAlgorithm.LevenbergMarquardt,
                Termination = SolverTermination.Converged,
                Loss = 1.25,
                Objective = 2.5e-12,
                MolarRmsdJoulesPerMole = 4321.5,
            });

            var copy = convergence.Copy();
            var restored = SolverConvergence.FromSnapshot(convergence.ToSnapshot());

            Assert.Equal(4321.5, copy.MolarRMSD.Value.Value, 12);
            Assert.Equal(4321.5, restored.MolarRMSD.Value.Value, 12);
            Assert.Equal(2.5e-12, copy.Objective.Value, 12);
            Assert.Equal(2.5e-12, restored.Objective.Value, 12);
        }

        [Fact]
        public void MissingSnapshotValueRemainsNull()
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());

            Assert.Null(convergence.MolarRMSD);
            Assert.Null(convergence.Copy().MolarRMSD);
            Assert.Null(convergence.ToSnapshot().MolarRmsdJoulesPerMole);
            Assert.Null(convergence.Objective);
        }

        [Fact]
        public void SetterStoresEnergyWithoutChangingRawRmsd()
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Loss = 2.5,
            });

            convergence.SetMolarRMSD(new Energy(2500));
            convergence.SetObjective(7.5e-12);

            Assert.Equal(2.5, convergence.Loss, 12);
            Assert.Equal(2.5, convergence.UnweightedRmsd, 12);
            Assert.Equal(7.5e-12, convergence.Objective.Value, 12);
            Assert.Equal(2500, convergence.MolarRMSD.Value.Value, 12);
        }

        [Fact]
        public void RmsdRefreshDoesNotReplaceObjective()
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Loss = 1.25,
                Objective = 7.5e-12,
            });

            convergence.SetUnweightedRmsd(3.5);

            Assert.Equal(3.5, convergence.UnweightedRmsd, 12);
            Assert.Equal(7.5e-12, convergence.Objective.Value, 12);
        }
    }
}
