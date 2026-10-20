using System;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class ResultExporterTemperatureTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void UnitsPreserveEvaluationAndEveryOtherColumn(bool series, bool allRows, bool tsv)
    {
        var preferences = PreferencesState.FromSettings();
        var path = Path.GetTempFileName();
        try
        {
            PreferencesState.Defaults().ApplyToSettings();
            AppSettings.ReferenceTemperature = 27;
            var models = (series ? new[] { 20.0, 30.0 } : new[] { 20.0 }).Select(temperature =>
            {
                var model = InjectionProcessingMethodTests.FittedModel();
                model.Data.MeasuredTemperature = model.Data.TargetTemperature = temperature;
                return model;
            }).ToList();
            var global = new GlobalModel(models);
            foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
            var solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(m => m.Solution).ToList(),
                models[0].Solution.Convergence, reconstructBootstrap: false);
            global.Solution = solution;
            var result = new AnalysisResult(solution);
            var options = new AnalysisResultExportOptions
            {
                RowMode = allRows ? AnalysisResultExportRowMode.AllRows : AnalysisResultExportRowMode.Summary,
                FileFormat = tsv ? AnalysisResultExportFileFormat.TSV : AnalysisResultExportFileFormat.CSV,
                ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
            };
            var beforeParameters = models.SelectMany(m => m.Solution.Parameters.Values).ToArray();
            string[][] celsius = null;
            foreach (var kelvin in new[] { false, true, false })
            {
                options.UseKelvin = kelvin;
                var text = AnalysisResultTableExporter.Build(new[] { result }, options);
                AnalysisResultTableExporter.WriteToFile(path, new[] { result }, options);
                Assert.Equal(text, File.ReadAllText(path));
                var table = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                    .Select(row => row.Split(options.Delimiter)).ToArray();
                var column = Array.IndexOf(table[0], (allRows ? "Temperature" : "Evaluation temperature") + (kelvin ? " (K)" : " (°C)"));
                Assert.True(column >= 0);
                var expected = allRows && series ? (kelvin ? new[] { "293.2", "303.2" } : new[] { "20.0", "30.0" })
                    : new[] { series ? (kelvin ? "300.2" : "27.0") : (kelvin ? "293.2" : "20.0") };
                Assert.Equal(expected, table.Skip(1).Select(row => row[column]));
                celsius ??= table;
                Assert.Equal(celsius.Length, table.Length);
                for (var row = 0; row < table.Length; row++)
                    Assert.Equal(celsius[row].Where((_, i) => i != column), table[row].Where((_, i) => i != column));
                Assert.Equal(beforeParameters, models.SelectMany(m => m.Solution.Parameters.Values));
                Assert.Equal(series ? new[] { 20.0, 30.0 } : new[] { 20.0 }, models.Select(m => m.Solution.Temp));
                Assert.Same(solution, result.Solution);
                Assert.Equal(27, AppSettings.ReferenceTemperature);
            }
        }
        finally
        {
            File.Delete(path);
            preferences.ApplyToSettings();
        }
    }
}
