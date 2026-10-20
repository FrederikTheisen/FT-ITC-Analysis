using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class AnalysisResultStoichiometryExportTests
{
    static readonly (AnalysisModel first, bool firstCorrected, AnalysisModel second, string[] headers)[] Combinations =
    {
        (AnalysisModel.OneSetOfSites, false, AnalysisModel.OneSetOfSites,
            new[] { "N", "α", "N fixed", "Kd", "∆H", "-T∆S", "∆G" }),
        (AnalysisModel.TwoSetsOfSites, false, AnalysisModel.TwoSetsOfSites,
            new[] { "N1", "α", "N1 fixed", "Kd1", "∆H1", "-T∆S1", "∆G1", "N2", "N2 fixed", "Kd2", "∆H2", "-T∆S2", "∆G2" }),
        (AnalysisModel.OneSetOfSites, true, AnalysisModel.TwoSetsOfSites,
            new[] { "α", "N1 fixed", "Kd1", "∆H1", "-T∆S1", "∆G1", "N2 fixed", "Kd2", "∆H2", "-T∆S2", "∆G2" }),
        (AnalysisModel.OneSetOfSites, false, AnalysisModel.TwoSetsOfSites,
            new[] { "N1", "α", "N1 fixed", "Kd1", "∆H1", "-T∆S1", "∆G1", "N2 fixed", "Kd2", "∆H2", "-T∆S2", "∆G2" }),
        (AnalysisModel.TwoSetsOfSites, false, AnalysisModel.OneSetOfSites,
            new[] { "N1", "α", "N1 fixed", "Kd1", "∆H1", "-T∆S1", "∆G1", "N2", "Kd2", "∆H2", "-T∆S2", "∆G2" }),
        (AnalysisModel.CompetitiveBinding, false, AnalysisModel.CompetitiveBinding,
            new[] { "N", "α", "N fixed", "Kd_app", "Kd", "∆H", "-T∆S", "∆G" }),
    };

    public static IEnumerable<object[]> MixedLayouts()
    {
        for (var pair = 0; pair < Combinations.Length; pair++)
        foreach (var options in Cr019Fixtures.Layouts())
            yield return new object[] { pair, options.RowMode, options.FileFormat, options.ErrorStyle, options.UncertaintyDisplayStyle };
    }

    [Theory]
    [MemberData(nameof(MixedLayouts))]
    public void MixedColumnsKeepTheirMeaningAndOrder(int pair, AnalysisResultExportRowMode mode,
        AnalysisResultExportFileFormat format, AnalysisResultExportErrorStyle errors, UncertaintyDisplayStyle style)
    {
        var combination = Combinations[pair];
        var first = Cr019Fixtures.Create(combination.first, combination.firstCorrected, "first");
        var second = Cr019Fixtures.Create(combination.second, true, "second");
        var options = Options(mode, format, errors, style);
        var rows = Parse(AnalysisResultTableExporter.Build(new[] { first, second }, options), options.Delimiter);
        var reverse = Parse(AnalysisResultTableExporter.Build(new[] { second, first }, options), options.Delimiter);
        Assert.Equal(rows[0], reverse[0]);
        Assert.All(rows.Concat(reverse), row => Assert.Equal(rows[0].Length, row.Length));
        var loss = Array.IndexOf(rows[0], "Loss");
        Assert.Equal(combination.headers.SelectMany(header => ExpandedHeader(header, options)), rows[0].Skip(4).Take(loss - 4));
        foreach (var row in rows.Skip(1))
        {
            Assert.Equal(row, Assert.Single(reverse.Skip(1), candidate => candidate[0] == row[0]
                && (mode == AnalysisResultExportRowMode.Summary || candidate[1] == row[1])));
            var result = row[0] == "first" ? first : second;
            var corrected = row[0] == "second" || combination.firstCorrected;
            foreach (var header in combination.headers.Where(h => h.StartsWith("N") || h == "α"))
            {
                var column = Array.IndexOf(rows[0], header + (errors == AnalysisResultExportErrorStyle.SeparateColumns ? "_value" : ""));
                var expected = header switch
                {
                    "α" => corrected ? "0.73" : "",
                    "N fixed" or "N1 fixed" => corrected ? "2" : "",
                    "N2 fixed" => corrected && result.Model.ModelType == AnalysisModel.TwoSetsOfSites ? "0.5" : "",
                    "N2" => !corrected && result.Model.ModelType == AnalysisModel.TwoSetsOfSites ? "2.4" : "",
                    _ => !corrected ? "1.4" : "",
                };
                if (expected == "" || header.Contains("fixed")) Assert.Equal(expected, row[column]);
                else Assert.StartsWith(expected, row[column]);
                if (expected == "" && errors == AnalysisResultExportErrorStyle.SeparateColumns && !header.Contains("fixed"))
                    Assert.All(ExpandedHeader(header, options), field => Assert.Equal("", row[Array.IndexOf(rows[0], field)]));
            }
            if (mode == AnalysisResultExportRowMode.AllRows && errors == AnalysisResultExportErrorStyle.SeparateColumns
                && style is UncertaintyDisplayStyle.StandardDeviation or UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval or UncertaintyDisplayStyle.Automatic)
                Assert.Equal(corrected ? "0.03" : "", row[Array.IndexOf(rows[0], "α_sd")]);
        }
    }

    [Theory]
    [InlineData(AnalysisModel.OneSetOfSites)]
    [InlineData(AnalysisModel.TwoSetsOfSites)]
    [InlineData(AnalysisModel.CompetitiveBinding)]
    public void CorrectedExportsAndQuickCopyAddOnlyScalarFixedInputs(AnalysisModel type)
    {
        var result = Cr019Fixtures.Create(type, true, "corrected");
        var headers = type == AnalysisModel.TwoSetsOfSites ? Combinations[1].headers
            : type == AnalysisModel.CompetitiveBinding ? Combinations[5].headers : Combinations[0].headers;
        headers = headers.Where(header => header != "N" && header != "N1" && header != "N2").ToArray();
        foreach (var options in Cr019Fixtures.Layouts())
        {
            var rows = Parse(AnalysisResultTableExporter.Build(new[] { result }, options), options.Delimiter);
            var loss = Array.IndexOf(rows[0], "Loss");
            Assert.Equal(headers.SelectMany(header => ExpandedHeader(header, options)), rows[0].Skip(4).Take(loss - 4));
            Assert.All(rows, row => Assert.Equal(rows[0].Length, row.Length));
            var fixed1 = (type == AnalysisModel.TwoSetsOfSites ? "N1 fixed" : "N fixed")
                + (options.ErrorStyle == AnalysisResultExportErrorStyle.SeparateColumns ? "_value" : "");
            Assert.All(rows.Skip(1), row => Assert.Equal("2", row[Array.IndexOf(rows[0], fixed1)]));
        }
        foreach (var style in Enum.GetValues<UncertaintyDisplayStyle>())
        {
            var rows = Parse(Cr019Fixtures.Copy(result, style), '\t');
            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.Equal(rows[0].Length, row.Length));
            Assert.DoesNotContain("N_value", rows[0]);
            Assert.DoesNotContain("N1_value", rows[0]);
            Assert.DoesNotContain("N2_value", rows[0]);
            var fixed1 = type == AnalysisModel.TwoSetsOfSites ? "N1 fixed_value" : "N fixed_value";
            Assert.All(rows.Skip(1), row => Assert.Equal("2", row[Array.IndexOf(rows[0], fixed1)]));
            Assert.Equal(type == AnalysisModel.TwoSetsOfSites ? 2 : 1, rows[0].Count(h => h.Contains("fixed")));
            if (type == AnalysisModel.TwoSetsOfSites)
            {
                Assert.All(rows.Skip(1), row => Assert.Equal("0.5", row[Array.IndexOf(rows[0], "N2 fixed_value")]));
                Assert.All(result.Solution.Solutions, member => Assert.True(member.Parameters.ContainsKey(ParameterType.Nvalue2)));
                Assert.All(result.Solution.Solutions, member => Assert.False(member.ReportParameters.ContainsKey(ParameterType.Nvalue2)));
            }
        }
    }

    [Theory]
    [InlineData(AnalysisResultExportRowMode.Summary)]
    [InlineData(AnalysisResultExportRowMode.AllRows)]
    public void FixedInputsUseSavedNumbersAndLeaveMissingOptionsBlank(AnalysisResultExportRowMode mode)
    {
        var result = Cr019Fixtures.Create(AnalysisModel.TwoSetsOfSites, true, "corrected");
        foreach (var member in result.Solution.Solutions)
        {
            member.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue = 1.0 / 3;
            member.ModelOptions.Remove(AttributeKey.NumberOfSites2);
        }
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
            foreach (var errors in Enum.GetValues<AnalysisResultExportErrorStyle>())
            {
                var options = Options(mode, errors: errors);
                var rows = Parse(AnalysisResultTableExporter.Build(new[] { result }, options), '\t');
                Assert.Equal("da-DK", CultureInfo.CurrentCulture.Name);
                var suffix = errors == AnalysisResultExportErrorStyle.SeparateColumns ? "_value" : "";
                Assert.All(rows.Skip(1), row => Assert.Equal("0.33333", row[Array.IndexOf(rows[0], "N1 fixed" + suffix)]));
                Assert.All(rows.Skip(1), row => Assert.Equal("", row[Array.IndexOf(rows[0], "N2 fixed" + suffix)]));
                foreach (var member in result.Solution.Solutions) member.ModelOptions.Remove(AttributeKey.NumberOfSites1);
                rows = Parse(AnalysisResultTableExporter.Build(new[] { result }, options), '\t');
                Assert.All(rows.Skip(1), row => Assert.Equal("", row[Array.IndexOf(rows[0], "N1 fixed" + suffix)]));
                foreach (var member in result.Solution.Solutions)
                    member.ModelOptions[AttributeKey.NumberOfSites1] = ExperimentAttribute.Double(AttributeKey.NumberOfSites1, "Stoichiometry", 1.0 / 3);
            }
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Theory]
    [InlineData(AnalysisResultExportRowMode.Summary)]
    [InlineData(AnalysisResultExportRowMode.AllRows)]
    public void SuppressedFitInputsAreBlankAndCannotIntroduceColumns(AnalysisResultExportRowMode mode)
    {
        var ordinary = Cr019Fixtures.Create(AnalysisModel.OneSetOfSites, false, "ordinary");
        var corrected = Cr019Fixtures.Create(AnalysisModel.TwoSetsOfSites, true, "corrected");
        var suppressed = Cr019Fixtures.Create(AnalysisModel.TwoSetsOfSites, true, "suppressed");
        suppressed.SetMemberBindingAssessmentOverride(suppressed.Solution.Solutions[0].Guid, BindingAssessmentOutcome.NoBindingDetected);
        foreach (var errors in Enum.GetValues<AnalysisResultExportErrorStyle>())
        {
            var options = Options(mode, errors: errors);
            var rows = Parse(AnalysisResultTableExporter.Build(new[] { ordinary, corrected, suppressed }, options), '\t');
            var suppressedRow = Assert.Single(rows.Skip(1), row => row[0] == "suppressed"
                && (mode == AnalysisResultExportRowMode.Summary || row[1] == suppressed.Solution.Solutions[0].Data.Name));
            for (var index = 0; index < rows[0].Length; index++)
                if (rows[0][index].StartsWith("α") || rows[0][index].Contains("fixed"))
                    Assert.Equal("", suppressedRow[index]);
            if (mode == AnalysisResultExportRowMode.AllRows)
            {
                var allowedRow = Assert.Single(rows.Skip(1), row => row[0] == "suppressed" && row[1] == suppressed.Solution.Solutions[1].Data.Name);
                Assert.NotEqual("", allowedRow[Array.IndexOf(rows[0], errors == AnalysisResultExportErrorStyle.SeparateColumns ? "α_value" : "α")]);
            }
            suppressed.SetMemberBindingAssessmentOverride(suppressed.Solution.Solutions[1].Guid, BindingAssessmentOutcome.NoBindingDetected);
            rows = Parse(AnalysisResultTableExporter.Build(new[] { ordinary, suppressed }, options), '\t');
            Assert.DoesNotContain(rows[0], header => header.Contains("fixed") || header.StartsWith("α"));
            options.OutputPurpose = ResultOutputPurpose.Diagnostic;
            rows = Parse(AnalysisResultTableExporter.Build(new[] { ordinary, suppressed }, options), '\t');
            Assert.Contains(rows[0], header => header.Contains("fixed"));
            Assert.All(rows.Skip(1).Where(row => row[0] == "suppressed"), row =>
                Assert.Equal("2", row[Array.IndexOf(rows[0], errors == AnalysisResultExportErrorStyle.SeparateColumns ? "N1 fixed_value" : "N1 fixed")]));
            suppressed.UseAutomaticBindingAssessments();
            suppressed.SetMemberBindingAssessmentOverride(suppressed.Solution.Solutions[0].Guid, BindingAssessmentOutcome.NoBindingDetected);
        }
    }

    [Fact]
    public void OrdinaryTablesAndQuickCopyAreByteIdenticalToPreChangeOutput()
    {
        var one = Cr019Fixtures.Create(AnalysisModel.OneSetOfSites, false, "ordinary one");
        var two = Cr019Fixtures.Create(AnalysisModel.TwoSetsOfSites, false, "ordinary two");
        var competitive = Cr019Fixtures.Create(AnalysisModel.CompetitiveBinding, false, "ordinary competitive");
        // Pre-CR-019 UTF-8 baselines, joined with two LF characters. Normalize only
        // platform line endings so these byte-content checks also run on Windows.
        var cases = new[]
        {
            (new[] { one }, "3E9AE704B7BA550A909DA2CBC941298765BEAD97AFFF9206056798D83194DB87"),
            (new[] { two }, "4FEC3E36ACD5339DA4DE8D59CC64107FB6CB7C1603643C177BC012637E0C5013"),
            (new[] { competitive }, "8B8C4F7553A585A1BA54398ED7E34071595B949E6B7DF51CCAB497A9340E3EE8"),
            (new[] { one, two }, "E11F4EF710CCABD4A27FAEAF5C8418E85ACBFEEDFC06FC9CC76B59F511C0E0D4"),
            (new[] { two, one }, "BD68E523164E89D34448400B2D833A5A514FAA35E32D7D422DF0CFAC9AC37B85"),
        };
        foreach (var (results, expected) in cases)
            Assert.Equal(expected, Hash(Cr019Fixtures.Layouts().Select(options => AnalysisResultTableExporter.Build(results, options))));
        Assert.Equal("F892B68FEAB6D0402A6B3DAD388823DD122090500281C5AE008DAF8EC9C17230", Hash(Enum.GetValues<UncertaintyDisplayStyle>().Select(style => Cr019Fixtures.Copy(one, style))));
        Assert.Equal("05F519D287D1A1DFF89CE4F5CF910E08755C7CEDCEA28E58F6CC363F75292577", Hash(Enum.GetValues<UncertaintyDisplayStyle>().Select(style => Cr019Fixtures.Copy(two, style))));
        Assert.Equal("1949C51D0A5A859DF5150538949CD46AE71E6258D16224775BBF29CF3D8A3CEE", Hash(Enum.GetValues<UncertaintyDisplayStyle>().Select(style => Cr019Fixtures.Copy(competitive, style))));
    }

    [Fact]
    public void QuickCopyRetainsInvariantNumbersAndUserCommasAcrossCultures()
    {
        var result = Cr019Fixtures.Create(AnalysisModel.OneSetOfSites, false, "User text, 1,25");
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var expected = Enum.GetValues<UncertaintyDisplayStyle>()
                .ToDictionary(style => style, style => Cr019Fixtures.Copy(result, style));
            Assert.All(expected.Values, text => Assert.Contains("User text, 1,25", text));
            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                foreach (var style in expected.Keys)
                    Assert.Equal(expected[style], Cr019Fixtures.Copy(result, style));
                Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    static string Hash(IEnumerable<string> tables) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n\n", tables.Select(table => table.Replace(Environment.NewLine, "\n"))))));

    static AnalysisResultExportOptions Options(AnalysisResultExportRowMode mode,
        AnalysisResultExportFileFormat format = AnalysisResultExportFileFormat.TSV,
        AnalysisResultExportErrorStyle errors = AnalysisResultExportErrorStyle.SeparateColumns,
        UncertaintyDisplayStyle style = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval) => new()
        {
            RowMode = mode, FileFormat = format, ErrorStyle = errors, UncertaintyDisplayStyle = style,
            EnergyUnitOverride = EnergyUnit.KiloJoule, EnergyUnitFamily = EnergyUnitFamily.Joules,
            ConcentrationUnitOverride = ConcentrationUnit.µM,
        };

    static IEnumerable<string> ExpandedHeader(string header, AnalysisResultExportOptions options)
    {
        var label = header.StartsWith("Kd") ? header + " (µM)"
            : header.StartsWith("∆") || header.StartsWith("-T") ? header + " (kJ/mol)" : header;
        if (options.ErrorStyle == AnalysisResultExportErrorStyle.ValueWithError) return new[] { label };
        if (header.Contains("fixed")) return new[] { label + "_value" };
        var interval = options.RowMode == AnalysisResultExportRowMode.Summary ? "_interval" : "_ci";
        return options.UncertaintyDisplayStyle switch
        {
            UncertaintyDisplayStyle.None => new[] { label + "_value" },
            UncertaintyDisplayStyle.StandardDeviation => new[] { label + "_value", label + "_sd" },
            UncertaintyDisplayStyle.ConfidenceInterval => new[] { label + "_value", label + interval + "_lower", label + interval + "_upper" },
            _ => new[] { label + "_value", label + "_sd", label + interval + "_lower", label + interval + "_upper" },
        };
    }

    static List<string[]> Parse(string table, char delimiter)
    {
        using var parser = new TextFieldParser(new StringReader(table));
        parser.SetDelimiters(delimiter.ToString());
        parser.TrimWhiteSpace = false;
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields());
        return rows;
    }
}


