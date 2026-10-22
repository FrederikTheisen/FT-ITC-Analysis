using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class AdvancedAnalysisInterpretationTests
{
    [Theory]
    [InlineData(0.5, 0.2, 0.9)]
    [InlineData(-0.5, -0.9, -0.2)]
    [InlineData(0.0, -0.2, 0.4)]
    public async Task SavedProtonUptakeAndAsymmetricIntervalsSurviveAllOutputPaths(
        double center, double lower, double upper)
    {
        var result = await LoadAdvancedResult();
        var uptake = new FloatWithError(center, 0.1, lower, upper);
        var enthalpy = new FloatWithError(-40000, 500, -41200, -39400);
        var completed = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        result.ProtonationAnalysis.RestoreResult(enthalpy, uptake, 18, completed, ErrorEstimationMethod.None);

        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, result.Solution.Solutions.Select(item => item.Data).Distinct(), new[] { result });
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var reader = new StreamReader(archive.GetEntry("results/000000/result.json")!.Open()))
        {
            var json = await reader.ReadToEndAsync();
            Assert.Contains("\"protonationChange\"", json);
            Assert.Contains("\"bindingEnthalpy\"", json);
        }
        stream.Position = 0;
        var restored = (await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>().Single();
        foreach (var saved in new[] { result, restored })
        {
            Assert.Equal(center, saved.ProtonationAnalysis.ProtonationChange.Value);
            Assert.Equal(0.1, saved.ProtonationAnalysis.ProtonationChange.SD);
            Assert.Equal(lower, saved.ProtonationAnalysis.ProtonationChange.Lower);
            Assert.Equal(upper, saved.ProtonationAnalysis.ProtonationChange.Upper);
            Assert.Equal(-40000, saved.ProtonationAnalysis.BindingEnthalpy.Value);
            Assert.Equal(-41200, saved.ProtonationAnalysis.BindingEnthalpy.FloatWithError.Lower);
            Assert.Equal(-39400, saved.ProtonationAnalysis.BindingEnthalpy.FloatWithError.Upper);
            Assert.Equal(completed, saved.ProtonationAnalysis.CompletedAtUtc);

            var package = AnalysisInterpretationPackageBuilder.Build(CreateReport(saved), saved);
            var evidence = Assert.Single(package.Result.AdvancedAnalyses, item => item.Type == "protonation");
            Assert.Equal("buffer-ionization-enthalpy", evidence.XAxis);
            var value = Value(evidence, "protonation-change");
            Assert.Equal(center, value.Value);
            Assert.Equal(0.1, value.StandardDeviation);
            Assert.Equal(lower, value.Confidence95Lower);
            Assert.Equal(upper, value.Confidence95Upper);

            var options = new AnalysisReportOptions
            {
                EnergyUnitOverride = EnergyUnit.KiloJoule,
                UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            };
            options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(AnalysisReportAdvancedSectionKind.Protonation));
            var document = AnalysisReportBuilder.Build(saved, options);
            var section = Assert.Single(document.Sections, item => item.Title == "Protonation dependence");
            var plot = Assert.Single(section.Blocks.OfType<AnalysisReportPlotBlock>());
            Assert.Equal("Buffer ionization enthalpy (kJ/mol)", plot.XAxisTitle);
            var rows = section.Blocks.OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items).ToList();
            Assert.Equal(FormattableString.Invariant($"{center:G5} ± 0.1 [{lower:G5}, {upper:G5}]"),
                rows.Single(item => item.Label == "Net proton uptake").Value);
            Assert.Equal("-40.0 ± 0.5 [-41.2, -39.4] kJ/mol",
                rows.Single(item => item.Label == "Buffer-independent enthalpy").Value);
        }
        stream.Position = 0;
        var viewer = await new ViewerDocumentReader().ReadAsync(stream, "uptake.ftxtc", ViewerFileFormat.Ftxtc);
        var protonation = Assert.Single(viewer.AnalysisResults).AdvancedAnalyses.Protonation;
        Assert.Equal("Buffer ionization enthalpy (kJ/mol)", protonation.Plot.XAxisLabel);
        Assert.Equal(center, protonation.ProtonationChange.Value);
        Assert.Equal(0.1, protonation.ProtonationChange.Sd);
        Assert.Equal(lower, protonation.ProtonationChange.ConfidenceLower);
        Assert.Equal(upper, protonation.ProtonationChange.ConfidenceUpper);
        Assert.Equal(-40, protonation.BindingEnthalpyKilojoulesPerMole.Value);
        Assert.Equal(-41.2, protonation.BindingEnthalpyKilojoulesPerMole.ConfidenceLower);
        Assert.Equal(-39.4, protonation.BindingEnthalpyKilojoulesPerMole.ConfidenceUpper);
    }
}
