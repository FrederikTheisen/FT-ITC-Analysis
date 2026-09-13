using System;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition("Dissociation scientific validation", DisableParallelization = true)]
    public sealed class DissociationScientificValidationCollectionDefinition
    {
    }

    /// <summary>
    /// Scientific validation for the monomer-dimer dilution model.
    ///
    /// The expected heats in ReferenceMolarHeatsJoulesPerMole were calculated
    /// independently from C = M + 2D, Ka = D/M^2 and the stated displacement
    /// balance. They are deliberately frozen values rather than calls into the
    /// production model.
    /// </summary>
    [Collection("Dissociation scientific validation")]
    public sealed class DissociationScientificReferenceTests : IDisposable
    {
        readonly PreferencesState originalPreferences;

        public DissociationScientificReferenceTests()
        {
            originalPreferences = PreferencesState.FromSettings();
            PreferencesState.Defaults().ApplyToSettings();
            // The recovery assertions intentionally use the tightest solver
            // preference; this is restored in Dispose for the whole process.
            AppSettings.OptimizerTolerance = 1.0;
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            AppSettings.ApplySettings();
        }

        public void Dispose()
        {
            originalPreferences.ApplyToSettings();
            AppSettings.ApplySettings();
        }

        const double CellVolume = 1.4e-3;
        const double SyringeConcentration = 1.2e-3;
        const double AssociationConstant = 1.0 / 32e-6;
        const double AssociationEnthalpyJoulesPerMole = -18000.0;
        const double OffsetJoulesPerMoleInjectant = 1250.0;
        const double ReferenceStandardDeviationJoules = 1e-10;

        // The first injection is the usual excluded priming injection.
        static readonly double[] InjectionVolumes =
            { 0.25e-6, 2.5e-6, 2.5e-6, 2.5e-6, 2.5e-6, 2.5e-6,
              2.5e-6, 2.5e-6, 2.5e-6, 2.5e-6, 2.5e-6, 2.5e-6 };

        // Independently derived absolute heats in J, including the stated
        // 1250 J/mol injectant offset.
        static readonly double[] ReferenceHeatsJoules =
        {
            2.7454882867272259e-06,
            2.4417687884941452e-05,
            2.0665578446908085e-05,
            1.8270168154081933e-05,
            1.6565237163614344e-05,
            1.5268429687424486e-05,
            1.4236543444133263e-05,
            1.3388116093600499e-05,
            1.2672920043430164e-05,
            1.2058073794040459e-05,
            1.1521028086894815e-05,
            1.1045734501527051e-05,
        };

        // Independent normalized values in J/mol injectant. These also make
        // the absolute-to-molar unit convention reviewable in the test.
        static readonly double[] ReferenceMolarHeatsJoulesPerMole =
        {
            9151.6276224240883,
            8139.2292949804842,
            6888.5261489693621,
            6090.0560513606442,
            5521.7457212047811,
            5089.4765624748288,
            4745.5144813777542,
            4462.7053645335,
            4224.3066811433882,
            4019.3579313468194,
            3840.3426956316048,
            3681.9115005090171,
        };

        // Independent references using the actual concentration traces
        // produced by RawDataReader.ProcessInjectionsUsingMethod.
        static readonly double[] MicroCalHeatsJoules =
        {
            2.7454944953515147e-06, 2.4423710993969483e-05,
            2.0692924526260129e-05, 1.8326444659368964e-05,
            1.6654250769721096e-05, 1.5392500040773075e-05,
            1.4397258651845049e-05, 1.358664987925958e-05,
            1.2910187800631806e-05, 1.2334818331186588e-05,
            1.1837870733363286e-05, 1.1403207531195256e-05,
        };

        static readonly double[] ExponentialHeatsJoules =
        {
            2.7454944949820046e-06, 2.4423707049894225e-05,
            2.069288681955209e-05, 1.8326321271361823e-05,
            1.6653981109238686e-05, 1.5392019140077297e-05,
            1.4396498853466679e-05, 1.3585541675721365e-05,
            1.2908660317455282e-05, 1.2332799632937247e-05,
            1.1835288031380931e-05, 1.1399987332017695e-05,
        };

        static readonly double[] MicroCalPostConcentrations =
        {
            2.1426658163265305e-07, 2.3548278061224489e-06,
            4.4915625e-06, 6.6244706632653058e-06,
            8.7535522959183673e-06, 1.0878807397959183e-05,
            1.3000235969387755e-05, 1.5117838010204082e-05,
            1.7231613520408161e-05, 1.93415625e-05,
            2.1447684948979591e-05, 2.3549980867346937e-05,
        };

        static readonly double[] ExponentialPostConcentrations =
        {
            2.1426658277139408e-07, 2.3548293211855585e-06,
            4.4915730369947669e-06, 6.6245045437967047e-06,
            8.7536306430330502e-06, 1.0878958124010784e-05,
            1.3000493763923914e-05, 1.511824432787505e-05,
            1.7232216568897263e-05, 1.9342417227975116e-05,
            2.1448853034066671e-05, 2.3551530704124654e-05,
        };

        [Fact]
        public void IndependentMonomerDimerReferenceConservesMassAndHasPhysicalLimits()
        {
            foreach (var total in new[] { 0.0, 1e-12, 2e-8, 2e-6, 2e-4, 1e-2 })
            {
                var monomer = MonomerFromTotal(total, AssociationConstant);
                var dimer = AssociationConstant * monomer * monomer;
                var scale = Math.Max(total, 1e-30);

                Assert.InRange(monomer, 0.0, total);
                Assert.InRange(dimer, 0.0, total / 2.0);
                Assert.InRange(Math.Abs(monomer + 2.0 * dimer - total) / scale, 0.0, 2e-14);
            }

            Assert.Equal(0.0, MonomerFromTotal(0.0, AssociationConstant));
            Assert.Equal(0.0, DimerFromTotal(0.0, AssociationConstant));
            Assert.Equal(0.0, DimerFromTotal(1e-3, 0.0));
        }

        [Fact]
        public void ReferenceHeatsMatchSignUnitsAndInjectionDisplacementConvention()
        {
            var model = CreateReferenceModel(includeHeats: false, ConcentrationConvention.ExplicitState);
            var cumulativeVolume = 0.0;
            var previousTotal = 0.0;
            var syringeDimer = DimerFromTotal(SyringeConcentration, AssociationConstant);
            SetParameters(model, AssociationConstant, AssociationEnthalpyJoulesPerMole, OffsetJoulesPerMoleInjectant);

            for (var i = 0; i < InjectionVolumes.Length; i++)
            {
                var injection = model.Data.Injections[i];
                var beforeDimer = DimerFromTotal(previousTotal, AssociationConstant);
                cumulativeVolume += injection.Volume;
                var afterTotal = SyringeConcentration * cumulativeVolume / CellVolume;
                var afterDimer = DimerFromTotal(afterTotal, AssociationConstant);
                var nPre = injection.Volume * syringeDimer
                           + (CellVolume - injection.Volume) * beforeDimer;
                var nPost = CellVolume * afterDimer;
                var independentlyDerived = AssociationEnthalpyJoulesPerMole
                    * (nPost - nPre)
                    + OffsetJoulesPerMoleInjectant * injection.InjectionMass;

                // This assertion is against the frozen external reference;
                // the next assertion is the production-model comparison.
                var expected = ReferenceHeatsJoules[i];
                AssertRelative(expected, independentlyDerived, 2e-12);
                var actual = model.Evaluate(i, withoffset: true);
                AssertRelative(expected, actual, 2e-12);
                AssertRelative(
                    ReferenceMolarHeatsJoulesPerMole[i],
                    independentlyDerived / injection.InjectionMass,
                    2e-12);
                AssertRelative(
                    ReferenceMolarHeatsJoulesPerMole[i],
                    model.EvaluateEnthalpy(i, withoffset: true),
                    2e-12);

                // Negative association enthalpy plus a fall in dimer amount
                // gives a positive dilution heat for this reference case.
                Assert.True(actual > 0.0);
                previousTotal = afterTotal;
            }

            var withOffset = model.Evaluate(5, withoffset: true);
            var withoutOffset = model.Evaluate(5, withoffset: false);
            AssertRelative(
                OffsetJoulesPerMoleInjectant * model.Data.Injections[5].InjectionMass,
                withOffset - withoutOffset,
                2e-12);
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void ImportedDilutionConventionsMatchIndependentStableReference(DilutionMethod method)
        {
            var model = CreateReferenceModel(includeHeats: false, ConventionFor(method));
            SetParameters(model, AssociationConstant, AssociationEnthalpyJoulesPerMole, OffsetJoulesPerMoleInjectant);
            var expectedHeats = method == DilutionMethod.MicroCal
                ? MicroCalHeatsJoules
                : ExponentialHeatsJoules;
            var expectedConcentrations = method == DilutionMethod.MicroCal
                ? MicroCalPostConcentrations
                : ExponentialPostConcentrations;
            var cumulativeVolume = 0.0;
            var previousTotal = 0.0;
            var syringeDimer = DimerFromTotal(SyringeConcentration, AssociationConstant);

            for (var i = 0; i < InjectionVolumes.Length; i++)
            {
                var injection = model.Data.Injections[i];
                cumulativeVolume += injection.Volume;
                var independentlyDerivedTotal = ImportedPostConcentration(method, cumulativeVolume);
                var independentlyDerivedHeat = AssociationEnthalpyJoulesPerMole
                    * (CellVolume * DimerFromTotal(independentlyDerivedTotal, AssociationConstant)
                       - injection.Volume * syringeDimer
                       - (CellVolume - injection.Volume)
                           * DimerFromTotal(previousTotal, AssociationConstant))
                    + OffsetJoulesPerMoleInjectant * injection.InjectionMass;

                AssertRelative(expectedConcentrations[i], independentlyDerivedTotal, 2e-14);
                AssertRelative(expectedConcentrations[i], injection.ActualTitrantConcentration, 2e-14);
                AssertRelative(expectedHeats[i], independentlyDerivedHeat, 2e-12);
                AssertRelative(expectedHeats[i], model.Evaluate(i, withoffset: true), 2e-12);
                Assert.True(model.Evaluate(i, withoffset: false) > 0.0);
                previousTotal = independentlyDerivedTotal;
            }
        }

        [Fact]
        public void ZeroAssociationLimitRetainsOnlyMolarInjectionOffset()
        {
            var model = CreateReferenceModel(includeHeats: false, ConcentrationConvention.ExplicitState);
            SetParameters(model, 0.0, AssociationEnthalpyJoulesPerMole, OffsetJoulesPerMoleInjectant);

            foreach (var injection in model.Data.Injections)
            {
                AssertRelative(
                    OffsetJoulesPerMoleInjectant * injection.InjectionMass,
                    model.Evaluate(injection.ID, withoffset: true),
                    2e-12);
                Assert.Equal(0.0, model.Evaluate(injection.ID, withoffset: false));
            }
        }

        [Fact]
        public void ZeroEnthalpyLimitRemovesReactionHeatExactly()
        {
            var model = CreateReferenceModel(includeHeats: false, ConcentrationConvention.ExplicitState);
            SetParameters(model, AssociationConstant, 0.0, 0.0);

            Assert.All(model.Data.Injections, injection =>
            {
                Assert.Equal(0.0, model.Evaluate(injection.ID, withoffset: false));
                Assert.Equal(0.0, model.Evaluate(injection.ID, withoffset: true));
            });
        }

        [Theory]
        [InlineData(1e-6)]
        [InlineData(1e14)]
        public void ProductionEvaluationMatchesStableReferenceAtAssociationLimits(double associationConstant)
        {
            var model = CreateReferenceModel(includeHeats: false, ConcentrationConvention.ExplicitState);
            SetParameters(model, associationConstant, AssociationEnthalpyJoulesPerMole, 0.0);
            var previousTotal = 0.0;
            var cumulativeVolume = 0.0;
            var syringeDimer = DimerFromTotal(SyringeConcentration, associationConstant);

            foreach (var injection in model.Data.Injections)
            {
                var beforeDimer = DimerFromTotal(previousTotal, associationConstant);
                cumulativeVolume += injection.Volume;
                var afterTotal = SyringeConcentration * cumulativeVolume / CellVolume;
                var afterDimer = DimerFromTotal(afterTotal, associationConstant);
                var expected = AssociationEnthalpyJoulesPerMole
                    * (CellVolume * afterDimer
                       - injection.Volume * syringeDimer
                       - (CellVolume - injection.Volume) * beforeDimer);

                // The 1e-10 relative tolerance is strict enough to detect
                // cancellation in the production quadratic root at weak Ka.
                AssertRelative(expected, model.Evaluate(injection.ID, withoffset: false), 1e-10);
                previousTotal = afterTotal;
            }
        }

        [Theory]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(SolverAlgorithm.NelderMead)]
        public void IndependentlyDerivedReferenceHeatsRecoverAllFreeParameters(SolverAlgorithm algorithm)
        {
            var model = CreateReferenceModel(includeHeats: true, ConcentrationConvention.ExplicitState);
            model.InitializeParameters(model.Data);
            model.Parameters.Table[ParameterType.Affinity1].Update(5.2);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(-12000.0);
            model.Parameters.Table[ParameterType.Offset].Update(0.0);

            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = algorithm,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                // A uniform positive SD only rescales every residual equally;
                // it preserves the unweighted least-squares optimum while
                // putting the residuals on a useful numerical scale.
                UseErrorWeightedFitting = true,
                MaxOptimizerIterations = 20_000,
                SolverToleranceModifier = algorithm == SolverAlgorithm.NelderMead ? 1e-6 : 1,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success, convergence.Message);
            AssertRelative(
                AssociationConstant,
                Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity1].Value),
                2e-7);
            AssertRelative(
                AssociationEnthalpyJoulesPerMole,
                model.Parameters.Table[ParameterType.Enthalpy1].Value,
                2e-7);
            AssertRelative(
                OffsetJoulesPerMoleInjectant,
                model.Parameters.Table[ParameterType.Offset].Value,
                2e-7);
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(DilutionMethod.MicroCal, SolverAlgorithm.NelderMead)]
        [InlineData(DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(DilutionMethod.Exponential, SolverAlgorithm.NelderMead)]
        public void ImportedDilutionReferenceHeatsRecoverAllFreeParameters(
            DilutionMethod method,
            SolverAlgorithm algorithm)
        {
            var model = CreateReferenceModel(includeHeats: true, ConventionFor(method));
            model.InitializeParameters(model.Data);
            model.Parameters.Table[ParameterType.Affinity1].Update(5.2);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(-12000.0);
            model.Parameters.Table[ParameterType.Offset].Update(0.0);

            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = algorithm,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                // A uniform positive SD only rescales every residual equally;
                // it preserves the unweighted least-squares optimum while
                // putting the residuals on a useful numerical scale.
                UseErrorWeightedFitting = true,
                MaxOptimizerIterations = 20_000,
                SolverToleranceModifier = algorithm == SolverAlgorithm.NelderMead ? 1e-6 : 1,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success, convergence.Message);
            AssertRelative(
                AssociationConstant,
                Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity1].Value),
                2e-7);
            AssertRelative(
                AssociationEnthalpyJoulesPerMole,
                model.Parameters.Table[ParameterType.Enthalpy1].Value,
                2e-7);
            AssertRelative(
                OffsetJoulesPerMoleInjectant,
                model.Parameters.Table[ParameterType.Offset].Value,
                2e-7);
        }

        enum ConcentrationConvention
        {
            ExplicitState,
            MicroCal,
            Exponential,
        }

        static ConcentrationConvention ConventionFor(DilutionMethod method) => method switch
        {
            DilutionMethod.MicroCal => ConcentrationConvention.MicroCal,
            DilutionMethod.Exponential => ConcentrationConvention.Exponential,
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

        static double ImportedPostConcentration(DilutionMethod method, double cumulativeVolume)
        {
            var u = cumulativeVolume / CellVolume;
            return method == DilutionMethod.MicroCal
                ? SyringeConcentration * u * (1.0 - u / 2.0)
                : SyringeConcentration * (1.0 - Math.Exp(-u));
        }

        static Dissociation CreateReferenceModel(bool includeHeats, ConcentrationConvention convention)
        {
            var expectedHeats = convention switch
            {
                ConcentrationConvention.MicroCal => MicroCalHeatsJoules,
                ConcentrationConvention.Exponential => ExponentialHeatsJoules,
                _ => ReferenceHeatsJoules,
            };
            var data = new ExperimentData("dissociation-independent-reference.itc")
            {
                CellConcentration = new FloatWithError(0.0),
                SyringeConcentration = new FloatWithError(SyringeConcentration),
                CellVolume = CellVolume,
                MeasuredTemperature = 25.0,
                TargetTemperature = 25.0,
            };

            for (var i = 0; i < InjectionVolumes.Length; i++)
            {
                var injection = new InjectionData(
                    data,
                    i,
                    InjectionVolumes[i],
                    SyringeConcentration * InjectionVolumes[i],
                    include: i != 0)
                {
                    ActualCellConcentration = 0.0,
                    ActualTitrantConcentration = 0.0,
                    Ratio = 0.0,
                };
                if (includeHeats)
                    injection.SetPeakArea(new FloatWithError(expectedHeats[i], ReferenceStandardDeviationJoules));
                else
                    injection.SetPeakArea(new FloatWithError(0.0, 0.0));

                data.Injections.Add(injection);
            }

            if (convention != ConcentrationConvention.ExplicitState)
            {
                RawDataReader.ProcessInjectionsUsingMethod(
                    data,
                    convention == ConcentrationConvention.MicroCal
                        ? DilutionMethod.MicroCal
                        : DilutionMethod.Exponential);
            }
            else
            {
                var cumulativeVolume = 0.0;
                foreach (var injection in data.Injections)
                {
                    cumulativeVolume += injection.Volume;
                    injection.ActualTitrantConcentration = SyringeConcentration * cumulativeVolume / CellVolume;
                }
            }

            var model = new Dissociation(data);
            model.InitializeParameters(data);
            return model;
        }

        static void SetParameters(
            Dissociation model,
            double associationConstant,
            double associationEnthalpyJoulesPerMole,
            double offsetJoulesPerMoleInjectant)
        {
            // Affinity1 stores log10(Ka); the solution maps it to Kd = 1/Ka.
            var logAssociationConstant = associationConstant == 0.0
                ? double.NegativeInfinity
                : Math.Log10(associationConstant);
            model.Parameters.Table[ParameterType.Affinity1].Update(logAssociationConstant);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(associationEnthalpyJoulesPerMole);
            model.Parameters.Table[ParameterType.Offset].Update(offsetJoulesPerMoleInjectant);
        }

        // Independent stable evaluation of 2 Ka M^2 + M - C = 0.
        static double MonomerFromTotal(double total, double associationConstant)
        {
            if (total <= 0.0 || associationConstant <= 0.0) return total;
            return 2.0 * total / (1.0 + Math.Sqrt(1.0 + 8.0 * associationConstant * total));
        }

        static double DimerFromTotal(double total, double associationConstant)
        {
            var monomer = MonomerFromTotal(total, associationConstant);
            return associationConstant * monomer * monomer;
        }

        static void AssertRelative(double expected, double actual, double tolerance)
        {
            var scale = Math.Max(Math.Abs(expected), 1e-30);
            Assert.InRange(Math.Abs(actual - expected) / scale, 0.0, tolerance);
        }
    }
}
