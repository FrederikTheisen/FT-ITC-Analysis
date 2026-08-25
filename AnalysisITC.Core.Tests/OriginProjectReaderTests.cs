using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Application;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class OriginProjectReaderTests
    {
        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "FileTypeTests", name);

        [Fact]
        public void OpjExtensionIsDetectedCaseInsensitively()
        {
            Assert.Equal(ITCDataFormat.OriginProject, DataReader.GetFormat(Fixture("G223W_Mn_onesite_first_run.OPJ")));
        }

        [Fact]
        public void LegacyOriginFixtureRestoresPrimaryItcWorksheet()
        {
            var experiment = OriginProjectReader.ReadFile(Fixture("G223W_Mn_onesite_first_run.OPJ"));

            Assert.Equal(ITCDataFormat.OriginProject, experiment.DataSourceFormat);
            Assert.Equal("G223W_Mn_onesite_first_run.OPJ", experiment.FileName);
            Assert.Equal(20, experiment.Injections.Count);
            Assert.Equal(2_998, experiment.DataPoints.Count);
            Assert.Equal(25e-6, experiment.CellConcentration.Value, 12);
            Assert.Equal(6e-3, experiment.SyringeConcentration.Value, 12);
            Assert.Equal(203.9e-6, experiment.CellVolume, 7);
            Assert.Equal(24.986265, experiment.TargetTemperature, 6);
            Assert.Equal(FeedbackMode.High, experiment.FeedBackMode);
            Assert.Null(experiment.Solution);
            Assert.False(experiment.Processor.BaselineCompleted);

            var first = experiment.Injections[0];
            var last = experiment.Injections[19];
            Assert.False(first.Include);
            Assert.False(first.IsIntegrated);
            Assert.Equal(596f, first.Time);
            Assert.Equal(120f, first.Delay);
            Assert.Equal(-3f, first.IntegrationStartDelay);
            Assert.Equal(48f, first.IntegrationEndOffset);
            Assert.Equal(0, first.RawPeakArea.Value);
            Assert.Equal(0, last.RawPeakArea.Value);
            Assert.Equal(11.758930381830904e-6, first.ActualTitrantConcentration, 12);
            Assert.Equal(24.951004409603135e-6, first.ActualCellConcentration, 12);
            Assert.Equal(.4712808425982695, first.Ratio, 12);

            Assert.Equal(5f, experiment.DataPoints[0].Time);
            Assert.Equal(3002f, experiment.DataPoints[^1].Time);
            Assert.Equal(.0020302318927907237e-6 * 4.184, experiment.DataPoints[0].Power, 12);
            Assert.Contains("Selected Origin worksheet: n6mM070220", experiment.Comments);
            Assert.Contains("ResultsLog", experiment.Comments);
            Assert.Contains("OneSites", experiment.Comments);
            Assert.Contains("Embedded thermogram was restored", experiment.Comments);
            Assert.Contains("Source DH heats were not imported", experiment.Comments);
        }

        [Theory]
        [InlineData("CPYA 4,2673 552#")]
        [InlineData("CPYA 4.3478 212 W64 #")]
        [InlineData("CPYA 4,3168 226 W32 #")]
        public void OriginSignatureVersionTextIsDiagnosticOnly(string signatureText)
        {
            var bytes = File.ReadAllBytes(Fixture("G223W_Mn_onesite_first_run.OPJ"));
            bytes = WithSignature(bytes, signatureText);

            using var stream = new MemoryStream(bytes);
            var experiment = OriginProjectReader.ReadStream(stream, "variant.OPJ");

            Assert.Equal(20, experiment.Injections.Count);
            Assert.Contains(signatureText, experiment.Comments);
        }

        [Fact]
        public async Task OriginSourceFormatSurvivesFtxtcRoundTrip()
        {
            var source = OriginProjectReader.ReadFile(Fixture("G223W_Mn_onesite_first_run.OPJ"));
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { source });
            package.Position = 0;

            var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());
            Assert.Equal(ITCDataFormat.OriginProject, restored.DataSourceFormat);
            Assert.Equal(source.DataPoints.Count, restored.DataPoints.Count);
            Assert.Equal(source.Injections.Count, restored.Injections.Count);
            Assert.Equal(source.Injections[0].RawPeakArea.Value, restored.Injections[0].RawPeakArea.Value, 12);
            Assert.Contains("ResultsLog", restored.Comments);
        }

        [Fact]
        public async Task OriginSourceFormatSurvivesLegacyFtitcRoundTrip()
        {
            var source = OriginProjectReader.ReadFile(Fixture("G223W_Mn_onesite_first_run.OPJ"));
            using var project = new MemoryStream();
            await FTITCWriter.WriteStream(project, new[] { source });
            project.Position = 0;

            var restored = Assert.Single((await FTITCReader.ReadStream(project, processProcessorData: false)).OfType<ExperimentData>());
            Assert.Equal(ITCDataFormat.OriginProject, restored.DataSourceFormat);
            Assert.Equal(source.Injections.Count, restored.Injections.Count);
            Assert.Equal(source.Injections[0].RawPeakArea.Value, restored.Injections[0].RawPeakArea.Value, 12);
        }

        [Fact]
        public void WrongSignatureAndTruncatedOriginFilesAreRejected()
        {
            var bytes = File.ReadAllBytes(Fixture("G223W_Mn_onesite_first_run.OPJ"));
            bytes = WithSignature(bytes, "NOPE 4.2673 552#");

            using (var wrongSignature = new MemoryStream(bytes))
            {
                var exception = Assert.Throws<FormatException>(() => OriginProjectReader.ReadStream(wrongSignature, "wrong.OPJ"));
                Assert.Contains("wrong.OPJ", exception.Message);
            }

            using (var truncated = new MemoryStream(File.ReadAllBytes(Fixture("G223W_Mn_onesite_first_run.OPJ")).Take(100).ToArray()))
            {
                var exception = Assert.Throws<FormatException>(() => OriginProjectReader.ReadStream(truncated, "truncated.OPJ"));
                Assert.Contains("truncated.OPJ", exception.Message);
            }
        }

        [Fact]
        public void OversizedBlockAndProjectWithoutItcWorksheetAreRejected()
        {
            var oversized = System.Text.Encoding.ASCII.GetBytes("CPYA 4.2673 552#\n")
                .Concat(new byte[] { 0xff, 0xff, 0xff, 0xff, 0x0a })
                .ToArray();
            using (var stream = new MemoryStream(oversized))
                Assert.Throws<FormatException>(() => OriginProjectReader.ReadStream(stream, "oversized.OPJ"));

            var document = ParseFixture();
            foreach (var column in document.Columns.Where(column =>
                column.Name.EndsWith("DH", StringComparison.OrdinalIgnoreCase)
                || column.Name.EndsWith("INJV", StringComparison.OrdinalIgnoreCase)))
            {
                column.Name += "_IGNORED";
            }

            var exception = Assert.Throws<FormatException>(() =>
                OriginExperimentMapper.Map(document, "no-itc.OPJ", null));
            Assert.Contains("no compatible ITC worksheet", exception.Message);
        }

        [Fact]
        public void MissingTraceAndParametersUseDocumentedFallbacks()
        {
            var noTrace = ParseFixture();
            noTrace.Columns.RemoveAll(column =>
                column.Name.EndsWith("RAW_time", StringComparison.OrdinalIgnoreCase)
                || column.Name.EndsWith("RAW_cp", StringComparison.OrdinalIgnoreCase));

            var heatsOnly = OriginExperimentMapper.Map(noTrace, "heats-only.OPJ", null);
            Assert.Empty(heatsOnly.DataPoints);
            Assert.Contains("imported as source heats only", heatsOnly.Comments);
            Assert.Equal(20, heatsOnly.Injections.Count);
            Assert.All(heatsOnly.Injections, injection => Assert.True(injection.IsIntegrated));
            Assert.Equal(.5504273975424692e-6 * 4.184, heatsOnly.Injections[0].RawPeakArea.Value, 12);

            var noParameters = ParseFixture();
            noParameters.Parameters.Clear();
            var inferred = OriginExperimentMapper.Map(noParameters, "inferred.OPJ", null);

            Assert.True(inferred.CellConcentration.Value > 0);
            Assert.True(inferred.SyringeConcentration.Value > 0);
            Assert.True(inferred.CellVolume > 0);
            Assert.Equal(AppSettings.ReferenceTemperature, inferred.TargetTemperature, 6);
            Assert.Contains("metadata was unavailable", inferred.Comments);
        }

        [Fact]
        public void RawTraceDoesNotRequireIntegratedHeatColumns()
        {
            var rawOnly = ParseFixture();
            rawOnly.Columns.RemoveAll(column =>
                column.Name.EndsWith("DH", StringComparison.OrdinalIgnoreCase));

            var experiment = OriginExperimentMapper.Map(rawOnly, "raw-only.OPJ", null);

            Assert.Equal(2_998, experiment.DataPoints.Count);
            Assert.Equal(20, experiment.Injections.Count);
            Assert.All(experiment.Injections, injection => Assert.False(injection.IsIntegrated));
            Assert.All(experiment.Injections, injection => Assert.Equal(0, injection.RawPeakArea.Value));
            Assert.Contains("Embedded thermogram was restored", experiment.Comments);
            Assert.DoesNotContain("Source DH heats were not imported", experiment.Comments);
        }

        [Fact]
        public void OpjuRemainsUnsupported()
        {
            Assert.Equal(ITCDataFormat.Unknown, DataReader.GetFormat("newer-project.opju"));
        }

        static OriginProjectDocument ParseFixture()
        {
            using var stream = File.OpenRead(Fixture("G223W_Mn_onesite_first_run.OPJ"));
            return OriginProjectParser.Read(stream);
        }

        static byte[] WithSignature(byte[] source, string signature)
        {
            var sourceLineEnd = Array.IndexOf(source, (byte)'\n');
            Assert.True(sourceLineEnd >= 0);
            var replacement = System.Text.Encoding.ASCII.GetBytes(signature + "\n");
            return replacement.Concat(source.Skip(sourceLineEnd + 1)).ToArray();
        }
    }
}
