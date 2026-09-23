using System;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class PytcInjectionHeatTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    public PytcInjectionHeatTests() => PreferencesState.Defaults().ApplyToSettings();
    public void Dispose() => original.ApplyToSettings();

    [Fact]
    public void ConcentrationsFollowIndividualShotsNotTheirSum()
    {
        var split = Data(20e-6, 400e-6, 20e-6, 40e-6);
        var single = Data(20e-6, 400e-6, 60e-6);
        RawDataReader.ProcessInjections(split, DilutionMethod.DiscreteDisplacement);
        RawDataReader.ProcessInjections(single, DilutionMethod.DiscreteDisplacement);
        // Retained fractions are .9*.8=.72 versus .7 for one shot.
        Assert.InRange(Math.Abs(20e-6 * .72 - split.Injections[1].ActualCellConcentration), 0, 1e-18);
        Assert.InRange(Math.Abs(400e-6 * .28 - split.Injections[1].ActualTitrantConcentration), 0, 1e-18);
        Assert.NotEqual(single.Injections[0].ActualCellConcentration, split.Injections[1].ActualCellConcentration);
        Assert.Throws<ArgumentException>(() => InjectionDisplacementCalculator.Calculate(
            DilutionMethod.DiscreteDisplacement, 200e-6, 400e-6, 20e-6, 60e-6));
    }

    [Fact]
    public void DiscreteHeatUsesTwoStatesAndBalancesIncomingHeat()
    {
        var calls = 0;
        var before = new InjectionConcentrationState(2, 3);
        var after = new InjectionConcentrationState(1, 4);
        var actual = InjectionHeatCalculator.DiscreteDisplacement(4, 1, before, after,
            (m, l) => { calls++; return m + 2*l; }, 0.5);
        // Retain 3/4 of the previous 8 J, finish with 9 J, receive .5 J.
        Assert.Equal(2.5, actual);
        Assert.Equal(2, calls);
        Assert.Equal(0, InjectionHeatCalculator.DiscreteDisplacement(4, 0, before, before,
            (_, _) => throw new InvalidOperationException("Zero shots need no equilibrium evaluation.")));
        Assert.Equal(0, InjectionHeatCalculator.DiscreteDisplacement(4, 1, before, before, (_, _) => 12, 3));
    }

    [Theory]
    [InlineData(-1e-6)]
    [InlineData(200e-6)]
    [InlineData(201e-6)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidShotRejectsEntireSwitchWithoutChangingSavedState(double invalid)
    {
        var data = Data(20e-6, 400e-6, 2e-6, 3e-6);
        RawDataReader.ProcessInjections(data, DilutionMethod.MicroCal);
        var cells = data.Injections.Select(i => i.ActualCellConcentration).ToArray();
        var revision = data.ProcessingRevision;
        data.Injections[1] = new InjectionData(data, 1, invalid, 0, true)
        {
            ActualCellConcentration = data.Injections[1].ActualCellConcentration,
            ActualTitrantConcentration = data.Injections[1].ActualTitrantConcentration,
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => RawDataReader.ReprocessInjections(data, DilutionMethod.DiscreteDisplacement));
        Assert.Equal(cells, data.Injections.Select(i => i.ActualCellConcentration));
        Assert.Equal(InjectionHeatMethod.Legacy, data.HeatMethod);
        Assert.Equal(DilutionMethod.MicroCal, data.AppliedDilutionMethod);
        Assert.Equal(revision, data.ProcessingRevision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidCellVolumeIsRejected(double volume)
    {
        var data = Data(20e-6, 400e-6, 2e-6);
        data.CellVolume = volume;
        Assert.Throws<ArgumentOutOfRangeException>(() => RawDataReader.ProcessInjections(data, DilutionMethod.DiscreteDisplacement));
        Assert.Null(data.AppliedDilutionMethod);
    }

    [Fact]
    public void CumulativeVolumeCanExceedCellVolumeAndExcludedShotsStillDisplace()
    {
        var data = Data(20e-6, 400e-6, 100e-6, 0, 100e-6, 100e-6, 100e-6, 100e-6);
        data.Injections[2].Include = false;
        RawDataReader.ProcessInjections(data, DilutionMethod.DiscreteDisplacement);
        Assert.Equal(data.Injections[0].ActualTitrantConcentration, data.Injections[1].ActualTitrantConcentration);
        Assert.Equal(20e-6 / 32, data.Injections.Last().ActualCellConcentration);
        Assert.Equal(400e-6 * 31 / 32, data.Injections.Last().ActualTitrantConcentration);
    }

    [Theory]
    [InlineData("one-c100-v0.01")]
    [InlineData("two-site")]
    [InlineData("competitive")]
    [InlineData("dissociation")]
    [InlineData("sequential-2")]
    [InlineData("sequential-4")]
    [InlineData("tandem-one")]
    public void EveryModelRetainsStatelessEvaluationAndOffsetConvention(string id)
    {
        var model = InjectionProcessingMethodTests.FittedModel(id, method: DilutionMethod.DiscreteDisplacement);
        var expected = model.Data.Injections.Select(i => model.Evaluate(i.ID, false)).ToArray();
        model.Data.Injections[1].Include = false;
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        model.Parameters.Table[ParameterType.Offset].Update(123);
        var random = new Random(76);
        foreach (var i in Enumerable.Range(0, expected.Length).Reverse()
            .Concat(Enumerable.Range(0, expected.Length).OrderBy(_ => random.Next())))
        {
            Assert.Equal(expected[i], model.Evaluate(i, false));
            Assert.Equal(expected[i] + 123 * model.Data.Injections[i].InjectionMass, model.Evaluate(i));
        }
    }

    [Fact]
    public void FractionalTwoSiteExtensionAgreesWithKnownFreeLigandStates()
    {
        // Construct the total ligand concentrations from independently chosen free
        // ligand states; the test expectation does not solve the implementation's cubic.
        const double m = 20e-6, u = .1, n1 = .65, n2 = 1.4, k1 = 2e6, k2 = 3e4;
        const double h1 = -23000, h2 = 12000, freeBefore = 5e-6, freeAfter = 25e-6;
        double Occupancy(double k, double free) => k*free/(1+k*free);
        double Total(double macromolecule, double free) => free + macromolecule *
            (n1*Occupancy(k1, free) + n2*Occupancy(k2, free));
        var before = Total(m, freeBefore);
        var after = Total(m*(1-u), freeAfter);
        var data = Data(m, (after-(1-u)*before)/u, 200e-6*u);
        RawDataReader.ProcessInjections(data, DilutionMethod.DiscreteDisplacement);
        data.AddSegment(new TandemExperimentSegment(0, m, before));
        data.Injections[0].ActualTitrantConcentration = after;
        var model = new TwoSetsOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.Table[ParameterType.Nvalue1].Update(n1);
        model.Parameters.Table[ParameterType.Nvalue2].Update(n2);
        model.Parameters.Table[ParameterType.Affinity1].Update(Math.Log10(k1));
        model.Parameters.Table[ParameterType.Affinity2].Update(Math.Log10(k2));
        model.Parameters.Table[ParameterType.Enthalpy1].Update(h1);
        model.Parameters.Table[ParameterType.Enthalpy2].Update(h2);
        var expected = data.CellVolume*m*(1-u) *
            (n1*h1*(Occupancy(k1,freeAfter)-Occupancy(k1,freeBefore))
             + n2*h2*(Occupancy(k2,freeAfter)-Occupancy(k2,freeBefore)));
        Assert.InRange(Math.Abs(model.Evaluate(0, false)/expected-1), 0, 2e-12);
    }

    [Fact]
    public void DissociationExtensionSubtractsSyringeDimersAndUsesSegmentStart()
    {
        const double ka = 5e4, h = -27000, monomerBefore = 2e-6, monomerAfter = 4e-6, u = .01;
        var dimerBefore = ka*monomerBefore*monomerBefore;
        var dimerAfter = ka*monomerAfter*monomerAfter;
        var before = monomerBefore+2*dimerBefore;
        var after = monomerAfter+2*dimerAfter;
        var syringe = (after-(1-u)*before)/u;
        var syringeMonomer = (Math.Sqrt(1+8*ka*syringe)-1)/(4*ka);
        var syringeDimer = ka*syringeMonomer*syringeMonomer;
        var data = Data(0, syringe, 200e-6*u);
        RawDataReader.ProcessInjections(data, DilutionMethod.DiscreteDisplacement);
        data.AddSegment(new TandemExperimentSegment(0, 0, before));
        data.Injections[0].ActualTitrantConcentration = after;
        var model = new Dissociation(data);
        model.InitializeParameters(data);
        model.Parameters.Table[ParameterType.Affinity1].Update(Math.Log10(ka));
        model.Parameters.Table[ParameterType.Enthalpy1].Update(h);
        var expected = h*data.CellVolume*(dimerAfter-(1-u)*dimerBefore-u*syringeDimer);
        Assert.InRange(Math.Abs(model.Evaluate(0, false)/expected-1), 0, 2e-14);
    }

    [Theory]
    [InlineData(false, 6.0/7, 1.0/7, .6, .4)]
    [InlineData(true, 5.0/6, 1.0/6, 7.0/12, 5.0/12)]
    public void TandemMatchesIndependentWholeCompartmentBalance(bool removeOverflow,
        double startCell, double startLigand, double postCell, double postLigand)
    {
        var data = Data(1, 1, .2, .3, .1);
        data.CellVolume = 1;
        data.Injections[0].Include = false;
        TandemConcatenation.ProcessInjectionsWithBackMixing(data,
            new[] { new TandemConcatenation.TandemInjectionSegment(0, 1),
                    new TandemConcatenation.TandemInjectionSegment(1, 2) },
            new TandemConcatenation.BackMixingSettings
            { UseBackMixingMethod = true, DeadVolume = .2, DidRemoveOverflow = removeOverflow },
            new[] { 1.0 }, DilutionMethod.DiscreteDisplacement);
        // First shot expels only cell material. Without removal: 1.2 mol of
        // original material and .2 mol ligand mix in 1.4 L. Removing the .2 L
        // overflow first instead leaves 1 mol original material in 1.2 L.
        Assert.Equal(startCell, data.Segments[1].SegmentInitialActiveCellConc, 14);
        Assert.Equal(startLigand, data.Segments[1].SegmentInitialActiveTitrantConc, 14);
        Assert.Equal(postCell, data.Injections[1].ActualCellConcentration, 14);
        Assert.Equal(postLigand, data.Injections[1].ActualTitrantConcentration, 14);
        Assert.Equal(startCell*.7*.9, data.Injections[2].ActualCellConcentration, 14);
        Assert.Equal(startLigand*.7*.9 + (1-.7*.9), data.Injections[2].ActualTitrantConcentration, 14);
        Assert.Equal(InjectionHeatMethod.DiscreteDisplacement, data.HeatMethod);
    }

    [Fact]
    public void ExplicitSwitchInvalidatesFitButPreservesMeasuredProcessing()
    {
        var model = InjectionProcessingMethodTests.FittedModel();
        var snapshot = ExperimentFitInputSnapshot.Capture(model);
        var measured = model.Data.Injections.Select(i => i.PeakArea.Value).ToArray();
        var processor = model.Data.Processor;
        RawDataReader.ReprocessInjections(model.Data, DilutionMethod.DiscreteDisplacement);
        Assert.False(model.Solution.IsValid);
        Assert.Same(processor, model.Data.Processor);
        Assert.Equal(measured, model.Data.Injections.Select(i => i.PeakArea.Value));
        Assert.Equal(InjectionHeatMethod.IdealContinuousMixing, model.HeatMethod);
        Assert.Equal(InjectionHeatMethod.DiscreteDisplacement, new OneSetOfSites(model.Data).HeatMethod);
        var reasons = new System.Collections.Generic.List<string>();
        Assert.True(snapshot.Compare(model, reasons));
        Assert.Contains(reasons, r => r.Contains("bookkeeping"));
    }

    internal static ExperimentData Data(double cell, double syringe, params double[] volumes)
    {
        var data = new ExperimentData("pytc-test")
        { CellVolume = 200e-6, CellConcentration = new(cell), SyringeConcentration = new(syringe), MeasuredTemperature = 25 };
        foreach (var volume in volumes)
        {
            var injection = new InjectionData(data, data.Injections.Count, volume, volume*syringe, true);
            injection.SetPeakArea(new(-1e-5, 1e-9));
            data.Injections.Add(injection);
        }
        return data;
    }
}
