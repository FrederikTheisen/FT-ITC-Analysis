using System;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;

namespace AnalysisITC.Core.Processing
{
    internal readonly struct InjectionConcentrationState
    {
        public InjectionConcentrationState(double cellConcentration, double titrantConcentration)
        {
            CellConcentration = cellConcentration;
            TitrantConcentration = titrantConcentration;
        }

        public double CellConcentration { get; }
        public double TitrantConcentration { get; }
    }

    internal static class InjectionDisplacementCalculator
    {
        public static InjectionConcentrationState Calculate(
            DilutionMethod method,
            double cellVolume,
            double syringeConcentration,
            double initialCellConcentration,
            double cumulativeInjectedVolume)
        {
            var relativeVolume = RelativeVolume(cellVolume, cumulativeInjectedVolume);

            var curve = EvaluateReferenceCurve(method, relativeVolume);
            return new InjectionConcentrationState(
                initialCellConcentration * curve.Retention,
                syringeConcentration * curve.Titrant);
        }

        /// <summary>
        /// Advances an already populated active-cell state by one injection.
        ///
        /// The MicroCal curve is published for an initially ligand-free cell. This
        /// application extension advances an arbitrary current state while retaining
        /// cumulative injected-volume history. Repeated advancement without back-mixing
        /// telescopes to <see cref="Calculate"/>.
        /// </summary>
        public static InjectionConcentrationState AdvanceState(
            DilutionMethod method,
            double cellVolume,
            double syringeConcentration,
            InjectionConcentrationState currentState,
            double cumulativeInjectedVolumeBefore,
            double injectionVolume)
        {
            if (method == DilutionMethod.DiscreteDisplacement)
                return DiscreteDisplacementState(currentState, syringeConcentration, DiscreteDisplacementRetention(cellVolume, injectionVolume));

            var previousRelativeVolume = RelativeVolume(cellVolume, cumulativeInjectedVolumeBefore);
            var newCumulativeInjectedVolume = cumulativeInjectedVolumeBefore + injectionVolume;
            var relativeVolume = RelativeVolume(cellVolume, newCumulativeInjectedVolume);

            if (previousRelativeVolume < 0.0)
                throw new ArgumentOutOfRangeException(nameof(cumulativeInjectedVolumeBefore), "Cumulative injected volume must be non-negative.");
            if (injectionVolume < 0.0)
                throw new ArgumentOutOfRangeException(nameof(injectionVolume), "Injection volume must be non-negative.");

            EnsureReferenceCurveDomain(method, previousRelativeVolume);
            EnsureReferenceCurveDomain(method, relativeVolume);

            var previousCurve = EvaluateReferenceCurve(method, previousRelativeVolume);
            var newCurve = EvaluateReferenceCurve(method, relativeVolume);
            var ratio = newCurve.Retention / previousCurve.Retention;

            return new InjectionConcentrationState(
                currentState.CellConcentration * ratio,
                currentState.TitrantConcentration * ratio
                    + syringeConcentration * (newCurve.Titrant - ratio * previousCurve.Titrant));
        }

        /// <summary>Fraction of the pre-injection mixture retained by one discrete shot.</summary>
        public static double DiscreteDisplacementRetention(double cellVolume, double injectionVolume)
        {
            var u = RelativeVolume(cellVolume, injectionVolume);
            if (injectionVolume < 0 || injectionVolume >= cellVolume)
                throw new ArgumentOutOfRangeException(nameof(injectionVolume),
                    "Discrete displacement requires injection volumes to be non-negative and smaller than the cell volume.");
            return 1.0 - u;
        }

        /// <summary>
        /// Reconstruct a fixed-syringe segment from its initial state and the product
        /// of its shot retentions. A cumulative volume alone cannot describe this law.
        /// </summary>
        public static InjectionConcentrationState DiscreteDisplacementState(
            InjectionConcentrationState initialState, double syringeConcentration, double retention)
        {
            return new InjectionConcentrationState(
                initialState.CellConcentration * retention,
                syringeConcentration * (1.0 - retention) + initialState.TitrantConcentration * retention);
        }

