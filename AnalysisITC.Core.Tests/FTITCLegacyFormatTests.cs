using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class FTITCLegacyFormatTests
    {
        [Fact]
        public async Task OriginalTaggedProjectRestoresThermogramDataPoints()
        {
            const string text =
                "<Experiment>" +
                "<FileName>old-project.itc</FileName>" +
                "<ID>legacy-experiment</ID>" +
                "<Date>2018-04-03T12:30:00.0000000</Date>" +
                "<SyringeConcentration>0.001,0</SyringeConcentration>" +
                "<CellConcentration>0.0001,0</CellConcentration>" +
                "<StirringSpeed>750</StirringSpeed>" +
                "<TargetTemperature>25</TargetTemperature>" +
                "<MeasuredTemperature>25.1</MeasuredTemperature>" +
                "<InitialDelay>60</InitialDelay>" +
                "<TargetPowerDiff>5</TargetPowerDiff>" +
                "<FeedBackMode>2</FeedBackMode>" +
                "<CellVolume>0.0002</CellVolume>" +
                "<Include>1</Include>" +
                "<InjectionList>0,0,10,0.000002,120,4,25,0,60;1,1,130,0.000002,120,4,25,0,60</InjectionList>" +
                "<DataPointList>0,0.000010,25,24.9;1,0.000011,25.01,24.9;2,0.000012,25.02,24.9</DataPointList>" +
                "</Experiment>";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var experiment = Assert.Single((await FTITCReader.ReadStream(stream, processProcessorData: false)).OfType<ExperimentData>());

            Assert.Equal("legacy-experiment", experiment.UniqueID);
            Assert.Equal(3, experiment.DataPoints.Count);
            Assert.Equal(0f, experiment.DataPoints[0].Time);
            Assert.Equal(0.000011f, experiment.DataPoints[1].Power);
            Assert.Equal(25.02f, experiment.DataPoints[2].Temperature);
            Assert.Equal(2, experiment.Injections.Count);
        }

        [Fact]
        public async Task IntegratedHeatProjectWithHistoricalSourceOrdinalNeedsNoThermogram()
        {
            const string text = @"FTITCVersion:1.4.0
FILE:Experiment:integrated.DH
Name:Integrated heats
ID:legacy-integrated
Date:2026-04-12T12:00:00.0000000+02:00
Source:5
Comments:
Include:1
SyringeConcentration:0.002,0
CellConcentration:0.00011,0
StirringSpeed:-1
TargetTemperature:25
MeasuredTemperature:25
InitialDelay:0
TargetPowerDiff:0
FeedBackMode:0
CellVolume:0.0002013
Instrument:0
LIST:InjectionList
0,0,0,5E-07,0,0,0,0,0,0.0001097,4.96E-06,-1.49E-07,0
1,1,1,3E-06,0,0,0,0,0,0.0001081,3.45E-05,-0.000139,2E-06
ENDLIST
LIST:DataPointList
ENDLIST
OBJECT:DataProcessor
ProcessorType:-1
ENDOBJECT
ENDFILE";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var experiment = Assert.Single((await FTITCReader.ReadStream(stream, processProcessorData: false)).OfType<ExperimentData>());

            Assert.Equal(ITCDataFormat.IntegratedHeats, experiment.DataSourceFormat);
            Assert.Empty(experiment.DataPoints);
            Assert.Equal(2, experiment.Injections.Count);
            Assert.All(experiment.Injections, injection => Assert.True(injection.IsIntegrated));
            Assert.Equal(new FloatWithError(-0.000139, 0.000002), experiment.Injections[1].RawPeakArea);
            Assert.True(experiment.CanBeAnalyzed);
        }

    }
}
