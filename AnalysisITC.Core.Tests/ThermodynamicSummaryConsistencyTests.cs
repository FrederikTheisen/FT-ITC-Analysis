using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class ThermodynamicSummaryConsistencyTests
{
    [Theory]
    [InlineData("bootstrap", 1)]
    [InlineData("bootstrap", 2)]
    [InlineData("profile", 1)]
    [InlineData("unavailable", 1)]
    [InlineData("local", 1)]
    public void ThermodynamicSummariesAgreeAcrossCacheDisplayReportAndExport(string uncertainty, int steps)
    {
        var solution = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.TemperatureDependent,
            affinity: uncertainty == "local" ? VariableConstraint.None : VariableConstraint.ThermodynamicallyLinked,
            modelType: steps == 2 ? AnalysisModel.TwoSetsOfSites : AnalysisModel.OneSetOfSites, steps: steps);
        if (uncertainty == "bootstrap")
            solution.RestoreGlobalBootstrapSolutions(new List<GlobalSolution> {
                LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.TemperatureDependent, gibbs: -24000,
                    modelType: steps == 2 ? AnalysisModel.TwoSetsOfSites : AnalysisModel.OneSetOfSites, steps: steps),
                LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.TemperatureDependent, gibbs: -27000,
                    modelType: steps == 2 ? AnalysisModel.TwoSetsOfSites : AnalysisModel.OneSetOfSites, steps: steps) });
        if (uncertainty is "profile" or "unavailable")
            solution.ProfileLikelihoodRun = LinkedThermodynamicUncertaintyTests.Run(
                LinkedThermodynamicUncertaintyTests.Coordinate(ParameterType.Gibbs1, -25000, 500, 700,
                    incomplete: uncertainty == "unavailable"),
                LinkedThermodynamicUncertaintyTests.Coordinate(ParameterType.Enthalpy1, -40000, 4000, 6000),
                LinkedThermodynamicUncertaintyTests.Coordinate(ParameterType.HeatCapacity1, 300, 20, 40));
        var result = new AnalysisResult(solution);
        var cached = new AnalysisResultAggregateSummaryCalculator(result);
        var uncached = new AnalysisResultAggregateSummaryCalculator(result, bypassPresentationCache: true);
        var defaultTemperature = AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);
        foreach (var temperature in new[] { defaultTemperature, 6.85, 46.85 })
        {
            var display = AnalysisResultParameterEvaluator.Evaluate(result, temperature, EnergyUnit.Joule,
                UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval);
            foreach (var slot in Enumerable.Range(1, steps).Select(ThermodynamicParameterSlots.ForStep))
            {
                var gibbs = uncached.EvaluateSummaryParameter(slot.Gibbs, temperature).Value;
                var kd = uncached.EvaluateSummaryParameter(slot.Affinity, temperature).Value;
                var rt = (temperature + 273.15) * Energy.R;
                Assert.Equal(Math.Exp(gibbs.Value / rt), kd.Value, 12);
                var expectedSd = kd.Value * gibbs.SD / rt;
                if (double.IsNaN(expectedSd)) Assert.True(double.IsNaN(kd.SD));
                else Assert.InRange(Math.Abs(expectedSd - kd.SD), 0, Math.Abs(expectedSd) * 1e-13);
                Assert.Equal(Math.Exp(gibbs.Lower / rt), kd.Lower);
                Assert.Equal(Math.Exp(gibbs.Upper / rt), kd.Upper);
                Assert.Contains(display.Rows, row => row.Value == kd.AsFormattedConcentration(withunit: true,
                    style: UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval));
                foreach (var key in new[] { slot.Enthalpy, slot.EntropyContribution, slot.Gibbs, slot.Affinity, slot.HeatCapacity })
                {
                    var expected = uncached.EvaluateSummaryParameter(key, temperature).Value;
                    Equal(expected, cached.EvaluateSummaryParameter(key, temperature).Value);
                    if (key != slot.Affinity && (key != slot.HeatCapacity || expected.Value != 0))
                        Assert.Contains(display.Rows, row => row.Value == new Energy(expected).ToFormattedString(
                            EnergyUnit.Joule, permole: true, perK: key == slot.HeatCapacity,
                            style: UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval));
                    if (temperature == defaultTemperature)
                        Equal(expected, AnalysisResultTableExporter.SummaryValue(result, key));
                }
            }
        }
        // Reports deliberately use dot decimals; compare like-for-like formatting.
        var previousCulture = CultureInfo.CurrentCulture;
        AnalysisResultParameterEvaluation defaultDisplay;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            defaultDisplay = AnalysisResultParameterEvaluator.Evaluate(result, defaultTemperature, EnergyUnit.Joule,
                UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        var report = AnalysisReportBuilder.Build(new[] { result }, new AnalysisReportOptions {
            EnergyUnitOverride = EnergyUnit.Joule,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval });
        var reportItems = report.Sections.SelectMany(s => s.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .SelectMany(block => block.Items).ToList();
        foreach (var row in defaultDisplay.Rows)
            Assert.True(reportItems.Any(item => item.Label == row.Label && item.Value == (string.IsNullOrWhiteSpace(row.Value) ? "Unavailable" : row.Value)),
                $"Display {row.Label}: '{row.Value}'; report: " + string.Join("; ", reportItems.Where(item => item.Label == row.Label).Select(item => item.Value)));
    }

    [Theory]
    [InlineData(AnalysisResultExportFileFormat.CSV)]
    [InlineData(AnalysisResultExportFileFormat.TSV)]
    public void SummaryExportPropagatesKdAndQuickCopyKeepsIndividualRows(AnalysisResultExportFileFormat format)
    {
        var solution = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll);
        solution.RestoreGlobalBootstrapSolutions(new List<GlobalSolution> {
            LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll, gibbs: -24000),
            LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll, gibbs: -27000) });
        var result = new AnalysisResult(solution);
        var temperature = AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);
        var rt = (temperature + 273.15) * Energy.R;
        // Shared H gives G(T) = T/300 * G(300) + (1-T/300) * H.
        var g = (temperature + 273.15) / 300 * -25000 + (1 - (temperature + 273.15) / 300) * -40000;
        var center = Math.Exp(g / rt);
        var sd = center * Math.Sqrt(2500000) / (300 * Energy.R);
        var options = new AnalysisResultExportOptions {
            RowMode = AnalysisResultExportRowMode.Summary, FileFormat = format,
            ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            ConcentrationUnitOverride = ConcentrationUnit.M };
        var table = AnalysisResultTableExporter.Build(new[] { result }, options);
        var delimiter = format == AnalysisResultExportFileFormat.CSV ? ',' : '\t';
        Assert.Contains(center.ToString("G5", CultureInfo.InvariantCulture) + delimiter
            + sd.ToString("G5", CultureInfo.InvariantCulture) + delimiter, table);
        // Quick-copy deliberately contains individual rows only. Configured summary
        // copying in both desktops copies the table builder output checked above.
        var clipboard = new RecordingClipboard();
        var oldStyle = AppSettings.UncertaintyDisplayStyle;
        PlatformServices.RegisterClipboardService(clipboard);
        try
        {
            AppSettings.UncertaintyDisplayStyle = options.UncertaintyDisplayStyle;
            Exporter.CopyToClipboard(result, ConcentrationUnit.M, EnergyUnit.Joule, false);
            options.RowMode = AnalysisResultExportRowMode.AllRows;
            options.FileFormat = AnalysisResultExportFileFormat.TSV;
            options.EnergyUnitOverride = EnergyUnit.Joule;
            Assert.Equal(AnalysisResultTableExporter.Build(new[] { result }, options), clipboard.Value);
        }
        finally
        {
            AppSettings.UncertaintyDisplayStyle = oldStyle;
            PlatformServices.RegisterClipboardService(null);
        }
    }

    static void Equal(FloatWithError expected, FloatWithError actual)
    {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.SD, actual.SD);
        Assert.Equal(expected.Lower, actual.Lower);
        Assert.Equal(expected.Upper, actual.Upper);
    }

    sealed class RecordingClipboard : IClipboardService
    {
        public string Value { get; private set; }
        public void SetString(string value) => Value = value;
    }
}
