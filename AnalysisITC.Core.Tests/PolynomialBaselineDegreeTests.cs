using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Published model reproduction")]
public sealed class PolynomialBaselineDegreeTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(12, 12)]
    [InlineData(24, 24)]
    [InlineData(32, 24)]
    public void DegreeIsBoundedInTheSharedProcessor(int requested, int expected)
    {
        var data = new ExperimentData("degree.itc");
        var polynomial = new PolynomialLeastSquaresInterpolator(data.Processor) { Degree = requested };

        Assert.Equal(expected, polynomial.Degree);
    }

    [Fact]
    public async Task OlderDegree32ProjectStillLoadsAndRetainsItsSavedBaseline()
    {
        var data = new ExperimentData("legacy-degree.itc")
        {
            DataPoints = new List<DataPoint> { new(0, 1), new(1, 2), new(2, 3) },
        };
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        data.Processor.Interpolator.Baseline = new List<Energy> { new(0.1), new(0.2), new(0.3) };
        data.Processor.BaselineCompleted = true;
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { data });
        using var legacy = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/experiment.json")) return bytes;
            var root = JsonNode.Parse(bytes);
            root["processor"]["polynomial"]["degree"] = 32;
            return Encoding.UTF8.GetBytes(root.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        var read = await FTXTCReader.ReadWithRecovery(legacy, FtxtcReadPolicy.Strict);
        Assert.Empty(read.Issues);
        var restored = Assert.Single(read.Containers.OfType<ExperimentData>());
        var polynomial = Assert.IsType<PolynomialLeastSquaresInterpolator>(restored.Processor.Interpolator);
        Assert.Equal(24, polynomial.Degree);
        Assert.Equal(new[] { 0.1, 0.2, 0.3 }, polynomial.Baseline.Select(value => value.Value));
    }
}
