using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class TaInstrumentTests
{
    [Theory]
    [InlineData(167, null, null, ITCInstrument.TAInstrumentsNanoITCLowVolume)]
    [InlineData(182, null, null, ITCInstrument.TAInstrumentsAffinityITCLowVolume)]
    [InlineData(950, null, null, ITCInstrument.TAInstrumentsNanoITCStandard)]
    [InlineData(965, null, null, ITCInstrument.TAInstrumentsAffinityITCStandard)]
    [InlineData(177.5, null, null, ITCInstrument.TAInstrumentsITCLowVolume)]
    [InlineData(957.5, null, null, ITCInstrument.TAInstrumentsITCStandard)]
    [InlineData(50, null, null, ITCInstrument.TAInstrumentsITCLowVolume)]
    [InlineData(2000, null, null, ITCInstrument.TAInstrumentsITCStandard)]
    [InlineData(167, "Instrument Type: Affinity (Dual-board)", "False", ITCInstrument.TAInstrumentsAffinityITCLowVolume)]
    [InlineData(182, "Other setting: 1\r\n INSTRUMENT  TYPE : nano itc  \r\n", "True", ITCInstrument.TAInstrumentsNanoITCLowVolume)]
    [InlineData(182, "Instrument Type: NanoITC", null, ITCInstrument.TAInstrumentsNanoITCLowVolume)]
    [InlineData(950, "Instrument Type: Affinity ITC", null, ITCInstrument.TAInstrumentsAffinityITCStandard)]
    [InlineData(965, "Instrument Type: Nano ITC", "True", ITCInstrument.TAInstrumentsNanoITCStandard)]
    [InlineData(167, null, "True", ITCInstrument.TAInstrumentsAffinityITCLowVolume)]
    [InlineData(950, "Instrument Type: Unrecognized", " true ", ITCInstrument.TAInstrumentsAffinityITCStandard)]
    [InlineData(167, null, "False", ITCInstrument.TAInstrumentsNanoITCLowVolume)]
    [InlineData(182, null, "invalid", ITCInstrument.TAInstrumentsAffinityITCLowVolume)]
    [InlineData(177.5, "Not Instrument Type: Nano ITC", "False", ITCInstrument.TAInstrumentsITCLowVolume)]
    [InlineData(177.5, "Instrument Type: Nano ITC successor", null, ITCInstrument.TAInstrumentsITCLowVolume)]
    [InlineData(200, "Instrument Type: Nano ITC", null, ITCInstrument.TAInstrumentsNanoITCLowVolume)]
    [InlineData(201, "Instrument Type: Nano ITC", null, ITCInstrument.TAInstrumentsNanoITCStandard)]
    public void TaIdentificationUsesExplicitMetadataBeforeVolume(double microlitres, string settings, string flag, ITCInstrument expected)
        => Assert.Equal(expected, ITCInstrumentAttribute.ResolveTAInstrument(microlitres * 1e-6, settings, flag));

    [Theory]
    [InlineData(167, ITCInstrument.TAInstrumentsNanoITCLowVolume)]
    [InlineData(182, ITCInstrument.TAInstrumentsAffinityITCLowVolume)]
    [InlineData(950, ITCInstrument.TAInstrumentsNanoITCStandard)]
    [InlineData(965, ITCInstrument.TAInstrumentsAffinityITCStandard)]
    [InlineData(204, ITCInstrument.MicroCalITC200)]
    [InlineData(207.1, ITCInstrument.MalvernITC200)]
    [InlineData(1479.1, ITCInstrument.MicroCalVPITC)]
    [InlineData(177.5, ITCInstrument.TAInstrumentsITCLowVolume)]
    [InlineData(50, ITCInstrument.Unknown)]
    [InlineData(0, ITCInstrument.Unknown)]
    [InlineData(double.NaN, ITCInstrument.Unknown)]
    [InlineData(double.PositiveInfinity, ITCInstrument.Unknown)]
    public void UnidentifiedIntegratedHeatsUseAllActualInstrumentVolumes(double microlitres, ITCInstrument expected)
    {
        var data = new ExperimentData("integrated.dat") { CellVolume = microlitres * 1e-6, DataSourceFormat = ITCDataFormat.IntegratedHeats };
        ITCInstrumentAttribute.ResolveInstrument(data);
        Assert.Equal(expected, data.Instrument);
        if (!double.IsNaN(microlitres)) Assert.Equal(microlitres * 1e-6, data.CellVolume);
    }

    [Fact]
    public void VolumeEligibilityKeepsTheExistingThirtyPercentRule()
    {
        var outside = new ExperimentData("outside.dat") { CellVolume = 118e-6 };
        var inside = new ExperimentData("inside.dat") { CellVolume = 120e-6 };
        ITCInstrumentAttribute.ResolveInstrument(outside);
        ITCInstrumentAttribute.ResolveInstrument(inside);
        Assert.Equal(ITCInstrument.Unknown, outside.Instrument);
        Assert.Equal(ITCInstrument.TAInstrumentsNanoITCLowVolume, inside.Instrument);
    }

    [Fact]
    public void ExistingGenericIdentitiesAreNotReclassified()
    {
        foreach (var instrument in new[] { ITCInstrument.TAInstrumentsITCLowVolume, ITCInstrument.TAInstrumentsITCStandard })
        {
            var data = new ExperimentData("saved.ftxtc") { CellVolume = 182e-6, Instrument = instrument };
            ITCInstrumentAttribute.ResolveInstrument(data);
            Assert.Equal(instrument, data.Instrument);
        }
    }

    [Theory]
    [InlineData(ITCInstrument.TAInstrumentsNanoITCLowVolume, 170, 50, 60)]
    [InlineData(ITCInstrument.TAInstrumentsNanoITCStandard, 950, 250, 200)]
    [InlineData(ITCInstrument.TAInstrumentsAffinityITCLowVolume, 185, 250, 60)]
    [InlineData(ITCInstrument.TAInstrumentsAffinityITCStandard, 965, 250, 200)]
    public void DesignerDefaultsUseEffectiveCellAndSupportedSyringeVolumes(ITCInstrument instrument, double cell, double syringe, double dead)
    {
        Assert.Contains(instrument, ITCInstrumentAttribute.GetITCInstruments());
        var properties = instrument.GetProperties();
        Assert.Equal(cell * 1e-6, properties.StandardCellVolume, 12);
        Assert.Equal(syringe * 1e-6, properties.StandardSyringeVolume, 12);
        Assert.Equal(dead * 1e-6, properties.DeadVolume, 12);
    }

    [Fact]
    public void SharedDesignerListOffersFourActualTaInstrumentsAndNoGenericPresets()
    {
        var ta = ITCInstrumentAttribute.GetITCInstruments().Where(ITCInstrumentAttribute.IsTAInstrument).ToArray();
        Assert.Equal(4, ta.Length);
        Assert.DoesNotContain(ITCInstrument.TAInstrumentsITCLowVolume, ta);
        Assert.DoesNotContain(ITCInstrument.TAInstrumentsITCStandard, ta);
        Assert.Equal(ITCInstrument.Unknown, ITCInstrumentAttribute.TryResolveMicroCalInstrument("TAITC"));
    }

    [Fact]
    public void NanoMapperUsesGlucoseMetadataAndPreservesItsActualVolume()
    {
        var document = new NanoItcDocument();
        document.Tables.Add("DataPointsTable", Table("DataPointsTable", new[] { "Time", "HeatRate", "Temperture" },
            new double[] { 0, 1 }, new double[] { -161, -160 }, new double[] { 25, 25 }));
        document.Tables.Add("InjectionsTable", Table("InjectionsTable", new[] { "StartTime", "Size", "InjectionInterval" },
            new double[] { 0 }, new double[] { 1 }, new double[] { 1 }));
        document.Tables.Add("KVP", Table("KVP", new[] { "Key", "Value" },
            new[] { "cellvolume", "cellType", "IsAffinityITCData", "OtherInstrumentSettings" },
            new[] { "167", "LV (170 µL)", "False", "Instrument Type: Nano ITC" }));

        var data = NanoItcExperimentMapper.Map(document, "glucose.nitc", null);

        Assert.Equal(ITCInstrument.TAInstrumentsNanoITCLowVolume, data.Instrument);
        Assert.Equal(167e-6, data.CellVolume, 12);
        Assert.Equal(2, data.DataPoints.Count);
        Assert.Equal(-161e-6, data.DataPoints[0].Power, 10);
    }

    static NanoItcTable Table(string name, string[] columns, params Array[] stores)
    {
        var rows = stores[0].Length;
        return new NanoItcTable(name, columns, stores,
            stores.Select(_ => new NanoItcBitArray(new int[(rows + 31) / 32], rows)).ToArray(),
            Enumerable.Range(0, rows).ToArray());
    }
}
