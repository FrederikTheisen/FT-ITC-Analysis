using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class MicroCalInstrumentTests
    {
        [Fact]
        public void VpItcHeaderMarkerResolvesInstrument()
        {
            var instrument = ITCInstrumentAttribute.TryResolveMicroCalInstrument("% VPITC_123456");

            Assert.Equal(ITCInstrument.MicroCalVPITC, instrument);
        }

        [Fact]
        public void VpItcCellVolumeResolvesInstrumentWhenHeaderMarkerIsUnavailable()
        {
            var experiment = new ExperimentData("vp-itc.itc")
            {
                Instrument = ITCInstrument.Unknown,
                CellVolume = 1479.1e-6,
            };

            ITCInstrumentAttribute.ResolveInstrument(experiment);

            Assert.Equal(ITCInstrument.MicroCalVPITC, experiment.Instrument);
        }

        [Fact]
        public void ItcReaderRecognizesVpItcWithoutASeparateFileFormat()
        {
            var sourceText = File.ReadAllText(Fixture("data_1.itc"))
                .Replace("MICROCALITC_MAL1122598", "VPITC_123456", StringComparison.Ordinal);
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(sourceText));

            var experiment = MicroCalITC200Reader.ReadStream(source, "vp-itc.itc");

            Assert.Equal(ITCDataFormat.ITC200, experiment.DataSourceFormat);
            Assert.Equal(ITCInstrument.MicroCalVPITC, experiment.Instrument);
        }

        [Fact]
        public async Task VpItcInstrumentRoundTripsThroughProjectFormats()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var experiment = (await FTITCReader.ReadStream(source)).OfType<ExperimentData>().First();
            experiment.Instrument = ITCInstrument.MicroCalVPITC;

            using var legacy = new MemoryStream();
            await FTITCWriter.WriteStream(legacy, new[] { experiment });
            legacy.Position = 0;
            var legacyRestored = Assert.Single((await FTITCReader.ReadStream(legacy)).OfType<ExperimentData>());

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { experiment });
            package.Position = 0;
            var packageRestored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());

            Assert.Equal(ITCInstrument.MicroCalVPITC, legacyRestored.Instrument);
            Assert.Equal(ITCInstrument.MicroCalVPITC, packageRestored.Instrument);
        }

        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    }
}
