using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using Json.Schema;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    /// <summary>
    /// Exercises the published JSON schemas through JsonSchema.Net, rather than
    /// through the FTXTC reader or its persistence DTOs.
    /// </summary>
    public sealed class FtxtcJsonSchemaValidationTests
    {
        [Fact]
        public async Task CurrentWriterOutputValidatesAgainstPublishedSchemas()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var report = new AnalysisReport { Name = "Schema validation report" };
            report.SetResultIds(containers.OfType<AnalysisResult>().Select(result => result.UniqueID));

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(
                package,
                containers.OfType<ExperimentData>(),
                containers.OfType<AnalysisResult>(),
                containers,
                new[] { report });

            AssertPackageJsonValidates(package);
        }

        [Theory]
        [InlineData(ProfileLikelihoodCalibration.WeightedFCalibratedStandardizedRss)]
        [InlineData(ProfileLikelihoodCalibration.WeightedChiSquared)]
        public async Task WeightedProfileCalibrationIdsValidateAgainstPublishedSchemas(
            ProfileLikelihoodCalibration calibration)
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var result = Assert.Single(containers.OfType<AnalysisResult>());
            result.Solution.ProfileLikelihoodRun = new ProfileLikelihoodRunResult(
                .95, calibration, 20, 1, 1, 19, 12, 1.25,
                SolverAlgorithm.NelderMead, true, 1, 30, 24, 40,
                TimeSpan.Zero, ErrorEstimationOutcome.Completed,
                Array.Empty<ProfileCoordinateResult>());

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(
                package,
                containers.OfType<ExperimentData>(),
                new[] { result });

            AssertPackageJsonValidates(package);
        }

        [Fact]
        public void CurrentJorsFixtureValidatesAgainstPublishedSchemas()
        {
            using var package = File.OpenRead(Fixture("jors.ftxtc"));

            AssertPackageJsonValidates(package);
        }

        [Fact]
        public void CurrentSequentialFixtureValidatesAgainstPublishedSchemas()
        {
            using var package = File.OpenRead(Fixture("sequential-hsa.ftxtc"));

            AssertPackageJsonValidates(package);
        }

        [Fact]
        public async Task SchemasRejectInvalidManifestReferenceAndModelVersion()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), containers.OfType<AnalysisResult>());

            var manifest = ReadJson(package, "manifest.json").AsObject();
            manifest["schemaMinor"] = 3;
            AssertInvalid("manifest.schema.json", manifest);

            var project = ReadJson(package, "project.json").AsObject();
            project["experiments"]!.AsArray()[0]!.AsObject()["metadata"] = "../experiment.json";
            AssertInvalid("project.schema.json", project);

            var solutionPath = project["solutions"]!.AsArray()[0]!.AsObject()["metadata"]!.GetValue<string>();
            var solution = ReadJson(package, solutionPath).AsObject();
            solution["modelSchemaVersion"] = 2;
            AssertInvalid("component.schema.json", solution);
        }

        static void AssertPackageJsonValidates(Stream package)
        {
            using var archive = OpenArchive(package);
            foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".json", StringComparison.Ordinal)))
            {
                var schemaName = entry.FullName switch
                {
                    "manifest.json" => "manifest.schema.json",
                    "project.json" => "project.schema.json",
                    _ => "component.schema.json",
                };
                using var document = JsonDocument.Parse(entry.Open());
                AssertValid(schemaName, document.RootElement, entry.FullName);
            }
        }

        static void AssertValid(string schemaName, JsonElement instance, string description)
        {
            var result = LoadSchema(schemaName).Evaluate(instance, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });
            Assert.True(result.IsValid, $"{description} does not validate against {schemaName}.");
        }

        static void AssertInvalid(string schemaName, JsonNode instance)
        {
            using var document = JsonDocument.Parse(instance.ToJsonString());
            var result = LoadSchema(schemaName).Evaluate(document.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });
            Assert.False(result.IsValid, $"Invalid {schemaName} input unexpectedly validated.");
        }

        static readonly IReadOnlyDictionary<string, Lazy<JsonSchema>> Schemas =
            new Dictionary<string, Lazy<JsonSchema>>(StringComparer.Ordinal)
            {
                ["manifest.schema.json"] = new Lazy<JsonSchema>(() => LoadSchemaFile("manifest.schema.json")),
                ["project.schema.json"] = new Lazy<JsonSchema>(() => LoadSchemaFile("project.schema.json")),
                ["component.schema.json"] = new Lazy<JsonSchema>(() => LoadSchemaFile("component.schema.json")),
            };

        static JsonSchema LoadSchema(string name) => Schemas[name].Value;

        static JsonSchema LoadSchemaFile(string name) => JsonSchema.FromFile(
            Path.Combine(AppContext.BaseDirectory, "Schemas", "FTXTC", name));

        static JsonNode ReadJson(Stream package, string path)
        {
            using var archive = OpenArchive(package);
            using var entry = archive.GetEntry(path)?.Open()
                ?? throw new InvalidDataException($"Test package is missing '{path}'.");
            return JsonNode.Parse(entry)
                ?? throw new InvalidDataException($"Test package contains empty JSON at '{path}'.");
        }

        static ZipArchive OpenArchive(Stream package)
        {
            package.Position = 0;
            return new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        }

        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    }
}
