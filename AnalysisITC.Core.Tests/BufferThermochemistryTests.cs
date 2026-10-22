using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Core.Tests;

public sealed class BufferThermochemistryTests
{
    [Theory]
    [InlineData(Buffer.Succinate)]
    [InlineData(Buffer.Maleate)]
    [InlineData(Buffer.Null)]
    [InlineData((Buffer)999)]
    public void MissingThermochemistryIsUnavailableRatherThanZero(Buffer buffer)
    {
        Assert.False(buffer.TryGetIonizationEnthalpy(25, out var value));
        Assert.True(double.IsNaN(value));
        Assert.True(double.IsNaN(buffer.GetIonizationEnthalpy(25)));
        var data = CreateData(buffer, "missing");
        Assert.False(BufferAttribute.TryGetIonizationEnthalpy(data, out var energy));
        Assert.True(double.IsNaN(energy.Value));
        Assert.True(double.IsNaN(BufferAttribute.GetIonizationEnthalpy(data).Value));
    }

    [Fact]
    public void MissingAttributesAreUnavailable()
    {
        Assert.False(BufferAttribute.TryGetIonizationEnthalpy(null, out _));
        Assert.False(BufferAttribute.TryGetIonizationEnthalpy(CreateData(null, "no buffer"), out _));
    }

