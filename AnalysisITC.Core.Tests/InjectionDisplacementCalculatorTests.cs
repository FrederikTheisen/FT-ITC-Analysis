using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class InjectionDisplacementCalculatorTests
    {
        [Fact]
        public void MicroCalMatchesMalvernReferenceRatios()
        {
            const double cellVolume = 204.7e-6;
            const double syringeConcentration = 1e-3;
            const double cellConcentration = 125e-6;

            var first = InjectionDisplacementCalculator.Calculate(
                DilutionMethod.MicroCal,
                cellVolume,
                syringeConcentration,
                cellConcentration,
                0.7e-6);
            var second = InjectionDisplacementCalculator.Calculate(
                DilutionMethod.MicroCal,
                cellVolume,
                syringeConcentration,
                cellConcentration,
                3.7002e-6);

            Assert.Equal(0.027404, Math.Round(first.TitrantConcentration / first.CellConcentration, 6));
            Assert.Equal(0.145917, Math.Round(second.TitrantConcentration / second.CellConcentration, 6));
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void IncrementalStateAdvancementMatchesDirectCalculation(DilutionMethod method)
        {
            const double cellVolume = 204.7e-6;
            const double syringeConcentration = 1e-3;
            const double cellConcentration = 125e-6;
            var current = new InjectionConcentrationState(cellConcentration, 0.0);
            var cumulativeVolume = 0.0;

            foreach (var injectionVolume in new[] { 0.7e-6, 3.0e-6, 3.0e-6, 0.7e-6 })
            {
                current = InjectionDisplacementCalculator.AdvanceState(
                    method,
                    cellVolume,
                    syringeConcentration,
                    current,
                    cumulativeVolume,
                    injectionVolume);
                cumulativeVolume += injectionVolume;

                var expected = InjectionDisplacementCalculator.Calculate(
                    method,
                    cellVolume,
                    syringeConcentration,
                    cellConcentration,
                    cumulativeVolume);
                AssertClose(expected.CellConcentration, current.CellConcentration);
                AssertClose(expected.TitrantConcentration, current.TitrantConcentration);
            }
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void StateAdvancementUsesArbitraryCurrentConcentrations(DilutionMethod method)
        {
            const double cellVolume = 200e-6;
            const double syringeConcentration = 1e-3;
            const double cumulativeVolume = 30e-6;
            const double injectionVolume = 4e-6;
            var current = new InjectionConcentrationState(80e-6, 150e-6);

            var actual = InjectionDisplacementCalculator.AdvanceState(
                method,
                cellVolume,
                syringeConcentration,
                current,
                cumulativeVolume,
                injectionVolume);
            var previousCurve = ReferenceCurve(method, cumulativeVolume / cellVolume);
            var nextCurve = ReferenceCurve(method, (cumulativeVolume + injectionVolume) / cellVolume);
            var retentionRatio = nextCurve.retention / previousCurve.retention;

            AssertClose(current.CellConcentration * retentionRatio, actual.CellConcentration);
            AssertClose(
                current.TitrantConcentration * retentionRatio
                    + syringeConcentration * (nextCurve.titrant - retentionRatio * previousCurve.titrant),
                actual.TitrantConcentration);
        }

        [Fact]
        public void ExponentialStateAdvancementReducesToClosedFormTransition()
        {
            const double cellVolume = 200e-6;
            const double syringeConcentration = 1e-3;
            const double injectionVolume = 4e-6;
            var current = new InjectionConcentrationState(80e-6, 150e-6);

            var actual = InjectionDisplacementCalculator.AdvanceState(
                DilutionMethod.Exponential,
                cellVolume,
                syringeConcentration,
                current,
                30e-6,
                injectionVolume);
            var retention = Math.Exp(-injectionVolume / cellVolume);

            AssertClose(current.CellConcentration * retention, actual.CellConcentration);
            AssertClose(
                current.TitrantConcentration * retention + syringeConcentration * (1.0 - retention),
                actual.TitrantConcentration);
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void StateAdvancementProducesNonnegativeMassConservingDisplacement(DilutionMethod method)
        {
            const double cellVolume = 200e-6;
            const double syringeConcentration = 1e-3;
            const double injectionVolume = 4e-6;
            var current = new InjectionConcentrationState(80e-6, 150e-6);
            var next = InjectionDisplacementCalculator.AdvanceState(
                method,
                cellVolume,
                syringeConcentration,
                current,
                30e-6,
                injectionVolume);

            var displacedCellMass = current.CellConcentration * cellVolume
                - next.CellConcentration * cellVolume;
            var displacedTitrantMass = current.TitrantConcentration * cellVolume
                + syringeConcentration * injectionVolume
                - next.TitrantConcentration * cellVolume;

            Assert.True(displacedCellMass >= 0.0);
            Assert.True(displacedTitrantMass >= 0.0);
            AssertClose(
                current.CellConcentration * cellVolume,
                next.CellConcentration * cellVolume + displacedCellMass);
            AssertClose(
                current.TitrantConcentration * cellVolume + syringeConcentration * injectionVolume,
                next.TitrantConcentration * cellVolume + displacedTitrantMass);
        }

        [Fact]
        public void MicroCalStateAdvancementRejectsZeroRetentionBoundary()
        {
            Assert.Throws<InvalidOperationException>(() => InjectionDisplacementCalculator.AdvanceState(
                DilutionMethod.MicroCal,
                1.0,
                1.0,
                new InjectionConcentrationState(1.0, 0.0),
                1.9,
                0.1));
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void SharedCalculatorPreservesOrdinaryProcessingResults(DilutionMethod method)
        {
            var experiment = CreateExperiment();
            var cumulativeVolume = 0.0;

            RawDataReader.ProcessInjectionsUsingMethod(experiment, method);

            foreach (var injection in experiment.Injections)
            {
                cumulativeVolume += injection.Volume;
                var expected = ExpectedState(
                    method,
                    experiment.CellVolume,
                    experiment.SyringeConcentration.Value,
                    experiment.CellConcentration.Value,
                    cumulativeVolume);

                AssertClose(expected.cell, injection.ActualCellConcentration);
                AssertClose(expected.titrant, injection.ActualTitrantConcentration);
                AssertClose(expected.titrant / expected.cell, injection.Ratio);
            }
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void ThreeSegmentConcatMatchesOneUninterruptedOrdinaryExperiment(DilutionMethod method)
        {
            var ordinary = CreateExperiment();
            var sources = CreateSegmentSources();

            RawDataReader.ProcessInjectionsUsingMethod(ordinary, method);
            var tandem = TandemConcatenation.ConcatTandem(sources, method);

            Assert.Equal(3, tandem.Segments.Count);
            AssertInjectionStatesEqual(ordinary, tandem);
            AssertSegmentStartsMatchPreviousInjection(tandem);
        }

        [Fact]
        public void MicroCalConcatDoesNotUseFormerPerInjectionRecurrence()
        {
            var experiment = CreateExperiment();
            TandemConcatenation.ProcessInjectionsWithoutBackMixing(
                experiment,
                Segments(),
                DilutionMethod.MicroCal);

            var recurrenceCell = experiment.CellConcentration.Value;
            foreach (var injection in experiment.Injections)
                recurrenceCell *= experiment.CellVolume / (experiment.CellVolume + injection.Volume);

            var actualCell = experiment.Injections.Last().ActualCellConcentration;
            Assert.True(Math.Abs(actualCell - recurrenceCell) > 1e-12);

            var totalVolume = experiment.Injections.Sum(injection => injection.Volume);
            var expected = ExpectedState(
                DilutionMethod.MicroCal,
                experiment.CellVolume,
                experiment.SyringeConcentration.Value,
                experiment.CellConcentration.Value,
                totalVolume);
            AssertClose(expected.cell, actualCell);
        }

        [Fact]
        public void BackMixingUsesTheSelectedGlobalDilutionPreference()
        {
            var originalMethod = AppSettings.DilutionCalculationMethod;
            try
            {
                AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
                var microCalPreference = ProcessBackMixingUsingGlobalPreference(0.10, 0.20);
                AppSettings.DilutionCalculationMethod = DilutionMethod.Exponential;
                var exponentialPreference = ProcessBackMixingUsingGlobalPreference(0.10, 0.20);

                Assert.True(StateDistance(microCalPreference, exponentialPreference) > 1e-12);
                Assert.True(SegmentStateDistance(microCalPreference, exponentialPreference) > 1e-12);
            }
            finally
            {
                AppSettings.DilutionCalculationMethod = originalMethod;
            }
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void BackMixingZeroFractionBaselineMatchesSelectedConcat(DilutionMethod method)
        {
            var concat = CreateExperiment();
            TandemConcatenation.ProcessInjectionsWithoutBackMixing(
                concat,
                Segments(),
                method);

            var backMixingBaseline = ProcessBackMixing(0.0, 0.0, method);

            AssertInjectionStatesEqual(concat, backMixingBaseline);
            AssertSegmentStatesEqual(concat, backMixingBaseline);
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void BackMixingLeavesFirstSegmentEqualToConcat(DilutionMethod method)
        {
            var concat = CreateExperiment();
            TandemConcatenation.ProcessInjectionsWithoutBackMixing(concat, Segments(), method);
            var backMixing = ProcessBackMixing(0.10, 0.20, method);

            for (var index = 0; index < 3; index++)
            {
                AssertClose(
                    concat.Injections[index].ActualCellConcentration,
                    backMixing.Injections[index].ActualCellConcentration);
                AssertClose(
                    concat.Injections[index].ActualTitrantConcentration,
                    backMixing.Injections[index].ActualTitrantConcentration);
            }
        }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(0.0, 0.1)]
        [InlineData(0.1, 0.0)]
        [InlineData(0.1, 0.1)]
        [InlineData(0.001, 0.0)]
        [InlineData(0.0, 0.001)]
        [InlineData(0.001, 0.001)]
        public void ThreeSegmentBackMixingScenariosProduceFiniteStates(double first, double second)
        {
            foreach (var method in new[] { DilutionMethod.MicroCal, DilutionMethod.Exponential })
            {
                var experiment = ProcessBackMixing(first, second, method);
                Assert.All(experiment.Injections, injection =>
                {
                    Assert.True(double.IsFinite(injection.ActualCellConcentration));
                    Assert.True(double.IsFinite(injection.ActualTitrantConcentration));
                    Assert.True(double.IsFinite(injection.Ratio));
                });
            }
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void SmallBackMixingFractionsConvergeTowardStatefulZeroBaseline(DilutionMethod method)
        {
            var baseline = ProcessBackMixing(0.0, 0.0, method);

            AssertConverges(
                baseline,
                ProcessBackMixing(0.1, 0.0, method),
                ProcessBackMixing(0.001, 0.0, method),
                ProcessBackMixing(0.000001, 0.0, method));
            AssertConverges(
                baseline,
                ProcessBackMixing(0.0, 0.1, method),
                ProcessBackMixing(0.0, 0.001, method),
                ProcessBackMixing(0.0, 0.000001, method));
            AssertConverges(
                baseline,
                ProcessBackMixing(0.1, 0.1, method),
                ProcessBackMixing(0.001, 0.001, method),
                ProcessBackMixing(0.000001, 0.000001, method));
        }

        static ExperimentData ProcessBackMixingUsingGlobalPreference(double first, double second)
        {
            var experiment = CreateExperiment();
            TandemConcatenation.ProcessInjectionsWithBackMixing(
                experiment,
                Segments(),
                new TandemConcatenation.BackMixingSettings
                {
                    UseBackMixingMethod = true,
                    DidRemoveOverflow = true,
                    DeadVolume = 80e-6,
                },
                new[] { first, second });
            return experiment;
        }

        static ExperimentData ProcessBackMixing(
            double first,
            double second,
            DilutionMethod method = DilutionMethod.MicroCal)
        {
            var experiment = CreateExperiment();
            TandemConcatenation.ProcessInjectionsWithBackMixing(
                experiment,
                Segments(),
                new TandemConcatenation.BackMixingSettings
                {
                    UseBackMixingMethod = true,
                    DidRemoveOverflow = true,
                    DeadVolume = 80e-6,
                },
                new[] { first, second },
                method);
            return experiment;
        }

        static ExperimentData CreateExperiment()
        {
            var experiment = new ExperimentData("displacement-test.itc")
            {
                CellConcentration = new FloatWithError(125e-6),
                SyringeConcentration = new FloatWithError(1e-3),
                CellVolume = 204.7e-6,
            };

            foreach (var volume in new[]
                     {
                         0.7e-6, 3.0e-6, 3.0e-6,
                         0.7e-6, 3.0e-6, 3.0e-6,
                         0.7e-6, 3.0e-6, 3.0e-6,
                     })
                experiment.Injections.Add(new InjectionData(experiment, volume));

            return experiment;
        }

        static List<ExperimentData> CreateSegmentSources()
        {
            var volumes = new[]
            {
                0.7e-6, 3.0e-6, 3.0e-6,
                0.7e-6, 3.0e-6, 3.0e-6,
                0.7e-6, 3.0e-6, 3.0e-6,
            };

            return Enumerable.Range(0, 3).Select(segmentIndex =>
            {
                var experiment = new ExperimentData($"segment-{segmentIndex + 1}.itc")
                {
                    CellConcentration = new FloatWithError(125e-6),
                    SyringeConcentration = new FloatWithError(1e-3),
                    CellVolume = 204.7e-6,
                    DataPoints = new List<DataPoint>
                    {
                        new(0, 0, 25),
                        new(10, 0, 25),
                    },
                };

                foreach (var volume in volumes.Skip(segmentIndex * 3).Take(3))
                    experiment.Injections.Add(new InjectionData(experiment, volume));

                return experiment;
            }).ToList();
        }

        static List<TandemConcatenation.TandemInjectionSegment> Segments() => new()
        {
            new TandemConcatenation.TandemInjectionSegment(0, 3, "first"),
            new TandemConcatenation.TandemInjectionSegment(3, 3, "second"),
            new TandemConcatenation.TandemInjectionSegment(6, 3, "third"),
        };

        static (double cell, double titrant) ExpectedState(
            DilutionMethod method,
            double cellVolume,
            double syringeConcentration,
            double initialCellConcentration,
            double cumulativeInjectedVolume)
        {
            var relativeVolume = cumulativeInjectedVolume / cellVolume;
            if (method == DilutionMethod.Exponential)
            {
                var factor = Math.Exp(-relativeVolume);
                return (
                    initialCellConcentration * factor,
                    syringeConcentration * (1.0 - factor));
            }

            var halfRelativeVolume = cumulativeInjectedVolume / (2.0 * cellVolume);
            var microCalFactor = (1.0 - halfRelativeVolume) / (1.0 + halfRelativeVolume);
            return (
                initialCellConcentration * microCalFactor,
                syringeConcentration * relativeVolume * (1.0 - halfRelativeVolume));
        }

        static (double retention, double titrant) ReferenceCurve(
            DilutionMethod method,
            double relativeVolume)
        {
            if (method == DilutionMethod.Exponential)
            {
                var retention = Math.Exp(-relativeVolume);
                return (retention, 1.0 - retention);
            }

            var halfRelativeVolume = relativeVolume / 2.0;
            return (
                (1.0 - halfRelativeVolume) / (1.0 + halfRelativeVolume),
                relativeVolume * (1.0 - halfRelativeVolume));
        }

        static void AssertSegmentStartsMatchPreviousInjection(ExperimentData experiment)
        {
            foreach (var segment in experiment.Segments.Skip(1))
            {
                var previous = experiment.Injections[segment.FirstInjectionID - 1];
                AssertClose(previous.ActualCellConcentration, segment.SegmentInitialActiveCellConc);
                AssertClose(previous.ActualTitrantConcentration, segment.SegmentInitialActiveTitrantConc);
            }
        }

        static void AssertConverges(
            ExperimentData baseline,
            ExperimentData largeFraction,
            ExperimentData smallFraction,
            ExperimentData tinyFraction)
        {
            var largeDistance = StateDistance(baseline, largeFraction);
            var smallDistance = StateDistance(baseline, smallFraction);
            var tinyDistance = StateDistance(baseline, tinyFraction);

            Assert.True(largeDistance > smallDistance);
            Assert.True(smallDistance > tinyDistance);
        }

        static double StateDistance(ExperimentData left, ExperimentData right)
        {
            return left.Injections.Zip(
                    right.Injections,
                    (leftInjection, rightInjection) => Math.Max(
                        Math.Abs(leftInjection.ActualCellConcentration - rightInjection.ActualCellConcentration),
                        Math.Abs(leftInjection.ActualTitrantConcentration - rightInjection.ActualTitrantConcentration)))
                .Max();
        }

        static double SegmentStateDistance(ExperimentData left, ExperimentData right)
        {
            return left.Segments.Zip(
                    right.Segments,
                    (leftSegment, rightSegment) => Math.Max(
                        Math.Abs(leftSegment.SegmentInitialActiveCellConc - rightSegment.SegmentInitialActiveCellConc),
                        Math.Abs(leftSegment.SegmentInitialActiveTitrantConc - rightSegment.SegmentInitialActiveTitrantConc)))
                .Max();
        }

        static void AssertInjectionStatesEqual(ExperimentData expected, ExperimentData actual)
        {
            Assert.Equal(expected.Injections.Count, actual.Injections.Count);
            for (var index = 0; index < expected.Injections.Count; index++)
            {
                AssertClose(expected.Injections[index].ActualCellConcentration, actual.Injections[index].ActualCellConcentration);
                AssertClose(expected.Injections[index].ActualTitrantConcentration, actual.Injections[index].ActualTitrantConcentration);
                AssertClose(expected.Injections[index].Ratio, actual.Injections[index].Ratio);
            }
        }

        static void AssertSegmentStatesEqual(ExperimentData expected, ExperimentData actual)
        {
            Assert.Equal(expected.Segments.Count, actual.Segments.Count);
            for (var index = 0; index < expected.Segments.Count; index++)
            {
                AssertClose(expected.Segments[index].SegmentInitialActiveCellConc, actual.Segments[index].SegmentInitialActiveCellConc);
                AssertClose(expected.Segments[index].SegmentInitialActiveTitrantConc, actual.Segments[index].SegmentInitialActiveTitrantConc);
            }
        }

        static void AssertClose(double expected, double actual) =>
            Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-14);
    }
}
