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
using AnalysisITC.Core.Numerics;
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
        [Theory]
        [InlineData(ITCInstrument.TAInstrumentsNanoITCLowVolume, "ta-nano-itc-low-volume", "TA Instruments Nano ITC (Low Volume)")]
        [InlineData(ITCInstrument.TAInstrumentsNanoITCStandard, "ta-nano-itc-standard", "TA Instruments Nano ITC (Standard Volume)")]
        [InlineData(ITCInstrument.TAInstrumentsAffinityITCLowVolume, "ta-affinity-itc-low-volume", "TA Instruments Affinity ITC (Low Volume)")]
        [InlineData(ITCInstrument.TAInstrumentsAffinityITCStandard, "ta-affinity-itc-standard", "TA Instruments Affinity ITC (Standard Volume)")]
        [InlineData(ITCInstrument.TAInstrumentsITCLowVolume, "ta-itc-low-volume", "TA Instruments ITC Low Vol")]
        [InlineData(ITCInstrument.TAInstrumentsITCStandard, "ta-itc-standard", "TA Instruments ITC Standard")]
        public async Task TaInstrumentIdentitiesValidateRoundTripAndReachViewer(ITCInstrument instrument, string wireId, string name)
        {
            var source = new ExperimentData("instrument.nitc") { Instrument = instrument, CellVolume = 182e-6 };
            source.DataPoints.Add(new DataPoint(0, 0, 25));
            source.DataPoints.Add(new DataPoint(1, 0, 25));
            source.Injections.Add(new InjectionData(source, 1e-6));
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { source });
            AssertPackageJsonValidates(package);
            Assert.Equal(wireId, ReadJson(package, "experiments/000000/experiment.json")["instrument"].GetValue<string>());
            package.Position = 0;
            var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());
            Assert.Equal(instrument, restored.Instrument);
            Assert.Equal(182e-6, restored.CellVolume, 12);
            Assert.Contains(restored.GetInfoString(), line => line.Contains(name));
            package.Position = 0;
            var viewer = await new AnalysisITC.Core.Viewer.ViewerDocumentReader().ReadAsync(package, "instrument.ftxtc", AnalysisITC.Core.Viewer.ViewerFileFormat.Ftxtc);
            Assert.Equal(name, Assert.Single(viewer.Experiments).Instrument);
        }

        [Theory]
        [InlineData("originDescription", "null")]
        [InlineData("originDescription", "\"\"")]
        [InlineData("originDescription", "\"Recorded merge\\nSources: first + second\"")]
        [InlineData("tandemSourceExperimentIds", "null")]
        [InlineData("tandemSourceExperimentIds", "[]")]
        [InlineData("tandemSourceExperimentIds", "[\"second\",\"first\",\"unavailable\"]")]
        public async Task ExperimentProvenanceIsOptionalAndAcceptsDocumentedValues(string property, string value)
        {
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { new ExperimentData("ordinary.itc") });
            AssertPackageJsonValidates(package);
            var experiment = ReadJson(package, "experiments/000000/experiment.json").AsObject();
            Assert.False(experiment.ContainsKey("originDescription"));
            Assert.False(experiment.ContainsKey("tandemSourceExperimentIds"));
            experiment[property] = JsonNode.Parse(value);
            AssertValid("component.schema.json", JsonSerializer.SerializeToElement(experiment), property);
        }

        [Theory]
        [InlineData("originDescription", "42")]
        [InlineData("originDescription", "true")]
        [InlineData("originDescription", "[]")]
        [InlineData("originDescription", "{}")]
        [InlineData("tandemSourceExperimentIds", "\"first\"")]
        [InlineData("tandemSourceExperimentIds", "42")]
        [InlineData("tandemSourceExperimentIds", "{}")]
        [InlineData("tandemSourceExperimentIds", "[42]")]
        [InlineData("tandemSourceExperimentIds", "[null]")]
        [InlineData("tandemSourceExperimentIds", "[\"\"]")]
        public async Task ExperimentProvenanceRejectsInvalidTypesAndEmptyIdentities(string property, string value)
        {
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { new ExperimentData("ordinary.itc") });
            var experiment = ReadJson(package, "experiments/000000/experiment.json");
            experiment[property] = JsonNode.Parse(value);
            AssertInvalid("component.schema.json", experiment);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CompletedSaltPolicyIsOptionalOnWireAndMustBeBooleanWhenPresent(bool policy)
        {
            var result = SaltAnalysisTests.Create(new[] { .04, .16, .36 });
            result.ElectrostaticsAnalysis.RestoreResult(new(new(1e-6), new(2), new(0)), null,
                8, 0, DateTime.UtcNow, ErrorEstimationMethod.None, policy);
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, result.Solution.Solutions.Select(s => s.Data), new[] { result });
            AssertPackageJsonValidates(package);
            var json = ReadJson(package, "results/000000/result.json");
            var salt = json["advancedAnalyses"]["electrostatics"].AsObject();
            Assert.Equal(policy, salt["includeBufferInIonicStrengthCalc"].GetValue<bool>());
            salt["includeBufferInIonicStrengthCalc"] = "true";
            AssertInvalid("component.schema.json", json);
            salt.Remove("includeBufferInIonicStrengthCalc");
            AssertValid("component.schema.json", JsonSerializer.SerializeToElement(json), "older optional salt policy");
        }

        [Theory]
        [InlineData(-5)]
        [InlineData(0)]
        public async Task ValidityTemperatureFieldsAreOptionalAndTyped(double temperature)
        {
            var result = AnalysisResultTemperatureValidityTests.Create(temperature);
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, result.Model.Models.Select(model => model.Data), new[] { result });
            AssertPackageJsonValidates(package);
            var json = ReadJson(package, "results/000000/result.json").AsObject();
            var validity = json["validity"].AsObject();
            var experiment = validity["experiments"].AsArray()[0].AsObject();
            Assert.Equal(temperature, experiment["measuredTemperature"].GetValue<double>());
            Assert.False(validity["temperatureDependenceEnabled"].GetValue<bool>());
            experiment["measuredTemperature"] = "warm";
            AssertInvalid("component.schema.json", json);
            experiment["measuredTemperature"] = null;
            validity["temperatureDependenceEnabled"] = "true";
            AssertInvalid("component.schema.json", json);
        }

        [Theory]
        [InlineData("one-c100-v0.01", DilutionMethod.Exponential)]
        [InlineData("sequential-4", DilutionMethod.Exponential)]
        [InlineData("one-c100-v0.01", DilutionMethod.DiscreteDisplacement)]
        [InlineData("sequential-4", DilutionMethod.DiscreteDisplacement)]
        public async Task ComponentsValidateAndRejectInconsistentBookkeeping(string id, DilutionMethod method)
        {
            var model = InjectionProcessingMethodTests.FittedModel(id, bootstrap: true, method: method);
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { model.Data });
            AssertPackageJsonValidates(package);
            var experiment = ReadJson(package, "experiments/000000/experiment.json").AsObject();
            experiment["concentrationMethod"] = "microcal";
            AssertInvalid("component.schema.json", experiment);
            var solution = ReadJson(package, "solutions/000000/solution.json").AsObject();
            solution.Remove("heatMethod");
            AssertInvalid("component.schema.json", solution);
            var bootstrap = ReadJson(package, "solutions/000000/bootstrap.json").AsObject();
            bootstrap["replicates"]!.AsArray()[0]!["concentrationMethod"] = "microcal";
            AssertInvalid("component.schema.json", bootstrap);
        }

        [Fact]
        public async Task BootstrapRequestIsOptionalNonnegativeConvergenceMetadata()
        {
            var model = InjectionProcessingMethodTests.FittedModel("one-c100-v0.01", bootstrap: true);
            model.Solution.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
            model.ModelCloneOptions.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            model.Solution.Convergence.CaptureBootstrapRequest(ErrorEstimationMethod.BootstrapResiduals, 100);
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { model.Data });
            AssertPackageJsonValidates(package);
            var solution = ReadJson(package, "solutions/000000/solution.json").AsObject();
            var convergence = solution["convergence"].AsObject();
            Assert.Equal(100, convergence["requestedBootstrapIterations"].GetValue<int>());
            convergence["requestedBootstrapIterations"] = -1;
            AssertInvalid("component.schema.json", solution);
            convergence.Remove("requestedBootstrapIterations");
            using var document = JsonDocument.Parse(solution.ToJsonString());
            AssertValid("component.schema.json", document.RootElement, "Historical convergence without requested count");
        }

        [Fact]
        public async Task CurrentWriterOutputValidatesAgainstPublishedSchemas()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var report = new AnalysisReport { Name = "Schema validation report" };
            report.SetResultIds(containers.OfType<AnalysisResult>().Select(result => result.UniqueID));
            // Regions set by Fit Peaks use the "fit" wire value.
            containers.OfType<ExperimentData>().First().Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Fit;

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(
                package,
                containers.OfType<ExperimentData>(),
                containers.OfType<AnalysisResult>(),
                containers,
                new[] { report });

            AssertPackageJsonValidates(package);
        }

        [Fact]
        public async Task StoredBufferReferenceValuesValidateAgainstPublishedSchemas()
        {
            var data = new ExperimentData("target.itc");
            data.SetID("target");
            var attribute = new BufferSubtractionSettings("blank", BufferSubtractionMethod.Linear,
                new BufferSubtractionReferenceSnapshot("Blank", new[]
                {
                    new BufferSubtractionReferencePoint(1, new FloatWithError(2e-6, 1e-8)),
                    new BufferSubtractionReferencePoint(3, new FloatWithError(3e-6, 1e-8)),
                })).ToAttribute();
            data.Attributes.Add(attribute);

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { data });

            AssertPackageJsonValidates(package);
            var experiment = ReadJson(package, "experiments/000000/experiment.json").AsObject();
            experiment["attributes"]!.AsArray()[0]!["bufferReferenceSnapshot"]!["points"]!.AsArray()[0]!["unexpected"] = 1;
            AssertInvalid("component.schema.json", experiment);
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

        [Theory]
        [InlineData("legacy", 1)]
        [InlineData("dumas-simpson", 2)]
        [InlineData("pytc-discrete", 3)]
        public void SavedOffsetNullSolutionsValidateWithTheirHeatMethod(string heatMethod, int modelSchemaVersion)
        {
            using var package = File.OpenRead(Fixture("jors.ftxtc"));
            var result = ReadJson(package, "results/000000/result.json").AsObject();
            var members = result["nullComparison"]!["members"]!.AsArray();
            Assert.NotEmpty(members);
            foreach (var member in members)
            {
                var solution = member!["solution"]!.AsObject();
                Assert.Equal("offset", solution["modelId"]!.GetValue<string>());
                solution["heatMethod"] = heatMethod;
                solution["modelSchemaVersion"] = modelSchemaVersion;
            }
            using (var document = JsonDocument.Parse(result.ToJsonString()))
                AssertValid("component.schema.json", document.RootElement, "Result with saved Offset Null solutions");

            members[0]!["solution"]!["modelSchemaVersion"] = modelSchemaVersion + 1;
            AssertInvalid("component.schema.json", result);
            members[0]!["solution"]!["modelSchemaVersion"] = modelSchemaVersion;
            members[0]!["solution"]!["modelId"] = "unknown-null-model";
            AssertInvalid("component.schema.json", result);
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

        internal static void AssertPackageJsonValidates(Stream package)
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