    [Fact]
    public void OmittedAndExplicitZeroConstructorArgumentsRemainDistinct()
    {
        var missing = new[]
        {
            new BufferAttribute("missing", 7, 0, 0, ""),
            new BufferAttribute("missing", 7, 0, 0, "", (double[])null),
            new BufferAttribute(new[] { "missing", "alias" }, 7, 0, 0, "", null),
            new BufferAttribute("missing", new[] { 7.0 }, new[] { 0.0 }, new[] { 0 }, ""),
            new BufferAttribute(new[] { "missing", "alias" }, new[] { 7.0 }, new[] { 0.0 }, new[] { 0 }, ""),
        };
        Assert.All(missing, properties =>
        {
            Assert.Null(properties.IonizationEnthalpy);
            Assert.False(properties.TryGetIonizationEnthalpy(25, out _));
            Assert.Contains("enthalpy unavailable", Tooltip(properties));
        });
        foreach (var zero in new[]
        {
            new BufferAttribute("zero", 7, 0, 0, "", 0),
            new BufferAttribute("zero", 7, 0, 0, "", new[] { 0.0, 0.0 }),
        })
        {
            Assert.True(zero.TryGetIonizationEnthalpy(25, out var enthalpy));
            Assert.Equal(0, enthalpy);
            Assert.Contains("@25°C = 0", Tooltip(zero));
            Assert.DoesNotContain("unavailable", Tooltip(zero));
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    public void NonfiniteTemperatureOrEvaluationIsUnavailable(double temperature)
    {
        Assert.False(Buffer.Hepes.TryGetIonizationEnthalpy(temperature, out var value));
        Assert.True(double.IsNaN(value));
    }

    [Fact]
    public void EveryBufferRemainsSelectable()
    {
        Assert.All(Enum.GetValues<Buffer>(), buffer => Assert.Contains(buffer, BufferAttribute.GetUIBuffers()));
    }

    [Theory]
    [InlineData(Buffer.MES, Buffer.Succinate, false)]
    [InlineData(Buffer.MES, Buffer.Hepes, true)]
    [InlineData(Buffer.SodiumPhosphate, Buffer.PotassiumPhosphate, false)]
    public void EligibilityDependsOnAvailableDistinctCoordinates(Buffer first, Buffer second, bool expected)
    {
        var result = CreateResult(first, second);
        Assert.Equal(expected, result.IsProtonationAnalysisEnabled);
        Assert.Equal(expected, result.ProtonationAnalysis != null);
        Assert.Equal(expected, string.IsNullOrEmpty(result.ProtonationAnalysisUnavailableReason));
        Assert.All(result.Solution.Solutions, member => Assert.Equal(-25000, member.TotalEnthalpy.Value));
        if (expected)
            Assert.Equal(new[] { 14800.0, 20400.0 }, result.ProtonationAnalysis.DataPoints.Select(point => point.Item1));
        else if (second == Buffer.Succinate)
        {
            Assert.Contains("Succinate", result.ProtonationAnalysisUnavailableReason);
            Assert.Contains("experiment-2", result.ProtonationAnalysisUnavailableReason);
        }
        else Assert.Contains("two distinct", result.ProtonationAnalysisUnavailableReason);
    }

    [Fact]
    public void MissingMemberDisablesWholeCollectionAndDirectAnalysisRejectsIt()
    {
        var result = CreateResult(Buffer.MES, Buffer.Hepes, Buffer.Succinate);
        Assert.False(result.IsProtonationAnalysisEnabled);
        Assert.Null(result.ProtonationAnalysis);
        Assert.Equal(3, result.Solution.Solutions.Count);
        Assert.Throws<InvalidOperationException>(() => new ProtonationAnalysis(result));
        var missing = CreateResult(Buffer.MES, null, Buffer.Null);
        Assert.Contains("No buffer metadata (experiment-2)", missing.ProtonationAnalysisUnavailableReason);
        Assert.Contains("Generic Buffer (experiment-3)", missing.ProtonationAnalysisUnavailableReason);
    }

    [Fact]
    public void NonfiniteMemberCannotEnableAnalysisAndMissingFigureAttributesAreOmitted()
    {
        var result = CreateResult(Buffer.MES, Buffer.Hepes);
        result.Solution.Solutions[1].Data.MeasuredTemperature = double.PositiveInfinity;
        Assert.Contains("experiment-2", ProtonationAnalysisAvailability.GetUnavailableReason(result.Solution.Solutions));
        Assert.Throws<InvalidOperationException>(() => new ProtonationAnalysis(result));
        var missing = CreateResult(Buffer.Succinate).Solution.Solutions.Single();
        Assert.Empty(missing.UIExperimentModelAttributes(DisplayAttributeOptions.ProtonationEnthalpy));
        Assert.Single(CreateResult(Buffer.MES).Solution.Solutions.Single()
            .UIExperimentModelAttributes(DisplayAttributeOptions.ProtonationEnthalpy));
    }

    [Fact]
    public void TablesAndExportsDoNotInventMissingBufferValues()
    {
        var result = CreateResult(Buffer.MES, Buffer.Succinate);
        var table = AnalysisResultOverviewTable.Build(result.PresentationData, EnergyUnitFamily.Joules, null, false);
        Assert.DoesNotContain(table.Columns, column => column.Id == "HPROT");
        Assert.Equal("", table.Rows[1]["HPROT"]);
        var exported = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions());
        Assert.DoesNotContain("bufferprotonation", exported);
    }

    [Fact]
    public void TablesAndExportsNameIonizationCoordinatesWithoutNegatingThem()
    {
        var result = CreateResult(Buffer.MES, Buffer.Hepes);
        var table = AnalysisResultOverviewTable.Build(result.PresentationData, EnergyUnitFamily.Joules,
            EnergyUnit.KiloJoule, false);
        Assert.Equal("∆H,ion (kJ/mol)", table.Columns.Single(column => column.Id == "HPROT").Title);
        Assert.Equal(14.8.ToString("F1", System.Globalization.CultureInfo.CurrentCulture), table.Rows[0]["HPROT"]);
        Assert.Equal(20.4.ToString("F1", System.Globalization.CultureInfo.CurrentCulture), table.Rows[1]["HPROT"]);
        var exported = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            EnergyUnitOverride = EnergyUnit.KiloJoule,
            RowMode = AnalysisResultExportRowMode.AllRows,
            FileFormat = AnalysisResultExportFileFormat.TSV,
        });
        Assert.Contains("∆H,ion (kJ/mol)", exported);
        Assert.DoesNotContain("∆H,prot", exported);
        Assert.Contains("14.8", exported);
        Assert.Contains("20.4", exported);
    }

    [Fact]
    public void SortingPlacesUnavailableValuesAfterKnownEnthalpies()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        try
        {
            var missing = CreateData(Buffer.Succinate, "missing");
            var high = CreateData(Buffer.Hepes, "high");
            var low = CreateData(Buffer.Acetate, "low");
            var absent = CreateData(null, "absent");
            DataManager.AddData(new[] { missing, high, absent, low });
            DataManager.SortContent(DataManager.SortMode.ProtonationEnthalpy);
            Assert.Equal(new[] { low, high, missing, absent }, DataManager.SourceItems);
        }
        finally { DataManager.Clear(DataClearMode.ResetSession); }
    }

    [Fact]
    public void RegistryDistinguishesMissingStepsCoefficientsAndGenuineZero()
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"schemaVersion":1,"buffers":[
              {"id":"zero","pKa":[7],"thermoSteps":[{"dH298_Jmol":0,"dCp_JmolK":0}]},
              {"id":"missing","pKa":[7],"thermoSteps":[null]},
              {"id":"omitted","pKa":[7],"thermoSteps":[{}]},
              {"id":"missing-step","pKa":[3,7],"thermoSteps":[{"dH298_Jmol":123,"dCp_JmolK":0}]}
            ]}
            """));
        var registry = BufferRegistry.LoadFromStream(json);
        Assert.True(BufferRegistry.TryGetIonizationEnthalpy(registry.Resolve("zero"), 298.15, 7, out var zero));
        Assert.Equal(0, zero);
        Assert.Equal(0, BufferRegistry.GetProtonationEnthalpy(registry.Resolve("zero"), 298.15, 7));
        foreach (var id in new[] { "missing", "omitted", "missing-step", "unknown" })
        {
            Assert.False(BufferRegistry.TryGetIonizationEnthalpy(registry.Resolve(id), 298.15, 7, out var value));
            Assert.True(double.IsNaN(value));
            Assert.True(double.IsNaN(BufferRegistry.GetProtonationEnthalpy(registry.Resolve(id), 298.15, 7)));
        }
        Assert.False(BufferRegistry.TryGetIonizationEnthalpy(registry.Resolve("zero"), double.NaN, 7, out _));
        Assert.False(BufferRegistry.TryGetIonizationEnthalpy(registry.Resolve("zero"), 298.15, double.NaN, out _));
        var builtIn = BufferRegistry.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Buffers.json"));
        Assert.False(BufferRegistry.TryGetIonizationEnthalpy(builtIn.Resolve("succinate"), 298.15, 5, out _));
        Assert.False(BufferRegistry.TryGetIonizationEnthalpy(builtIn.Resolve("null"), 298.15, 7, out _));
    }

    static string Tooltip(BufferAttribute properties) => (string)typeof(BufferAttribute)
        .GetMethod("GetBufferTooltip", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(properties, null);

    static ExperimentData CreateData(Buffer? buffer, string name)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name, MeasuredTemperature = 25, TargetTemperature = 25,
            CellConcentration = new FloatWithError(30e-6), SyringeConcentration = new FloatWithError(400e-6), CellVolume = 1.4e-3,
        };
        if (buffer.HasValue)
        {
            var attribute = ExperimentAttribute.FromKey(AttributeKey.Buffer);
            attribute.IntValue = (int)buffer.Value;
            attribute.DoubleValue = 7;
            attribute.ParameterValue = new FloatWithError(0.05);
            data.Attributes.Add(attribute);
        }
        return data;
    }

    internal static AnalysisResult CreateResult(params Buffer?[] buffers)
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        var models = buffers.Select((buffer, index) =>
        {
            var model = new OneSetOfSites(CreateData(buffer, "experiment-" + (index + 1)));
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            model.Solution = SolutionInterface.FromModel(model, convergence);
            return (Model)model;
        }).ToList();
        var global = new GlobalModel(models) { ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(model => model.Solution).ToList(), convergence);
        global.Solution = solution;
        return new AnalysisResult(solution);
    }
}
