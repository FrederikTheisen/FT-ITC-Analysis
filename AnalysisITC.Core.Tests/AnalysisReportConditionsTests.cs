using System;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class AnalysisReportConditionsTests : IDisposable
{
    readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;
    readonly NumberPrecision originalPrecision = AppSettings.NumberPrecision;

    public AnalysisReportConditionsTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        AppSettings.NumberPrecision = NumberPrecision.Standard;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = originalCulture;
        AppSettings.NumberPrecision = originalPrecision;
    }

    [Theory]
    [InlineData(ResultOutputPurpose.Standard, false)]
    [InlineData(ResultOutputPurpose.Diagnostic, false)]
    [InlineData(ResultOutputPurpose.Standard, true)]
    public void SubtitleShowsOrderedNominalConditionsInEveryReportMode(ResultOutputPurpose purpose, bool trace)
    {
        var result = Result();
        var data = result.Solution.Solutions[0].Data;
        data.CellConcentration = new FloatWithError(10e-6, 1e-6);
        data.SyringeConcentration = new FloatWithError(100e-6, 10e-6);
        data.TargetTemperature = 25;
        data.MeasuredTemperature = 24.85;
        data.Attributes.Clear();
        data.Attributes.Add(Condition(AttributeKey.Salt, (int)Salt.NaCl, .150));
        data.Attributes.Add(Condition(AttributeKey.Buffer, (int)Buffer.Hepes, .050, 7.4));
        data.Attributes.Add(ExperimentAttribute.Concentration(AttributeKey.PreboundLigandConc, "", new(5e-6, 1e-6)));
        data.Attributes.Add(Condition(AttributeKey.Salt, (int)Salt.KCl, .010));
        data.Attributes.Add(Condition(AttributeKey.Buffer, (int)Buffer.Tris, .010, 8));
        data.Attributes.Add(ExperimentAttribute.Parameter(AttributeKey.IonicStrength, "", new(.200, .020)));
        data.Attributes.Add(ExperimentAttribute.Species(ExperimentSpeciesLocation.Cell, "Protein name"));

        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
            { OutputPurpose = purpose, ExtraTraceability = trace });
        var chapter = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment);
        Assert.Equal("100 µM ▸ 10 µM | 25 °C | 50 mM Hepes pH 7.4"
            + " | 10 mM Tris pH 8 | 150 mM NaCl | 10 mM KCl | [competitor] = 5 µM | Ionic strength = 200 mM",
            chapter.Subtitle);
        var details = chapter.Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains("±", details.Items.Single(item => item.Label == "Cell concentration").Value);
        Assert.Contains("24.85", details.Items.Single(item => item.Label == "Temperature").Value);
        Assert.Contains("target 25.00 °C", details.Items.Single(item => item.Label == "Temperature").Value);
    }

    [Theory]
    [InlineData(25, false, "25 °C")]
    [InlineData(25.1, false, "25.1 °C")]
    [InlineData(25.12, false, "25.12 °C")]
    [InlineData(0, false, "0 °C")]
    [InlineData(-5, false, "-5 °C")]
    [InlineData(25, true, "298.15 K")]
    [InlineData(26.85, true, "300 K")]
    [InlineData(26.95, true, "300.1 K")]
    public void SubtitleUsesTargetTemperatureAndReportUnit(double target, bool useKelvin, string expected)
    {
        var result = Result();
        var data = result.Solution.Solutions[0].Data;
        data.TargetTemperature = target;
        data.MeasuredTemperature = 30;
        Assert.Equal(expected, ExperimentChapter(result, new AnalysisReportOptions { UseKelvin = useKelvin })
            .Subtitle.Split(" | ")[1]);
    }

    [Fact]
    public void MissingConditionsAreOmittedAndZeroConcentrationsArePreserved()
    {
        var result = Result();
        var data = result.Solution.Solutions[0].Data;
        data.Attributes.Clear();
        data.CellConcentration = new(0);
        data.SyringeConcentration = new(0);
        data.TargetTemperature = double.NaN;
        data.Attributes.Add(Condition(AttributeKey.Buffer, (int)Buffer.Hepes, double.NaN));
        data.Attributes.Add(Condition(AttributeKey.Salt, (int)Salt.NaCl, double.NaN));
        data.Attributes.Add(ExperimentAttribute.Concentration(AttributeKey.PreboundLigandConc, "", new(-1)));
        data.Attributes.Add(ExperimentAttribute.Parameter(AttributeKey.IonicStrength, "", new(double.PositiveInfinity)));
        Assert.Equal("0 µM ▸ 0 µM", ExperimentChapter(result).Subtitle);
        data.CellConcentration = new(double.NaN);
        data.SyringeConcentration = new(double.PositiveInfinity);
        Assert.Empty(ExperimentChapter(result).Subtitle);
    }

    [Theory]
    [InlineData(100e-6, 10e-6, "100 µM ▸ 10 µM | 25 °C")]
    [InlineData(100e-6, double.NaN, "100 µM | 25 °C")]
    [InlineData(double.NaN, 10e-6, "10 µM | 25 °C")]
    [InlineData(double.PositiveInfinity, double.NaN, "25 °C")]
    [InlineData(-1, 10e-6, "10 µM | 25 °C")]
    [InlineData(100e-6, 2e-3, "100 µM ▸ 2 mM | 25 °C")]
    [InlineData(0, 10e-6, "0 µM ▸ 10 µM | 25 °C")]
    public void ConcentrationPairUsesSyringeThenCellAndOmitsMissingValues(double syringe, double cell, string expected)
    {
        var result = Result();
        var data = result.Solution.Solutions[0].Data;
        data.Attributes.Clear();
        data.SyringeConcentration = new(syringe);
        data.CellConcentration = new(cell);
        data.TargetTemperature = 25;
        Assert.Equal(expected, ExperimentChapter(result).Subtitle);
    }

    [Fact]
    public void IonicStrengthIsOnlyDisplayedWhenExplicitlyRecorded()
    {
        var result = Result();
        var data = result.Solution.Solutions[0].Data;
        data.Attributes.Clear();
        data.Attributes.Add(Condition(AttributeKey.Salt, (int)Salt.NaCl, .150));
        Assert.DoesNotContain("Ionic strength", ExperimentChapter(result).Subtitle);
        // A recorded value deliberately differs from what the salt would contribute.
        data.Attributes.Add(ExperimentAttribute.Parameter(AttributeKey.IonicStrength, "", new(.123)));
        Assert.EndsWith("Ionic strength = 123 mM", ExperimentChapter(result).Subtitle);
    }

    [Fact]
    public void RepeatedAndSupportingExperimentHeadingsRetainSubtitles()
    {
        var first = Result();
        var second = Result();
        second.Solution.Solutions[0].Data.SetID(first.Solution.Solutions[0].Data.UniqueID);
        second.Solution.Solutions[0].Data.CellConcentration = new(20e-6);
        second.Solution.Solutions[0].Data.SyringeConcentration = new(100e-6);
        var supporting = new ExperimentData("Supporting blank") { CellConcentration = new(0),
            SyringeConcentration = new(100e-6), TargetTemperature = 25 };
        supporting.SetID("supporting-conditions");
        var report = new AnalysisReport();
        report.SetResultIds(new[] { first.UniqueID, second.UniqueID });
        report.SetSupportingExperimentIds(new[] { supporting.UniqueID });
        var document = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : id == second.UniqueID ? second : null,
            id => id == supporting.UniqueID ? supporting : null);
        var chapters = document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment).ToList();
        Assert.Equal(2, chapters.Count);
        Assert.All(chapters, chapter => Assert.NotEmpty(chapter.Subtitle));
        Assert.StartsWith("100 µM ▸ 20 µM", chapters[1].Subtitle);
        Assert.Contains(chapters[1].Blocks, block => block.Title == "Experiment details — condensed");
        var heading = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportHeadingBlock>().Single();
        Assert.Equal("100 µM ▸ 0 µM | 25 °C", heading.Subtitle);
    }

    [Fact]
    public void WrappedHeadingAndSubtitleStayTogetherAtPageBoundary()
    {
        var document = new AnalysisReportDocument { Title = "Conditions layout" };
        document.PageSettings.HeightCentimeters = 10;
        var section = new AnalysisReportSection(AnalysisReportSectionKind.SupportingData, "supporting", "Supporting",
            AnalysisReportLayoutPolicy.None);
        section.Add(new AnalysisReportTextBlock("", string.Join("\n", Enumerable.Repeat("Filler", 5)),
            AnalysisReportLayoutPolicy.KeepTogether));
        var heading = new AnalysisReportHeadingBlock(string.Join(" ", Enumerable.Repeat("Long experiment name", 8)), 2,
            string.Join(" | ", Enumerable.Repeat("50 mM Hepes pH 7.4", 12)));
        section.Add(heading);
        document.AddSection(section);
        var plan = AnalysisReportLayoutEngine.Paginate(document, new TestMeasurer());
        var page = plan.Pages.Single(item => item.Fragments.Any(fragment => fragment.Block == heading));
        Assert.True(page.PageNumber > 1);
        var fragment = page.Fragments.Single(item => item.Block == heading);
        var layout = fragment.HeadingLayout;
        Assert.True(layout.TitleLines.Count > 1);
        Assert.True(layout.SubtitleLines.Count > 1);
        Assert.Equal(3, layout.SubtitleBounds.Y - layout.TitleBounds.Bottom);
        Assert.Equal(layout.Height, fragment.Bounds.Height);
        Assert.Equal(11, AnalysisReportHeadingLayout.SubtitleStyle.FontSize);
        Assert.True(AnalysisReportHeadingLayout.SubtitleStyle.Light);
        Assert.All(layout.SubtitleLines, line => Assert.True(new TestMeasurer()
            .Measure(line, AnalysisReportHeadingLayout.SubtitleStyle).Width <= fragment.Bounds.Width));
        Assert.Equal(heading.Subtitle, string.Join(" ", layout.SubtitleLines));
        Assert.True(fragment.Bounds.Bottom <= page.Height - plan.MarginBottom - 18);
    }

    static AnalysisReportSection ExperimentChapter(AnalysisResult result, AnalysisReportOptions options = null) =>
        AnalysisReportBuilder.Build(result, options).Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment);

    static ExperimentAttribute Condition(AttributeKey key, int kind, double concentration, double pH = 7.4)
    {
        var attribute = ExperimentAttribute.Parameter(key, "", new(concentration, concentration * .1));
        attribute.IntValue = kind;
        attribute.DoubleValue = pH;
        return attribute;
    }

    static AnalysisResult Result()
    {
        var model = InjectionProcessingMethodTests.FittedModel();
        model.Data.SetID(Guid.NewGuid().ToString("N"));
        var global = new GlobalModel(new[] { model }.ToList()) { Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None } };
        global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, new[] { model.Solution }.ToList(),
            model.Solution.Convergence, reconstructBootstrap: false);
        global.Solution = solution;
        var result = new AnalysisResult(solution) { Name = "Conditions fixture" };
        result.SetID(Guid.NewGuid().ToString("N"));
        return result;
    }

    sealed class TestMeasurer : IAnalysisReportTextMeasurer
    {
        public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style) =>
            new(text.Length * style.FontSize * .53, style.FontSize);
    }
}
