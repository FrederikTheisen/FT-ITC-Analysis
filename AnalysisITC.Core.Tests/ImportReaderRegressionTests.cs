using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(FileTypeFixtureCollectionDefinition.Name)]
public sealed class ImportReaderRegressionTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), $"ft-itc-reader-{Guid.NewGuid():N}");
    readonly ValidationPrompt validationPrompt = new();

    public ImportReaderRegressionTests()
    {
        Directory.CreateDirectory(directory);
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(new JoulePrompt());
        PlatformServices.RegisterDataValidationPromptService(validationPrompt);
    }

    public void Dispose()
    {
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
        PlatformServices.RegisterDataValidationPromptService(null);
        Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData('\t')]
    [InlineData(';')]
    [InlineData(',')]
    public void DelimitedImportPreservesEmptyFieldsAndTrimsIndividualValues(char separator)
    {
        var path = WriteDelimited(separator,
            "Xt; DH ; INJV ; Mt ; Xmt ;Note",
            "; 1e-6 ; 2 ; 0.05 ; 0.1 ;",
            "; 2e-6 ; 3 ; 0.0495 ; 0.2 ;");

        var experiment = ReadIntegrated(path);

        Assert.Equal(2, experiment.Injections.Count);
        Assert.Equal(2e-6, experiment.Injections[0].Volume, 12);
        Assert.Equal(1e-6, experiment.Injections[0].RawPeakArea.Value, 12);
        Assert.Equal(3e-6, experiment.Injections[1].Volume, 12);
        Assert.Equal(2e-6, experiment.Injections[1].RawPeakArea.Value, 12);
        Assert.False(experiment.Injections[0].Include);
        Assert.True(experiment.Injections[1].Include);
        Assert.True(double.IsNaN(experiment.SyringeConcentration.Value));
        Assert.Equal(0.05e-3, experiment.CellConcentration.Value, 12);
    }

    [Theory]
    [InlineData('\t')]
    [InlineData(';')]
    [InlineData(',')]
    public void DelimitedImportRejectsMissingLeadingHeatWithoutShiftingColumns(char separator)
    {
        var path = WriteDelimited(separator, "DH;INJV;Xt;Mt", ";2;0.05;0.1");

        var error = Assert.Throws<FormatException>(() => ReadIntegrated(path));

        Assert.Contains("Invalid or missing DH value on line 2", error.Message);
    }

    [Theory]
    [InlineData('\t')]
    [InlineData(';')]
    [InlineData(',')]
    public void DelimitedImportRejectsMissingTrailingVolume(char separator)
    {
        var path = WriteDelimited(separator, "DH;INJV", "1e-6;");

        var error = Assert.Throws<FormatException>(() => ReadIntegrated(path));

        Assert.Contains("no corresponding INJV value", error.Message);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    public void PeaqFixtureDateIsIndependentOfCurrentCulture(string locale)
    {
        var experiment = WithCulture(locale, () => Assert.Single(PEAQReader.ReadFile(Fixture("TpxWT_CtFT(1).apj"))));

        Assert.Equal(new DateTime(2023, 6, 26, 12, 9, 22), experiment.Date);
        Assert.Equal(DateTimeKind.Unspecified, experiment.Date.Kind);
        Assert.Equal(ExperimentDateSource.DataFile, experiment.DateSource);
    }

    [Theory]
    [InlineData("en-US", "06/07/2023 12:09:22", 7)]
    [InlineData("da-DK", "06/07/2023 12:09:22", 7)]
    [InlineData("de-DE", "06/07/2023 12:09:22", 7)]
    [InlineData("en-US", "  06/26/2023   12:09:22  ", 26)]
    [InlineData("da-DK", "  06/26/2023   12:09:22  ", 26)]
    [InlineData("de-DE", "  06/26/2023   12:09:22  ", 26)]
    public void PeaqUsesMonthDayOrderAndAllowsWhitespace(string locale, string dateText, int day)
    {
        var path = WritePeaq(dateText);

        var experiment = WithCulture(locale, () => Assert.Single(PEAQReader.ReadFile(path)));

        Assert.Equal(new DateTime(2023, 6, day, 12, 9, 22), experiment.Date);
        Assert.Equal(DateTimeKind.Unspecified, experiment.Date.Kind);
        Assert.Equal(ExperimentDateSource.DataFile, experiment.DateSource);
    }

    [Theory]
    [InlineData("en-US", null)]
    [InlineData("da-DK", null)]
    [InlineData("de-DE", null)]
    [InlineData("en-US", "not a date")]
    [InlineData("da-DK", "not a date")]
    [InlineData("de-DE", "not a date")]
    [InlineData("en-US", "  ")]
    [InlineData("da-DK", "  ")]
    [InlineData("de-DE", "  ")]
    public void PeaqMissingOrInvalidDateRetainsFilesystemFallback(string locale, string dateText)
    {
        var path = WritePeaq(dateText);
        var expected = File.GetCreationTime(path);

        var experiment = WithCulture(locale, () => Assert.Single(PEAQReader.ReadFile(path)));

        Assert.Equal(expected, experiment.Date);
        Assert.Equal(expected.Kind, experiment.Date.Kind);
        Assert.Equal(ExperimentDateSource.FileSystem, experiment.DateSource);
    }

    [Fact]
    public void PeaqImportsEveryExperimentInFileOrder()
    {
        var path = WritePeaqXml(xml =>
        {
            var first = xml.Root.Element("Experiments").Element("Experiment");
            var second = new XElement(first);
            second.Element("Name").Value = " ";
            first.AddAfterSelf(second);
        });

        var experiments = PEAQReader.ReadFile(path);

        Assert.Equal(2, experiments.Length);
        Assert.Equal("MP08nr1", experiments[0].Name);
        Assert.Equal("experiment 2", experiments[1].Name);
        Assert.All(experiments, experiment => Assert.Equal(20, experiment.Injections.Count));
        Assert.All(experiments, experiment => Assert.Equal("experiment.apj", experiment.FileName));
    }

    [Fact]
    public void PeaqSkipsUnreadableExperimentAndKeepsTheRest()
    {
        var path = WritePeaqXml(xml =>
        {
            var first = xml.Root.Element("Experiments").Element("Experiment");
            var broken = new XElement(first);
            broken.Element("Name").Value = "Broken run";
            broken.Element("DataPoints").RemoveNodes();
            var third = new XElement(first);
            third.Element("Name").Value = "Third run";
            first.AddAfterSelf(broken, third);
        });

        var experiments = PEAQReader.ReadFile(path);

        Assert.Equal(new[] { "MP08nr1", "Third run" }, experiments.Select(experiment => experiment.Name));
    }

    [Fact]
    public void PeaqWithNoReadableExperimentFails()
    {
        var path = WritePeaqXml(xml =>
        {
            var first = xml.Root.Element("Experiments").Element("Experiment");
            first.Element("DataPoints").RemoveNodes();
            first.AddAfterSelf(new XElement(first));
        });

        Assert.Throws<FormatException>(() => PEAQReader.ReadFile(path));
    }

    [Fact]
    public void PeaqWithoutMethodUsesRecordedInjectionSpacing()
    {
        var path = WritePeaqXml(xml => xml.Root.Element("Experiments").Element("Experiment").Element("Method").Remove());

        var experiment = Assert.Single(PEAQReader.ReadFile(path));

        // Fixture StartTime values 120, 300 … 2280, 2465 … 3545; last data point T = 3725.
        Assert.Equal(20, experiment.Injections.Count);
        Assert.Equal(180f, experiment.Injections[0].Delay);
        Assert.Equal(185f, experiment.Injections[12].Delay);
        Assert.Equal(180f, experiment.Injections[19].Delay);
    }

    [Fact]
    public void PeaqWithMethodKeepsPlannedInjectionSpacing()
    {
        var experiment = Assert.Single(PEAQReader.ReadFile(Fixture("TpxWT_CtFT(1).apj")));

        // The recorded gap after injection 12 is 185 s; the Method plans 180 s.
        Assert.Equal(180f, experiment.Injections[12].Delay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NOT_A_KNOWN_CELL")]
    public void PeaqUnknownCellIdLeavesInstrumentUnknown(string cellId)
    {
        // The fixture cell volume (204.7 µL) would otherwise be inferred as an ITC200.
        var path = WritePeaqXml(xml =>
        {
            var element = xml.Root.Element("Experiments").Element("Experiment").Element("CellID");
            if (cellId == null) element.Remove();
            else element.Value = cellId;
        });

        var experiment = Assert.Single(PEAQReader.ReadFile(path));

        Assert.Equal(20, experiment.Injections.Count);
        Assert.Equal(ITCInstrument.Unknown, experiment.Instrument);
    }

    [Theory]
    [InlineData(0.199e-3, ITCInstrument.TAInstrumentsAffinityITCLowVolume)] // A known TA source must not become MicroCal.
    [InlineData(0.190e-3, ITCInstrument.TAInstrumentsAffinityITCLowVolume)]
    [InlineData(0.950e-3, ITCInstrument.TAInstrumentsNanoITCStandard)]
    public void TaCellVolumeOnlySelectsTaInstruments(double cellVolume, ITCInstrument expected)
    {
        Assert.Equal(expected, ITCInstrumentAttribute.ResolveTAInstrument(cellVolume));
    }

    [Fact]
    public void NanoItcFixturesResolveToTaInstruments()
    {
        var paths = Directory.GetFiles(Path.GetDirectoryName(Fixture("x")), "*.nitc");
        Assert.NotEmpty(paths);

        foreach (var path in paths)
        {
            var experiment = NanoItcReader.ReadPath(path);
            Assert.Equal(ITCInstrument.TAInstrumentsAffinityITCLowVolume, experiment.Instrument);
        }
    }

    [Fact]
    public void PeaqKnownCellIdResolvesInstrument()
    {
        var experiment = Assert.Single(PEAQReader.ReadFile(Fixture("TpxWT_CtFT(1).apj")));

        Assert.Equal(ITCInstrument.MicroCalITC200, experiment.Instrument);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void UppercaseExtensionsAndPeaqFeedbackAreIndependentOfCulture(string locale)
    {
        var path = WritePeaqXml(xml => xml.Root.Element("Experiments").Element("Experiment").Element("Method").Element("Feedback").Value = "HIGH");

        var experiment = WithCulture(locale, () => Assert.Single(PEAQReader.ReadFile(path)));
        var formats = WithCulture(locale, () => new[] { DataReader.GetFormat("RUN.ITC"), DataReader.GetFormat("RUN.NITC"), DataReader.GetFormat("RUN.APJ") });

        Assert.Equal(FeedbackMode.High, experiment.FeedBackMode);
        Assert.Equal(new[] { ITCDataFormat.ITC200, ITCDataFormat.NanoITC, ITCDataFormat.PEAQITCProject }, formats);
    }

    [Theory]
    [InlineData("Xt", "n/a")]
    [InlineData("Mt", "#N/A")]
    [InlineData("Xmt", "abc")]
    [InlineData("Mt", "Infinity")]
    public void DelimitedImportRejectsMalformedConcentration(string column, string token)
    {
        var values = new Dictionary<string, string> { ["Xt"] = "0.05", ["Mt"] = "0.1", ["Xmt"] = "0.5" };
        values[column] = token;
        var path = WriteDelimited(';', "DH;INJV;Xt;Mt;Xmt", $"1e-6;2;{values["Xt"]};{values["Mt"]};{values["Xmt"]}");

        var error = Assert.Throws<FormatException>(() => ReadIntegrated(path));

        Assert.Contains($"Invalid {column} value on line 2", error.Message);
    }

    [Fact]
    public void DelimitedImportKeepsBlankDashAndZeroConcentrations()
    {
        var path = WriteDelimited(';', "DH;INJV;Xt;Mt;Xmt", "1e-6;2;;--;0", "2e-6;2;0;0.1;0");

        var experiment = ReadIntegrated(path);

        Assert.Equal(2, experiment.Injections.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaFinalDelayUsesRemainingTraceAndIntegrationUsesRecordedScope(bool multipleInjections)
    {
        var path = WriteTa(170, multipleInjections ? new[] { 20, 70 } : new[] { 70 });
        var experiment = TAFileReader.ReadPath(path);
        var last = experiment.Injections.Last();

        Assert.Equal(multipleInjections ? 2 : 1, experiment.Injections.Count);
        Assert.Equal(multipleInjections ? new[] { 0, 1 } : new[] { 0 }, experiment.Injections.Select(injection => injection.ID));
        Assert.False(experiment.Injections[0].Include);
        Assert.Equal(multipleInjections, last.Include);
        if (multipleInjections)
        {
            Assert.Equal(50f, experiment.Injections[0].Delay);
            Assert.Equal(40f, experiment.Injections[0].IntegrationLength);
        }
        Assert.Equal(70f, last.Time);
        Assert.Equal(100f, last.Delay);
        Assert.Equal(80f, last.IntegrationLength);
        Assert.Equal(150f, last.IntegrationEndTime);

        // Treat the controlled 1 µcal/s trace as already baseline corrected.
        // Independently: 1 µcal/s = 4.184 µW; Q = power × 80 s = 334.72 µJ.
        experiment.BaseLineCorrectedDataPoints = experiment.DataPoints.ToList();
        last.Integrate();
        Assert.InRange(Math.Abs(last.RawPeakArea.Value - 334.72e-6), 0, 1e-10);

        last.SetIntegrationLengthByTime(1000);
        Assert.Equal(100f, last.IntegrationLength);
        Assert.Equal(170f, last.IntegrationEndTime);
        last.Integrate();
        Assert.InRange(Math.Abs(last.RawPeakArea.Value - 418.4e-6), 0, 1e-10);
    }

    [Fact]
    public void TaFixtureFinalDelayReachesRecordedTraceEnd()
    {
        var experiment = TAFileReader.ReadPath(Fixture("16102024_Rocu_3mM_Sug_0_3mM_PBS_2.ta"));
        var last = experiment.Injections.Last();

        Assert.Equal(18, experiment.Injections.Count);
        Assert.Equal(2936f, last.Time);
        Assert.Equal(3090f, experiment.DataPoints.Last().Time);
        Assert.Equal(154f, last.Delay);
        Assert.Equal(123.2f, last.IntegrationLength, 4);
        last.SetIntegrationLengthByTime(154);
        Assert.Equal(3090f, last.IntegrationEndTime);
    }

    [Theory]
    [InlineData(DataValidationPromptAction.Keep, true)]
    [InlineData(DataValidationPromptAction.Discard, false)]
    public void TaMarkerAtTraceEndUsesStandardRawImportWarning(DataValidationPromptAction action, bool accepted)
    {
        var experiment = TAFileReader.ReadPath(WriteTa(70, new[] { 70 }));
        var injection = Assert.Single(experiment.Injections);
        validationPrompt.Action = action;

        Assert.Equal(70f, injection.Time);
        Assert.Equal(0f, injection.Delay);
        Assert.Equal(0f, injection.IntegrationLength);
        Assert.Equal(accepted, ImportValidator.ValidateData(experiment, isRawImport: true));
        Assert.Same(injection, Assert.Single(experiment.Injections));
        Assert.Equal(0, injection.ID);
        var warning = Assert.Single(validationPrompt.Calls);
        Assert.Contains("All injections appear to occur at the very end", warning.Message);
        Assert.True(warning.AllowKeep);
        Assert.False(warning.CanFix);
    }

    string WriteDelimited(char separator, params string[] lines)
    {
        var path = Path.Combine(directory, "heats.dat");
        File.WriteAllLines(path, lines.Select(line => line.Replace(';', separator)));
        return path;
    }

    static ExperimentData ReadIntegrated(string path) =>
        IntegratedHeatReader.ReadFile(path, true, DilutionMethod.MicroCal, reprocessIntegratedHeatData: false);

    string WritePeaqXml(Action<XDocument> edit)
    {
        var xml = XDocument.Load(Fixture("TpxWT_CtFT(1).apj"));
        edit(xml);
        var path = Path.Combine(directory, "experiment.apj");
        xml.Save(path);
        return path;
    }

    string WritePeaq(string dateText)
    {
        var xml = XDocument.Load(Fixture("TpxWT_CtFT(1).apj"));
        var date = xml.Root.Element("Experiments").Element("Experiment").Element("ModifiedDate");
        if (dateText == null) date.Remove();
        else date.Value = dateText;
        var path = Path.Combine(directory, "experiment.apj");
        xml.Save(path);
        return path;
    }

    string WriteTa(int traceEnd, int[] injectionTimes)
    {
        var text = new StringBuilder("$ITC\n# 0\n# 1\n# 0.1\n# 0.2\n# 25\n@0\n");
        for (var time = 0; time <= traceEnd; time++)
        {
            text.Append(time.ToString(CultureInfo.InvariantCulture)).AppendLine(",1");
            var injection = Array.IndexOf(injectionTimes, time);
            if (injection >= 0)
                text.Append('@').Append((injection + 1).ToString(CultureInfo.InvariantCulture)).AppendLine(",2");
        }
        var path = Path.Combine(directory, "experiment.ta");
        File.WriteAllText(path, text.ToString());
        return path;
    }

    static T WithCulture<T>(string locale, Func<T> read)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
            return read();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "FileTypeTests", name);

    sealed class JoulePrompt : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
            => new(EnergyUnit.Joule, false, false, false, 25);
    }

    sealed class ValidationPrompt : IDataValidationPromptService
    {
        public DataValidationPromptAction Action { get; set; }
        public List<(string Message, bool CanFix, bool AllowKeep)> Calls { get; } = new();

        public DataValidationPromptResult AskValidationIssue(string title, string message, bool canFix, bool requiresInput, bool allowKeep = true)
        {
            Calls.Add((message, canFix, allowKeep));
            return new DataValidationPromptResult(Action);
        }
    }
}
