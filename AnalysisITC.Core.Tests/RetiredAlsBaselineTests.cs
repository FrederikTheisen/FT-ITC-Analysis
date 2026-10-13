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

// The asymmetric least-squares baseline was removed (ITC-058). Saved ALS processors restore as polynomial baselines.
#pragma warning disable CS0618
[Collection("Published model reproduction")]
public sealed class RetiredAlsBaselineTests
{
    [Fact]
    public async Task SavedAlsProcessorRestoresAsDefaultPolynomialAndKeepsItsStoredBaseline()
    {
        var data = new ExperimentData("als.itc")
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
            root["processor"]["type"] = "asl";
            root["processor"]["polynomial"] = null;
            root["processor"]["asl"] = new JsonObject { ["iterations"] = 17, ["lambda"] = 2200.0, ["asymmetry"] = 0.91 };
            return Encoding.UTF8.GetBytes(root.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        var read = await FTXTCReader.ReadWithRecovery(legacy, FtxtcReadPolicy.Strict);

        Assert.Empty(read.Issues);
        var restored = Assert.Single(read.Containers.OfType<ExperimentData>());
        var polynomial = Assert.IsType<PolynomialLeastSquaresInterpolator>(restored.Processor.Interpolator);
        Assert.Equal(BaselineInterpolatorTypes.Polynomial, restored.Processor.BaselineType);
        Assert.Equal(12, polynomial.Degree);
        Assert.True(restored.Processor.BaselineCompleted);
        Assert.Equal(new[] { 0.1, 0.2, 0.3 }, polynomial.Baseline.Select(value => value.Value));
        Assert.Equal(new[] { 0.9, 1.8, 2.7 }, restored.BaseLineCorrectedDataPoints.Select(point => (double)point.Power), new ToleranceComparer(1e-6));

        using var resaved = new MemoryStream();
        await FTXTCWriter.WriteStream(resaved, new[] { restored });
        byte[] experimentJson = null;
        using var inspected = FTXTCFormatTests.RewriteAuthenticatedPackage(resaved, (path, bytes) =>
        {
            if (path.EndsWith("/experiment.json")) experimentJson = bytes;
            return bytes;
        }, schemaMinor: FTXTCFormat.SchemaMinor);
        var processor = JsonNode.Parse(experimentJson)["processor"];
        Assert.Equal("polynomial", processor["type"].GetValue<string>());
        Assert.Null(processor["asl"]);
    }

    [Fact]
    public async Task LegacyFtitcAlsProcessorRestoresAsDefaultPolynomial()
    {
        var data = new ExperimentData("legacy-als.itc")
        {
            DataPoints = Enumerable.Range(0, 40).Select(index => new DataPoint(index, 5 + 0.01f * index)).ToList(),
        };
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        using var written = new MemoryStream();
        await LegacyFtItcFixtureWriter.WriteStream(written, new[] { data });

        // Older files wrote ALS as type 1 with no type-specific settings.
        var lines = Encoding.UTF8.GetString(written.ToArray()).Split('\n')
            .Where(line => !line.StartsWith(FTITCFormat.PolynomiumDegree + ":") && !line.StartsWith(FTITCFormat.PolynomiumLimit + ":"))
            .Select(line => line.TrimEnd('\r') == FTITCFormat.ProcessorType + ":2" ? FTITCFormat.ProcessorType + ":1" : line)
            .ToList();
        Assert.Contains(FTITCFormat.ProcessorType + ":1", lines);
        using var legacy = new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\n", lines)));

        var restored = (await FTITCReader.ReadStream(legacy)).OfType<ExperimentData>().Single();

        var polynomial = Assert.IsType<PolynomialLeastSquaresInterpolator>(restored.Processor.Interpolator);
        Assert.Equal(BaselineInterpolatorTypes.Polynomial, restored.Processor.BaselineType);
        Assert.Equal(12, polynomial.Degree);
    }

    [Fact]
    public void RecordedAlsProcessingDiffersFromTheRestoredPolynomialBaseline()
    {
        var data = new ExperimentData("validity.itc")
        {
            DataPoints = new List<DataPoint> { new(0, 1), new(1, 2), new(2, 3) },
        };
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        data.Processor.Interpolator.Baseline = new List<Energy> { new(0.1), new(0.2), new(0.3) };
        var current = ExperimentProcessingSnapshot.Capture(data);
        var recorded = ExperimentProcessingSnapshot.Capture(data);
        recorded.BaselineType = BaselineInterpolatorTypes.ASL;

        Assert.True(recorded.BaselineChangedComparedTo(current));
        Assert.False(ExperimentProcessingSnapshot.Capture(data).BaselineChangedComparedTo(current));
    }

    [Fact]
    public void RecordedAlsTypeKeepsItsWireIdentity()
    {
        Assert.Equal(BaselineInterpolatorTypes.ASL, FtxtcWireIds.Processor("asl"));
        Assert.Equal("asl", FtxtcWireIds.Processor(BaselineInterpolatorTypes.ASL));
    }

    sealed class ToleranceComparer : IEqualityComparer<double>
    {
        readonly double tolerance;
        public ToleranceComparer(double tolerance) => this.tolerance = tolerance;
        public bool Equals(double x, double y) => System.Math.Abs(x - y) <= tolerance;
        public int GetHashCode(double value) => 0;
    }
}
#pragma warning restore CS0618
