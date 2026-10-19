using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class ResultClipboardTests
{
    [Theory]
    [InlineData(UncertaintyDisplayStyle.None, 1)]
    [InlineData(UncertaintyDisplayStyle.StandardDeviation, 2)]
    [InlineData(UncertaintyDisplayStyle.ConfidenceInterval, 3)]
    [InlineData(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, 4)]
    [InlineData(UncertaintyDisplayStyle.Automatic, 4)]
    public void ClipboardUsesFixedScientificColumnsAndRoundTripsNames(UncertaintyDisplayStyle style, int columns)
    {
        var result = IndependentAssessmentTests.CreateIndependentResult(out var members);
        var names = new[] { "comma, tab\tquote\" and ±\nnext line", "carriage\r\nreturn" };
        for (var i = 0; i < members.Length; i++) members[i].Data.Name = names[i];
        members[0].Parameters[ParameterType.Nvalue1] = new FloatWithError(1.25, 0.1, 1, 1.6);
        members[1].Parameters[ParameterType.Nvalue1] = FloatWithError.NaN;

        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
            var table = Copy(style, () => Exporter.CopyToClipboard(result, EnergyUnit.Joule, false));
            Assert.Equal("da-DK", CultureInfo.CurrentCulture.Name);
            var rows = Parse(table, "\t");
            Assert.Equal(3, rows.Count);
            var header = rows[0];
            Assert.All(rows, row => Assert.Equal(header.Length, row.Length));
            Assert.Equal(columns, header.Count(cell => cell.StartsWith("N_", StringComparison.Ordinal)));
            Assert.Equal(names[0], rows[1][Array.IndexOf(header, "Experiment")]);
            Assert.Equal(names[1], rows[2][Array.IndexOf(header, "Experiment")]);
            var n = Array.IndexOf(header, "N_value");
            Assert.True(n >= 0);
            Assert.Equal("1.25", rows[1][n]);
            for (var i = 0; i < columns; i++) Assert.Equal("", rows[2][n + i]);
            if (style is UncertaintyDisplayStyle.StandardDeviation or UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval or UncertaintyDisplayStyle.Automatic)
                Assert.Equal("0.1", rows[1][Array.IndexOf(header, "N_sd")]);
            if (style is UncertaintyDisplayStyle.ConfidenceInterval or UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval or UncertaintyDisplayStyle.Automatic)
            {
                Assert.Equal("1", rows[1][Array.IndexOf(header, "N_ci_lower")]);
                Assert.Equal("1.6", rows[1][Array.IndexOf(header, "N_ci_upper")]);
            }
            Assert.NotEmpty(rows[2][Array.IndexOf(header, "Binding assessment")]);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Theory]
    [InlineData(UncertaintyDisplayStyle.StandardDeviation)]
    [InlineData(UncertaintyDisplayStyle.ConfidenceInterval)]
    [InlineData(UncertaintyDisplayStyle.None)]
    public void FixedValuesDoNotShiftFollowingParameters(UncertaintyDisplayStyle style)
    {
        var result = IndependentAssessmentTests.CreateIndependentResult(out var members);
        members[0].Parameters[ParameterType.Nvalue1] = new FloatWithError(1);
        members[0].Parameters[ParameterType.Enthalpy1] = new FloatWithError(-40000, 2000);
        var rows = Parse(Copy(style, () => Exporter.CopyToClipboard(result, EnergyUnit.KiloJoule, false)), "\t");
        Assert.All(rows, row => Assert.Equal(rows[0].Length, row.Length));
        Assert.Equal("1", rows[1][Array.IndexOf(rows[0], "N_value")]);
        Assert.Equal("-40", rows[1][Array.IndexOf(rows[0], "∆H (kJ/mol)_value")]);
        Assert.NotEmpty(rows[1][Array.IndexOf(rows[0], "Binding assessment")]);
    }

    [Fact]
    public void MissingUncertaintyRetainsTheValueAndEmptyUncertaintyCells()
    {
        var result = IndependentAssessmentTests.CreateIndependentResult(out var members);
        members[0].Parameters[ParameterType.Nvalue1] = new FloatWithError(1.5, double.NaN);
        var rows = Parse(Copy(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            () => Exporter.CopyToClipboard(result, EnergyUnit.Joule, false)), "\t");
        var n = Array.IndexOf(rows[0], "N_value");
        Assert.True(n >= 0);
        Assert.Equal("1.5", rows[1][n]);
        Assert.Equal(new[] { "", "", "" }, rows[1].Skip(n + 1).Take(3));
        Assert.Equal(rows[0].Length, rows[1].Length);
        Assert.NotEmpty(rows[1][Array.IndexOf(rows[0], "Binding assessment")]);
    }

    [Fact]
    public void ExplicitUnitsAndKelvinSurviveQuickCopy()
    {
        var result = IndependentAssessmentTests.CreateIndependentResult(out var members);
        members[0].Data.MeasuredTemperature = 25;
        members[0].Parameters[ParameterType.Enthalpy1] = new FloatWithError(-4184, 418.4);
        var rows = Parse(Copy(UncertaintyDisplayStyle.StandardDeviation,
            () => Exporter.CopyToClipboard(result, ConcentrationUnit.µM, EnergyUnit.Cal, true)), "\t");
        Assert.Equal("298.2", rows[1][Array.IndexOf(rows[0], "Temperature (K)")]);
        Assert.Contains("Kd (µM)_value", rows[0]);
        Assert.Equal("-1000", rows[1][Array.IndexOf(rows[0], "∆H (cal/mol)_value")]);
        Assert.Equal("100", rows[1][Array.IndexOf(rows[0], "∆H (cal/mol)_sd")]);
        var affinity = members[0].ReportParameters[ParameterType.Affinity1].Value;
        Assert.Equal((affinity * 1e6).ToString("G5", CultureInfo.InvariantCulture),
            rows[1][Array.IndexOf(rows[0], "Kd (µM)_value")]);
    }

    [Theory]
    [InlineData(AnalysisResultExportFileFormat.CSV, ",")]
    [InlineData(AnalysisResultExportFileFormat.TSV, "\t")]
    public void StructuredExportKeepsMissingAndDifferentParametersAligned(AnalysisResultExportFileFormat format, string delimiter)
    {
        var result = IndependentAssessmentTests.CreateIndependentResult(out var members);
        members[0].Data.Name = "sample, with\ttab\"quote\nand newline ±";
        members[0].Parameters[ParameterType.Nvalue1] = FloatWithError.NaN;
        var sequential = SequentialPersistenceTests.CreateGlobalResult(2).Item2;
        var table = AnalysisResultTableExporter.Build(new[] { result, sequential }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.AllRows,
            ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
            FileFormat = format,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
        });
        var rows = Parse(table, delimiter);
        Assert.All(rows, row => Assert.Equal(rows[0].Length, row.Length));
        Assert.Equal(members[0].Data.Name, rows[1][Array.IndexOf(rows[0], "Experiment")]);
        var kd2 = Array.FindIndex(rows[0], value => value.StartsWith("Kd2 (") && value.EndsWith("_value"));
        Assert.True(kd2 >= 0);
        Assert.Equal("", rows[1][kd2]);
        Assert.NotEqual("", rows.Last()[kd2]);
    }

    static string Copy(UncertaintyDisplayStyle style, Action action)
    {
        var previousStyle = AppSettings.UncertaintyDisplayStyle;
        var clipboard = new RecordingClipboard();
        PlatformServices.RegisterClipboardService(clipboard);
        try
        {
            AppSettings.UncertaintyDisplayStyle = style;
            action();
            return clipboard.Value;
        }
        finally
        {
            AppSettings.UncertaintyDisplayStyle = previousStyle;
            PlatformServices.RegisterClipboardService(null);
        }
    }

    static List<string[]> Parse(string table, string delimiter)
    {
        using var parser = new TextFieldParser(new StringReader(table));
        parser.SetDelimiters(delimiter);
        parser.TrimWhiteSpace = false;
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields());
        return rows;
    }

    sealed class RecordingClipboard : IClipboardService
    {
        public string Value { get; private set; }
        public void SetString(string value) => Value = value;
    }
}
