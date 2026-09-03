using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class MolarRmsdConvergenceTests
    {
        [Fact]
        public void CopyAndSnapshotPreserveMolarRmsd()
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Algorithm = SolverAlgorithm.LevenbergMarquardt,
                Termination = SolverTermination.Converged,
                Loss = 1.25,
                MolarRmsdJoulesPerMole = 4321.5,
            });

            var copy = convergence.Copy();
            var restored = SolverConvergence.FromSnapshot(convergence.ToSnapshot());

            Assert.Equal(4321.5, copy.MolarRMSD.Value.Value, 12);
            Assert.Equal(4321.5, restored.MolarRMSD.Value.Value, 12);
        }

        [Fact]
        public void MissingSnapshotValueRemainsNull()
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());

            Assert.Null(convergence.MolarRMSD);
            Assert.Null(convergence.Copy().MolarRMSD);
            Assert.Null(convergence.ToSnapshot().MolarRmsdJoulesPerMole);
        }

        [Fact]
        public void SetterStoresEnergyWithoutChangingRawRmsd()
        {
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Loss = 2.5,
            });

            convergence.SetMolarRMSD(new Energy(2500));

            Assert.Equal(2.5, convergence.Loss, 12);
            Assert.Equal(2500, convergence.MolarRMSD.Value.Value, 12);
        }
    }
}
