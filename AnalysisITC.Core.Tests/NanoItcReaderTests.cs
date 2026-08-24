using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class NanoItcReaderTests
    {
        const string Buffer2022 = "20221030 Insulin (2.5 um) and Binding Buffer (20 mM HEPES, 100 mM NaCl, 2 mM EDTA).nitc";
        const string Binding2022 = "20221030 Insulin (2.5 um) and IBP (20 um) in Binding Buffer (20 mM HEPES, 100 mM NaCl, 2 mM EDTA).nitc";
        const string Buffer2023 = "20230504 Insulin (200 mM) and Binding Buffer (20 mM HEPES, 100 mM NaCl, 2 mM EDTA).nitc";
        const string Binding2023 = "20230504 Insulin (200 mM) and IBP (1 mM) in Binding Buffer (20 mM HEPES, 100 mM NaCl, 2 mM EDTA).nitc";

        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "FileTypeTests", name);

        public static IEnumerable<object[]> Fixtures()
        {
            yield return new object[] { Buffer2022, 9_528, 0d, 0d, -167.2517e-6, 24.633808d };
            yield return new object[] { Binding2022, 10_575, 20e-6, 2.5e-6, -167.4261e-6, 24.633823d };
            yield return new object[] { Buffer2023, 13_321, 1e-3, 0.2e-3, -167.29612e-6, 24.633812d };
            yield return new object[] { Binding2023, 13_196, 1e-3, 0.2e-3, -167.34577e-6, 24.633816d };
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void SuppliedFixturesRestoreRawThermogramsAndCoreMetadata(
            string fileName,
            int pointCount,
            double syringeConcentration,
            double cellConcentration,
            double firstPower,
            double firstTemperature)
        {
            var experiment = NanoItcReader.ReadPath(Fixture(fileName));

            Assert.Equal(ITCDataFormat.NanoITC, experiment.DataSourceFormat);
            Assert.Equal(pointCount, experiment.DataPoints.Count);
            Assert.Equal(20, experiment.Injections.Count);
            Assert.Equal(0f, experiment.DataPoints[0].Time);
            Assert.Equal(firstPower, experiment.DataPoints[0].Power, 9);
            Assert.Equal(firstTemperature, experiment.DataPoints[0].Temperature, 5);
            Assert.Equal(syringeConcentration, experiment.SyringeConcentration.Value, 12);
            Assert.Equal(cellConcentration, experiment.CellConcentration.Value, 12);
            Assert.Equal(182e-6, experiment.CellVolume, 12);
            Assert.Equal(25d, experiment.TargetTemperature, 12);
            Assert.Equal(125d, experiment.StirringSpeed, 12);
            Assert.Equal(ITCInstrument.TAInstrumentsITCLowVolume, experiment.Instrument);
            Assert.Equal(ExperimentDateSource.DataFile, experiment.DateSource);
            Assert.False(experiment.Processor.BaselineCompleted);
            Assert.Null(experiment.BaseLineCorrectedDataPoints);
            Assert.Contains("NanoITC source: file 3", experiment.Comments);
            Assert.Contains("serial T21004", experiment.Comments);
            Assert.Contains("software 3.8.0.18730", experiment.Comments);
            Assert.Contains("firmware", experiment.Comments);

            Assert.All(experiment.Injections, injection => Assert.Equal(2.5e-6, injection.Volume, 12));
            Assert.False(experiment.Injections[0].Include);
            Assert.All(experiment.Injections.Skip(1), injection => Assert.True(injection.Include));
            Assert.Equal(60f, experiment.Injections[0].Time);
            Assert.Equal(5f, experiment.Injections[0].Duration, 5);
            Assert.Equal(
                experiment.DataPoints.OrderBy(point => Math.Abs(point.Time - experiment.Injections[0].Time)).First().Temperature,
                experiment.Injections[0].Temperature,
                8);
            Assert.True(experiment.Injections[0].ActualTitrantConcentration >= 0);
        }

        [Fact]
        public void EmbeddedValuesWinOverTheInconsistent2023FileName()
        {
            var experiment = NanoItcReader.ReadPath(Fixture(Buffer2023));

            Assert.Equal(200e-6, experiment.CellConcentration.Value, 12);
            Assert.Equal(1e-3, experiment.SyringeConcentration.Value, 12);
            Assert.Equal(new DateTime(2023, 5, 4, 13, 4, 49, DateTimeKind.Unspecified).Date, experiment.Date.Date);
            Assert.Equal(ExperimentDateSource.DataFile, experiment.DateSource);
        }

        [Fact]
        public void RepresentativeInjectionValuesUseVendorUnitsAndTiming()
        {
            var experiment = NanoItcReader.ReadPath(Fixture(Buffer2022));
            var first = experiment.Injections[0];

            Assert.Equal(60f, first.Time);
            Assert.Equal(2.5e-6, first.Volume, 12);
            Assert.Equal(105.2f, first.Delay, 3);
            Assert.Equal(5f, first.Duration, 6);
            Assert.Equal(24.633808d, first.Temperature, 5);
            Assert.Equal(new DateTime(638027476076363879L, DateTimeKind.Unspecified), experiment.Date);
        }

        [Fact]
        public void NitcExtensionDetectionIsCaseInsensitive()
        {
            Assert.Equal(ITCDataFormat.NanoITC, DataReader.GetFormat("sample.nitc"));
            Assert.Equal(ITCDataFormat.NanoITC, DataReader.GetFormat("sample.NITC"));
            Assert.Contains(".nitc", ITCFormatAttribute.GetAllExtensions());
        }

        [Fact]
        public async Task NanoItcSourceFormatSurvivesProjectRoundTrips()
        {
            var source = NanoItcReader.ReadPath(Fixture(Binding2022));

            using (var package = new MemoryStream())
            {
                await FTXTCWriter.WriteStream(package, new[] { source });
                package.Position = 0;
                var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());
                Assert.Equal(ITCDataFormat.NanoITC, restored.DataSourceFormat);
                Assert.Equal(source.DataPoints.Count, restored.DataPoints.Count);
                Assert.Equal(source.Injections.Count, restored.Injections.Count);
            }

            using (var legacy = new MemoryStream())
            {
                await FTITCWriter.WriteStream(legacy, new[] { source });
                legacy.Position = 0;
                var restored = Assert.Single((await FTITCReader.ReadStream(legacy, processProcessorData: false)).OfType<ExperimentData>());
                Assert.Equal(ITCDataFormat.NanoITC, restored.DataSourceFormat);
            }
        }

        [Fact]
        public void WrongAndTruncatedGzipPayloadsAreRejectedByNamedStages()
        {
            using (var wrong = new MemoryStream(new byte[] { 0x50, 0x4b, 0x03, 0x04 }))
            {
                var error = Assert.Throws<FormatException>(() => NanoItcReader.ReadStream(wrong, "wrong.nitc"));
                Assert.Contains("gzip stage", error.Message);
            }

            var compressed = File.ReadAllBytes(Fixture(Buffer2022));
            using (var truncated = new MemoryStream(compressed.Take(compressed.Length / 2).ToArray()))
            {
                var error = Assert.Throws<FormatException>(() => NanoItcReader.ReadStream(truncated, "truncated.nitc"));
                Assert.Contains("stage", error.Message);
            }
        }

        [Fact]
        public void TruncatedNrbfPayloadIsRejectedByTheNrbfStage()
        {
            var payload = Decompress(Fixture(Buffer2022));
            using var truncated = Compress(payload.Take(128).ToArray());

            var error = Assert.Throws<FormatException>(() => NanoItcReader.ReadStream(truncated, "truncated-nrbf.nitc"));

            Assert.Contains("NRBF stage", error.Message);
        }

        [Fact]
        public void RootTypeRequiredTablesAndRequiredColumnsAreValidated()
        {
            AssertMutationRejected("CSC.ITCData.ITCData", "CSC.ITCData.BadData", "NRBF stage");
            AssertMutationRejected("DataPointsTable", "MissingPntTable", "missing required table");
            AssertMutationRejected("HeatRate", "NoHeat__", "missing required column");
        }

        [Fact]
        public void DecompressionAndRowLimitsAreEnforced()
        {
            using (var stream = File.OpenRead(Fixture(Buffer2022)))
            {
                var error = Assert.Throws<FormatException>(() => NanoItcReader.ReadStream(
                    stream,
                    Buffer2022,
                    decompressedSizeLimit: 1024));
                Assert.Contains("gzip stage", error.Message);
                Assert.Contains("limit", error.Message);
            }

            using (var stream = File.OpenRead(Fixture(Buffer2022)))
            {
                var error = Assert.Throws<FormatException>(() => NanoItcReader.ReadStream(
                    stream,
                    Buffer2022,
                    tableRowLimit: 100));
                Assert.Contains("schema stage", error.Message);
                Assert.Contains("row count", error.Message);
            }
        }

        [Fact]
        public void RequiredNullsInvalidLengthsAndUnsupportedValueTypesAreRejected()
        {
            var nullTable = MakeSingleColumnTable(new double[] { 1 }, nullWord: 1, activeRecord: 0);
            var nullError = Assert.Throws<FormatException>(() => nullTable.GetRequiredNumber(0, "Value"));
            Assert.Contains("data stage", nullError.Message);
            Assert.Contains("null", nullError.Message);

            var lengthError = Assert.Throws<FormatException>(() =>
                MakeSingleColumnTable(new double[] { 1 }, nullWord: 0, activeRecord: 1));
            Assert.Contains("outside its column storage", lengthError.Message);

            var unsupportedTable = MakeSingleColumnTable(new bool[] { true }, nullWord: 0, activeRecord: 0);
            var typeError = Assert.Throws<FormatException>(() => unsupportedTable.GetRequiredNumber(0, "Value"));
            Assert.Contains("unsupported numeric value type", typeError.Message);
        }

        [Fact]
        public void InvalidEmbeddedTicksFallBackToFilesystemModificationDate()
        {
            var payload = Decompress(Fixture(Buffer2022));
            ReplaceAscii(payload, "638027476076363879", "XXXXXXXXXXXXXXXXXX");
            var fallback = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Local);
            using var stream = Compress(payload);

            var experiment = NanoItcReader.ReadStream(stream, "fallback.nitc", fallback);

            Assert.Equal(fallback, experiment.Date);
            Assert.Equal(ExperimentDateSource.FileSystem, experiment.DateSource);
        }

        static NanoItcTable MakeSingleColumnTable(Array values, int nullWord, int activeRecord)
        {
            return new NanoItcTable(
                "TestTable",
                new[] { "Value" },
                new[] { values },
                new[] { new NanoItcBitArray(new[] { nullWord }, values.Length) },
                new[] { activeRecord });
        }

        static void AssertMutationRejected(string before, string after, string expectedMessage)
        {
            var payload = Decompress(Fixture(Buffer2022));
            ReplaceAscii(payload, before, after);
            using var stream = Compress(payload);

            var error = Assert.Throws<FormatException>(() => NanoItcReader.ReadStream(stream, "mutated.nitc"));

            Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
        }

        static byte[] Decompress(string path)
        {
            using var source = File.OpenRead(path);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var payload = new MemoryStream();
            gzip.CopyTo(payload);
            return payload.ToArray();
        }

        static MemoryStream Compress(byte[] payload)
        {
            var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(payload, 0, payload.Length);
            compressed.Position = 0;
            return compressed;
        }

        static void ReplaceAscii(byte[] bytes, string before, string after)
        {
            Assert.Equal(before.Length, after.Length);
            var source = Encoding.ASCII.GetBytes(before);
            var replacement = Encoding.ASCII.GetBytes(after);
            var replacements = 0;

            for (var index = 0; index <= bytes.Length - source.Length; index++)
            {
                var matches = true;
                for (var offset = 0; offset < source.Length; offset++)
                {
                    if (bytes[index + offset] == source[offset]) continue;
                    matches = false;
                    break;
                }
                if (!matches) continue;

                Array.Copy(replacement, 0, bytes, index, replacement.Length);
                replacements++;
                index += source.Length - 1;
            }

            Assert.True(replacements > 0, $"Did not find '{before}' in the decompressed fixture.");
        }
    }
}
