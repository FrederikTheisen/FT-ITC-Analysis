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
        /// The MicroCal curve is published for an initially ligand-free cell.  This
        /// transition extends that curve to an arbitrary current state while retaining
        /// the cumulative injected-volume history.  Consequently, repeatedly applying
        /// this operation without back-mixing telescopes to <see cref="Calculate"/>.
        /// </summary>
        public static InjectionConcentrationState AdvanceState(
            DilutionMethod method,
            double cellVolume,
            double syringeConcentration,
            InjectionConcentrationState currentState,
            double cumulativeInjectedVolumeBefore,
            double injectionVolume)
        {
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

                default:
                case DilutionMethod.MicroCal:
                {
                    var halfRelativeVolume = relativeVolume / 2.0;
                    var retention = (1.0 - halfRelativeVolume) / (1.0 + halfRelativeVolume);
                    return new ReferenceCurve(retention, relativeVolume * (1.0 - halfRelativeVolume));
                }
            }
        }

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