internal static class Cr019Fixtures
{
    internal static AnalysisResult Create(AnalysisModel type, bool corrected, string name)
    {
        var models = new List<Model>();
        for (var member = 0; member < 2; member++)
        {
            var data = new ExperimentData(name + " member " + member + ".itc")
            {
                MeasuredTemperature = 25,
                CellConcentration = new FloatWithError(20e-6),
                SyringeConcentration = new FloatWithError(200e-6),
                CellVolume = 1.4e-3,
            };
            for (var i = 0; i < 6; i++)
            {
                var injection = new InjectionData(data, i, 2e-6, 4e-10, true)
                {
                    ActualCellConcentration = data.CellConcentration,
                    ActualTitrantConcentration = (i + 1) * 2e-6,
                    Ratio = (i + 1) * 0.1,
                };
                injection.SetPeakArea(new FloatWithError(-1e-6, 1e-8));
                data.Injections.Add(injection);
            }
            Model model = type switch
            {
                AnalysisModel.OneSetOfSites => new OneSetOfSites(data),
                AnalysisModel.TwoSetsOfSites => new TwoSetsOfSites(data),
                AnalysisModel.CompetitiveBinding => new CompetitiveBinding(data),
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
            model.InitializeParameters(data);
            model.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = corrected;
            model.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue = 2;
            if (type == AnalysisModel.TwoSetsOfSites)
                model.ModelOptions[AttributeKey.NumberOfSites2].DoubleValue = 0.5;
            foreach (var parameter in model.Parameters.Table.Values)
                parameter.Update(parameter.Key switch
                {
                    ParameterType.Nvalue1 => corrected ? 0.73 : 1.4,
                    ParameterType.Nvalue2 => 2.4,
                    ParameterType.Enthalpy1 => -40000,
                    ParameterType.Enthalpy2 => -20000,
                    ParameterType.Affinity1 => 6,
                    ParameterType.Affinity2 => 5,
                    _ => 0,
                });
            model.ApplyModelOptions();
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(
                new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }));
            model.Solution.Parameters[ParameterType.Nvalue1] = new FloatWithError(corrected ? 0.73 : 1.4, 0.03, corrected ? 0.65 : 1.3, corrected ? 0.82 : 1.6);
            if (type == AnalysisModel.TwoSetsOfSites && !corrected)
                model.Solution.Parameters[ParameterType.Nvalue2] = new FloatWithError(2.4, 0.04, 2.2, 2.7);
            models.Add(model);
        }
        var globalModel = new GlobalModel(models);
        foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { Termination = SolverTermination.Converged });
        var solution = new GlobalSolution(new GlobalSolver { Model = globalModel }, models.Select(m => m.Solution).ToList(), convergence, reconstructBootstrap: false);
        globalModel.Solution = solution;
        return new AnalysisResult(solution) { Name = name };
    }

    internal static IEnumerable<AnalysisResultExportOptions> Layouts()
    {
        foreach (var rows in Enum.GetValues<AnalysisResultExportRowMode>())
        foreach (var format in Enum.GetValues<AnalysisResultExportFileFormat>())
        foreach (var errors in Enum.GetValues<AnalysisResultExportErrorStyle>())
        foreach (var style in Enum.GetValues<UncertaintyDisplayStyle>())
            yield return new AnalysisResultExportOptions
            {
                RowMode = rows, FileFormat = format, ErrorStyle = errors,
                UncertaintyDisplayStyle = style, EnergyUnitOverride = EnergyUnit.KiloJoule,
                EnergyUnitFamily = EnergyUnitFamily.Joules, ConcentrationUnitOverride = ConcentrationUnit.µM,
            };
    }

    internal static string Copy(AnalysisResult result, UncertaintyDisplayStyle style)
    {
        var clipboard = new RecordingClipboard();
        var previousStyle = AppSettings.UncertaintyDisplayStyle;
        var previousClipboard = PlatformServices.ClipboardService;
        try
        {
            PlatformServices.RegisterClipboardService(clipboard);
            AppSettings.UncertaintyDisplayStyle = style;
            Exporter.CopyToClipboard(result, ConcentrationUnit.µM, EnergyUnit.KiloJoule, false);
            return clipboard.Value;
        }
        finally
        {
            AppSettings.UncertaintyDisplayStyle = previousStyle;
            PlatformServices.RegisterClipboardService(previousClipboard);
        }
    }
    sealed class RecordingClipboard : IClipboardService
    {
        public string Value { get; private set; }
        public void SetString(string value) => Value = value;
    }
}