        public static void ApplyToInjection(
            ExperimentData experiment,
            InjectionData injection,
            InjectionConcentrationState state)
        {
            injection.ActualCellConcentration = state.CellConcentration;
            injection.ActualTitrantConcentration = state.TitrantConcentration;
            injection.Ratio = experiment.AxisType switch
            {
                AnalysisXAxisType.ID => injection.ID + 1,
                AnalysisXAxisType.TitrantConcentration => state.TitrantConcentration,
                _ => state.TitrantConcentration / state.CellConcentration,
            };
        }

        readonly struct ReferenceCurve
        {
            public ReferenceCurve(double retention, double titrant)
            {
                Retention = retention;
                Titrant = titrant;
            }

            public double Retention { get; }
            public double Titrant { get; }
        }

        static double RelativeVolume(double cellVolume, double injectedVolume)
        {
            if (cellVolume <= 0.0 || double.IsNaN(cellVolume) || double.IsInfinity(cellVolume))
                throw new ArgumentOutOfRangeException(nameof(cellVolume), "Cell volume must be finite and greater than zero.");
            if (double.IsNaN(injectedVolume) || double.IsInfinity(injectedVolume))
                throw new ArgumentOutOfRangeException(nameof(injectedVolume), "Injected volume must be finite.");

            return injectedVolume / cellVolume;
        }

        static ReferenceCurve EvaluateReferenceCurve(DilutionMethod method, double relativeVolume)
        {
            switch (method)
            {
                case DilutionMethod.Exponential:
                {
                    var retention = Math.Exp(-relativeVolume);
                    return new ReferenceCurve(retention, 1.0 - retention);
                }

                case DilutionMethod.MicroCal:
                {
                    // Malvern Instruments, MicroCal PEAQ-ITC Analysis Software User Manual,
                    // MAN0576-01-EN-00 (2015), printed p. 101 (PDF p. 105), eqs. (2) and (4):
                    // https://www.malvernpanalytical.com/en/learn/knowledge-center/user-manuals/man0576en
                    // Also cited in the application manual: MicroCal ITC Analysis Software Using
                    // Origin User Manual, MAN0577-02-EN-00 (20 May 2015), section 12.3.1, eqs. (2), (4):
                    // https://www.malvernpanalytical.com/en/learn/knowledge-center/user-manuals/man0577en
                    // Here u = relativeVolume is cumulative injected volume / active cell volume.
                    // The manual uses an approximate ligand expression alongside its
                    // rational retained-cell curve. The displaced-volume assumptions remain approximate.
                    var halfRelativeVolume = relativeVolume / 2.0;
                    var retention = (1.0 - halfRelativeVolume) / (1.0 + halfRelativeVolume);
                    return new ReferenceCurve(retention, MicroCalApproximateTitrant(relativeVolume));
                }
                case DilutionMethod.DiscreteDisplacement:
                    throw new ArgumentException("Discrete displacement concentrations require the individual injection volumes, not just their sum.", nameof(method));
                default:
                    throw new ArgumentOutOfRangeException(nameof(method));
            }
        }

        // The MicroCal manual's approximate ligand expression drops the (u/2)^2
        // term from equation (3). Keep that equation's untruncated rational ligand
        // fraction available for diagnostics and independently tested comparisons.
        internal static double MicroCalApproximateTitrant(double relativeVolume) =>
            relativeVolume * (1.0 - relativeVolume / 2.0);

        internal static double MicroCalRationalTitrant(double relativeVolume) =>
            relativeVolume / (1.0 + relativeVolume / 2.0);

        static void EnsureReferenceCurveDomain(DilutionMethod method, double relativeVolume)
        {
            if (method != DilutionMethod.MicroCal)
                return;

            // MicroCal's rational retention curve reaches zero at u = 2.  An
            // arbitrary-state transition would require division by that zero (and
            // becomes nonphysical beyond it), so fail explicitly at the boundary.
            if (relativeVolume >= 2.0)
                throw new InvalidOperationException(
                    "The MicroCal displacement correction is undefined at or beyond two cell volumes of cumulative injection.");
        }
    }
}
