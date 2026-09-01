using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Viewer;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class SequentialPersistenceTestCollectionDefinition
    {
        public const string Name = "Sequential persistence and display";
    }

    [Collection(SequentialPersistenceTestCollectionDefinition.Name)]
    public sealed class SequentialPersistenceTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task FtxtcRoundTripRestoresConcreteModelCountShapeAndBootstrap(int count)
        {
            var source = CreateSolvedExperiment(count, includeBootstrap: true);
            using var stream = new MemoryStream();
            await FTXTCWriter.WriteStream(stream, new[] { source });

            stream.Position = 0;
            var restored = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<ExperimentData>());
            AssertSequentialRoundTrip(source, restored, count);

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            using var reader = new StreamReader(
                Assert.Single(archive.Entries, entry => entry.FullName.EndsWith("/solution.json", StringComparison.Ordinal)).Open());
            var state = JsonNode.Parse(await reader.ReadToEndAsync()).AsObject();
            Assert.Equal("sequential-binding-sites", state["modelId"].GetValue<string>());
            Assert.Equal(2, state["modelSchemaVersion"].GetValue<int>());
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task FtitcRoundTripRestoresConcreteModelCountShapeAndBootstrap(int count)
        {
            var source = CreateSolvedExperiment(count, includeBootstrap: true);
            using var stream = new MemoryStream();
            await FTITCWriter.WriteStream(stream, new[] { source });

            stream.Position = 0;
            var restored = Assert.Single((await FTITCReader.ReadStream(stream)).OfType<ExperimentData>());
            AssertSequentialRoundTrip(source, restored, count);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task FtxtcGlobalRoundTripPreservesSharedSequentialCount(int count)
        {
            var (experiments, result) = CreateGlobalResult(count);
            using var stream = new MemoryStream();
            await FTXTCWriter.WriteStream(stream, experiments, new[] { result });

            stream.Position = 0;
            var containers = await FTXTCReader.ReadStream(stream);
            var restored = Assert.Single(containers.OfType<AnalysisResult>());
            AssertGlobalSequentialRoundTrip(restored, count);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task FtitcGlobalRoundTripPreservesSharedSequentialCount(int count)
        {
            var (experiments, result) = CreateGlobalResult(count);
            using var stream = new MemoryStream();
            await FTITCWriter.WriteStream(stream, experiments, new[] { result });

            stream.Position = 0;
            var containers = await FTITCReader.ReadStream(stream);
            var restored = Assert.Single(containers.OfType<AnalysisResult>());
            AssertGlobalSequentialRoundTrip(restored, count);
        }

        [Fact]
        public void SequentialOverviewAndClipboardChooseUnitsIndependentlyPerAffinity()
        {
            var (_, result) = CreateGlobalResult(4);

            Assert.Equal(ConcentrationUnit.nM,
                result.GetAppropriateAffinityUnit(ParameterType.Affinity1));
            Assert.Equal(ConcentrationUnit.µM,
                result.GetAppropriateAffinityUnit(ParameterType.Affinity4));
            Assert.Equal(result.GetAppropriateAffinityUnit(ParameterType.Affinity1),
                result.AppropriateAffinityUnit);

            var table = AnalysisResultOverviewTable.Build(result, EnergyUnit.KiloJoule, useKelvin: false);
            Assert.Contains(table.Columns, column =>
                column.Parameter == ParameterType.Affinity1 && column.Title.Contains("nM"));
            Assert.Contains(table.Columns, column =>
                column.Parameter == ParameterType.Affinity4 && column.Title.Contains("µM"));
            Assert.Contains(table.Columns, column =>
                column.Parameter == ParameterType.Gibbs1 && column.Title.StartsWith("∆G1"));
            Assert.Contains(table.Columns, column =>
                column.Parameter == ParameterType.EntropyContribution1 && column.Title.StartsWith("-T∆S1"));

            var clipboard = new RecordingClipboardService();
            PlatformServices.RegisterClipboardService(clipboard);
            try
            {
                Exporter.CopyToClipboard(result, EnergyUnit.KiloJoule, usekelvin: false);
            }
            finally
            {
                PlatformServices.RegisterClipboardService(null);
            }

            var header = clipboard.Value.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[0];
            Assert.Contains("Kd1 (nM)_value", header);
            Assert.Contains("Kd4 (µM)_value", header);
            Assert.Contains("∆G1", header);
            Assert.Contains("-T∆S1", header);

            var exported = AnalysisResultTableExporter.Build(
                new[] { result },
                new AnalysisResultExportOptions());
            var exportedHeader = exported.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[0];
            Assert.Contains("∆G1", exportedHeader);
            Assert.Contains("-T∆S1", exportedHeader);
        }

        [Fact]
        public void SummaryExportKeepsArithmeticMeanOfMemberBestFitsWithSkewedIntervals()
        {
            var (_, result) = CreateGlobalResult(2);
            var members = result.Solution.Solutions;
            members[0].Parameters[ParameterType.Affinity1] = new FloatWithError(7.0, 0.1);
            members[1].Parameters[ParameterType.Affinity1] = new FloatWithError(7.2, 0.1);
            members[0].Parameters[ParameterType.Enthalpy1] = new FloatWithError(10.0, 0.01, 8.0, 50.0);
            members[1].Parameters[ParameterType.Enthalpy1] = new FloatWithError(30.0, 0.01, 28.0, 32.0);

            var clipboard = new RecordingClipboardService();
            var previousUncertaintyStyle = AppSettings.UncertaintyDisplayStyle;
            PlatformServices.RegisterClipboardService(clipboard);
            try
            {
                AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation;
                Exporter.CopyToClipboard(result, EnergyUnit.Joule, usekelvin: false);
            }
            finally
            {
                PlatformServices.RegisterClipboardService(null);
                AppSettings.UncertaintyDisplayStyle = previousUncertaintyStyle;
            }

            var clipboardRows = clipboard.Value.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            var clipboardSummary = clipboardRows.Last();
            var parameterOrder = members[0].ReportParameters.Keys.ToList();

            Assert.StartsWith("mean,", clipboardSummary);
            Assert.Contains(",20 ,", clipboardSummary);

            var table = AnalysisResultTableExporter.Build(
                new[] { result },
                new AnalysisResultExportOptions
                {
                    RowMode = AnalysisResultExportRowMode.Summary,
                    ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
                    FileFormat = AnalysisResultExportFileFormat.TSV,
                    UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation,
                    EnergyUnitOverride = EnergyUnit.Joule,
                });
            var tableSummary = table.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[1].Split('\t');
            var tableValueIndex = 4 + 2 * parameterOrder.IndexOf(ParameterType.Enthalpy1);

            Assert.Equal("20", tableSummary[tableValueIndex]);
        }

        [Fact]
        public void SummaryEvaluationKeepsArithmeticMeanOfMemberBestFitsWithSkewedIntervals()
        {
            var (_, result) = CreateGlobalResult(
                2,
                members =>
                {
                    members[0].Parameters[ParameterType.Enthalpy1] =
                        new FloatWithError(10.0, 0.01, 8.0, 50.0);
                    members[1].Parameters[ParameterType.Enthalpy1] =
                        new FloatWithError(30.0, 0.01, 28.0, 32.0);
                },
                exposeTemperatureDependence: false);
            var dependence = result.Solution.TemperatureDependence[ParameterType.Enthalpy1];

            Assert.False(result.Solution.Model.TemperatureDependenceExposed);
            Assert.Equal(20.0, dependence.Intercept.Value);

            var evaluated = dependence.Evaluate(result.Solution.MeanTemperature, iterations: 1_000);

            Assert.Equal(20.0, evaluated.Value);
            Assert.True(evaluated.SD > 0);
        }

        [Fact]
        public async Task ViewerScalesSequentialAffinitiesIndependently()
        {
            var source = CreateSolvedExperiment(4, includeBootstrap: false);
            using var stream = new MemoryStream();
            await FTXTCWriter.WriteStream(stream, new[] { source });

            stream.Position = 0;
            var document = await new ViewerDocumentReader().ReadAsync(
                stream, "sequential-units.ftxtc", ViewerFileFormat.Ftxtc);
            var fit = Assert.Single(Assert.Single(document.Experiments).Fits);
            var affinity1 = Assert.Single(fit.Parameters,
                parameter => parameter.Key == ParameterType.Affinity1.ToString());
            var affinity4 = Assert.Single(fit.Parameters,
                parameter => parameter.Key == ParameterType.Affinity4.ToString());

            Assert.Equal("nM", affinity1.Unit);
            Assert.Equal("µM", affinity4.Unit);
            Assert.Equal(Math.Pow(10, -6.65) * 1e9, affinity1.Value, 6);
            Assert.Equal(Math.Pow(10, -5.0) * 1e6, affinity4.Value, 6);
        }

        [Fact]
        public async Task ViewerGroupsSequentialConstraintsByFamily()
        {
            var (experiments, result) = CreateGlobalResult(4);
            foreach (var slot in ThermodynamicParameterSlots.Active(4))
            {
                result.Model.Parameters.SetConstraintForParameter(
                    slot.Affinity,
                    VariableConstraint.SameForAll);
                result.Model.Parameters.AddorUpdateGlobalParameter(
                    slot.Affinity,
                    result.Model.Models[0].Parameters.Table[slot.Affinity].Value);
            }
            result.Model.Parameters.SetIndividualFromGlobal();

            using var stream = new MemoryStream();
            await FTXTCWriter.WriteStream(stream, experiments, new[] { result });
            stream.Position = 0;
            var document = await new ViewerDocumentReader().ReadAsync(
                stream,
                "sequential-constraints.ftxtc",
                ViewerFileFormat.Ftxtc);

            var viewerResult = Assert.Single(document.AnalysisResults);
            var constraint = Assert.Single(viewerResult.Constraints);
            Assert.Equal("Affinity", constraint.Label);
            Assert.DoesNotContain(viewerResult.Constraints,
                item => item.Label.Contains("Affinity 2"));
        }

        [Fact]
        public async Task SequentialWireIdsAreStableAndExistingModelsRemainSchemaVersionOne()
        {
            Assert.Equal("sequential-site-count", FtxtcWireIds.Attribute(AttributeKey.SequentialSiteCount));
            Assert.Equal("affinity-log10-3", FtxtcWireIds.Parameter(ParameterType.Affinity3));
            Assert.Equal("affinity-log10-4", FtxtcWireIds.Parameter(ParameterType.Affinity4));
            Assert.Equal("enthalpy-3", FtxtcWireIds.Parameter(ParameterType.Enthalpy3));
            Assert.Equal("enthalpy-4", FtxtcWireIds.Parameter(ParameterType.Enthalpy4));
            Assert.Equal("gibbs-3", FtxtcWireIds.Parameter(ParameterType.Gibbs3));
            Assert.Equal("gibbs-4", FtxtcWireIds.Parameter(ParameterType.Gibbs4));
            Assert.Equal("heat-capacity-3", FtxtcWireIds.Parameter(ParameterType.HeatCapacity3));
            Assert.Equal("heat-capacity-4", FtxtcWireIds.Parameter(ParameterType.HeatCapacity4));
            Assert.Equal("entropy-3", FtxtcWireIds.Parameter(ParameterType.Entropy3));
            Assert.Equal("entropy-4", FtxtcWireIds.Parameter(ParameterType.Entropy4));
            Assert.Equal("entropy-contribution-3", FtxtcWireIds.Parameter(ParameterType.EntropyContribution3));
            Assert.Equal("entropy-contribution-4", FtxtcWireIds.Parameter(ParameterType.EntropyContribution4));

            var sequential = CreateSolvedExperiment(4, includeBootstrap: false);
            var oneSet = CreateOneSetSolvedExperiment();
            using var stream = new MemoryStream();
            await FTXTCWriter.WriteStream(stream, new[] { sequential, oneSet });

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var schemas = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries.Where(entry =>
                entry.FullName.EndsWith("/solution.json", StringComparison.Ordinal)))
            {
                using var reader = new StreamReader(entry.Open());
                var state = JsonNode.Parse(await reader.ReadToEndAsync()).AsObject();
                schemas.Add(state["modelId"].GetValue<string>(), state["modelSchemaVersion"].GetValue<int>());
            }

            Assert.Equal(2, schemas["sequential-binding-sites"]);
            Assert.Equal(1, schemas["one-set-of-sites"]);
        }

        [Theory]
        [InlineData("missing-count")]
        [InlineData("invalid-count")]
        [InlineData("missing-active-parameter")]
        [InlineData("missing-reported-parameter")]
        [InlineData("n-parameter")]
        [InlineData("wrong-model-schema")]
        public async Task MalformedSequentialSolutionFailsStrictAndIsSkippedWithSpecificRecoveryWarning(
            string mutation)
        {
            using var package = await CreateFtxtcPackage(4, includeBootstrap: false);
            using var malformed = RewriteAuthenticatedPackage(package, (path, bytes) =>
            {
                if (!path.EndsWith("/solution.json", StringComparison.Ordinal)) return bytes;
                var state = JsonNode.Parse(bytes).AsObject();
                switch (mutation)
                {
                    case "missing-count":
                        RemoveOption(state, "sequential-site-count");
                        break;
                    case "invalid-count":
                        FindOption(state, "sequential-site-count")["intValue"] = 5;
                        break;
                    case "missing-active-parameter":
                        RemoveParameter(state["fittedParameters"].AsArray(), "affinity-log10-4");
                        break;
                    case "missing-reported-parameter":
                        RemoveParameter(state["reportedParameters"].AsArray(), "enthalpy-4");
                        break;
                    case "n-parameter":
                        state["fittedParameters"].AsArray().Add(new JsonObject
                        {
                            ["id"] = "stoichiometry-1", ["value"] = 1.0, ["locked"] = false,
                        });
                        break;
                    case "wrong-model-schema":
                        state["modelSchemaVersion"] = 1;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
                return Encoding.UTF8.GetBytes(state.ToJsonString(FTXTCFormat.JsonOptions));
            });

            await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(malformed));

            malformed.Position = 0;
            var recovered = await FTXTCReader.ReadWithRecovery(
                malformed, FtxtcReadPolicy.RecoverUsableContent);
            var experiment = Assert.Single(recovered.Containers.OfType<ExperimentData>());
            Assert.Null(experiment.Model);
            var issue = Assert.Single(recovered.Issues,
                candidate => candidate.Code == "sequential-solution-skipped");
            Assert.Equal(FtxtcIssueSeverity.Warning, issue.Severity);
        }

        [Fact]
        public async Task MalformedSequentialBootstrapFailsStrictAndRecoveryKeepsPrimaryWithoutBootstrap()
        {
            using var package = await CreateFtxtcPackage(4, includeBootstrap: true);
            using var malformed = RewriteAuthenticatedPackage(package, (path, bytes) =>
            {
                if (!path.EndsWith("/bootstrap.json", StringComparison.Ordinal)) return bytes;
                var state = JsonNode.Parse(bytes).AsObject();
                FindOption(state["replicates"][0].AsObject(), "sequential-site-count")["intValue"] = 2;
                return Encoding.UTF8.GetBytes(state.ToJsonString(FTXTCFormat.JsonOptions));
            });

            await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(malformed));

            malformed.Position = 0;
            var recovered = await FTXTCReader.ReadWithRecovery(
                malformed, FtxtcReadPolicy.RecoverUsableContent);
            var experiment = Assert.Single(recovered.Containers.OfType<ExperimentData>());
            var model = Assert.IsType<SequentialBindingSites>(experiment.Model);
            Assert.Equal(4, model.SiteCount);
            Assert.Empty(experiment.Solution.BootstrapSolutions);
            var issue = Assert.Single(recovered.Issues,
                candidate => candidate.Code == "sequential-bootstrap-omitted");
            Assert.Equal(FtxtcIssueSeverity.Warning, issue.Severity);
        }

        [Theory]
        [InlineData("inconsistent-family")]
        [InlineData("missing-global-coordinate")]
        public async Task MalformedSequentialGlobalShapeFailsStrictAndRecoverySkipsOnlyResult(
            string mutation)
        {
            var (experiments, result) = CreateGlobalResult(4);
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, experiments, new[] { result });
            using var malformed = RewriteAuthenticatedPackage(package, (path, bytes) =>
            {
                if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
                var state = JsonNode.Parse(bytes).AsObject();
                var constraints = state["constraints"].AsArray();
                var globals = state["globalParameters"].AsArray();
                var affinityIds = new[]
                {
                    "affinity-log10-1", "affinity-log10-2",
                    "affinity-log10-3", "affinity-log10-4",
                };
                var count = mutation == "inconsistent-family" ? 1 : affinityIds.Length;
                for (var index = 0; index < count; index++)
                {
                    constraints.Add(new JsonObject
                    {
                        ["parameterId"] = affinityIds[index],
                        ["constraint"] = "same-for-all",
                    });
                    if (mutation == "inconsistent-family" || index < affinityIds.Length - 1)
                    {
                        globals.Add(new JsonObject
                        {
                            ["id"] = affinityIds[index], ["value"] = 6.0 - index,
                            ["locked"] = false,
                        });
                    }
                }
                return Encoding.UTF8.GetBytes(state.ToJsonString(FTXTCFormat.JsonOptions));
            });

            await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(malformed));

            malformed.Position = 0;
            var recovered = await FTXTCReader.ReadWithRecovery(
                malformed, FtxtcReadPolicy.RecoverUsableContent);
            Assert.Equal(2, recovered.Containers.OfType<ExperimentData>().Count());
            Assert.Empty(recovered.Containers.OfType<AnalysisResult>());
            var issue = Assert.Single(recovered.Issues,
                candidate => candidate.Code == "sequential-result-skipped");
            Assert.Equal(FtxtcIssueSeverity.Warning, issue.Severity);
        }

        static async Task<MemoryStream> CreateFtxtcPackage(int count, bool includeBootstrap)
        {
            var stream = new MemoryStream();
            await FTXTCWriter.WriteStream(stream, new[] { CreateSolvedExperiment(count, includeBootstrap) });
            stream.Position = 0;
            return stream;
        }

        static ExperimentData CreateSolvedExperiment(int count, bool includeBootstrap)
        {
            var data = CreateExperiment($"sequential-{count}.itc");
            var model = CreateSequentialModel(data, count, adjustment: 0);
            var solution = SolutionInterface.FromModel(model, Convergence());
            model.Solution = solution;
            data.UpdateSolution(model);

            if (includeBootstrap)
            {
                var bootstrapModel = CreateSequentialModel(data, count, adjustment: 0.025);
                var bootstrap = SolutionInterface.FromModel(bootstrapModel, Convergence());
                bootstrapModel.Solution = bootstrap;
                bootstrap.BootstrapReplicateIndex = 7;
                solution.SetBootstrapSolutions(new List<SolutionInterface> { bootstrap });
            }

            return data;
        }

        static (List<ExperimentData> experiments, AnalysisResult result) CreateGlobalResult(
            int count,
            Action<List<SolutionInterface>> configureMembers = null,
            bool exposeTemperatureDependence = true)
        {
            var experiments = new List<ExperimentData>
            {
                CreateSolvedExperiment(count, includeBootstrap: false),
                CreateSolvedExperiment(count, includeBootstrap: false),
            };
            experiments[0].MeasuredTemperature = exposeTemperatureDependence ? 20 : 25;
            experiments[1].MeasuredTemperature = exposeTemperatureDependence ? 30 : 25;

            var members = experiments.Select(experiment => experiment.Solution).ToList();
            configureMembers?.Invoke(members);
            var model = new GlobalModel(members.Select(member => member.Model).ToList())
            {
                Parameters = new GlobalModelParameters(),
                ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
            };
            foreach (var member in members)
                model.Parameters.AddIndivdualParameter(member.Model.Parameters);
            var solver = new GlobalSolver
            {
                Model = model,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                UseErrorWeightedFitting = false,
            };
            var solution = new GlobalSolution(solver, members, Convergence());
            model.Solution = solution;
            return (experiments, new AnalysisResult(solution));
        }

        static SequentialBindingSites CreateSequentialModel(
            ExperimentData data,
            int count,
            double adjustment)
        {
            var model = new SequentialBindingSites(data);
            model.InitializeParameters(data);
            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = count;
            model.ApplyModelOptions();
            foreach (var slot in ThermodynamicParameterSlots.Active(count))
            {
                model.Parameters.Table[slot.Affinity].Update(7.2 - 0.55 * slot.Index + adjustment);
                model.Parameters.Table[slot.Enthalpy].Update(-9000 * slot.Index + 100 * adjustment);
            }
            model.Parameters.Table[ParameterType.Offset].Update(130 + adjustment);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            return model;
        }

        static ExperimentData CreateOneSetSolvedExperiment()
        {
            var data = CreateExperiment("one-set-schema.itc");
            var model = new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            var solution = SolutionInterface.FromModel(model, Convergence());
            model.Solution = solution;
            data.UpdateSolution(model);
            return data;
        }

        static ExperimentData CreateExperiment(string fileName)
        {
            var data = new ExperimentData(fileName)
            {
                Name = fileName,
                CellConcentration = new FloatWithError(30e-6),
                SyringeConcentration = new FloatWithError(400e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };
            for (var index = 0; index < 10; index++)
            {
                var volume = index == 0 ? 0.5e-6 : 2e-6;
                var cell = data.CellConcentration.Value * Math.Pow(0.9986, index + 1);
                var ligand = 2.4e-6 * (index + 1);
                var injection = new InjectionData(
                    data, index, volume, data.SyringeConcentration * volume, include: index != 0)
                {
                    ActualCellConcentration = cell,
                    ActualTitrantConcentration = ligand,
                    Ratio = ligand / cell,
                };
                injection.SetPeakArea(new FloatWithError(-1.5e-6 + index * 3e-8));
                data.Injections.Add(injection);
            }
            return data;
        }

        static void AssertSequentialRoundTrip(
            ExperimentData expected,
            ExperimentData actual,
            int count)
        {
            var model = Assert.IsType<SequentialBindingSites>(actual.Model);
            Assert.IsType<SequentialBindingSites.ModelSolution>(actual.Solution);
            Assert.Equal(count, model.SiteCount);
            Assert.Equal(count * 2 + 1, model.Parameters.Table.Count);
            Assert.DoesNotContain(ParameterType.Nvalue1, model.Parameters.Table.Keys);
            Assert.Equal(expected.Model.Parameters.Table.Keys.OrderBy(key => (int)key),
                model.Parameters.Table.Keys.OrderBy(key => (int)key));
            foreach (var parameter in expected.Model.Parameters.Table)
                Assert.Equal(parameter.Value.Value, model.Parameters.Table[parameter.Key].Value, 12);

            var bootstrap = Assert.Single(actual.Solution.BootstrapSolutions);
            var bootstrapModel = Assert.IsType<SequentialBindingSites>(bootstrap.Model);
            Assert.Equal(count, bootstrapModel.SiteCount);
            Assert.Equal(7, bootstrap.BootstrapReplicateIndex);
            Assert.Equal(count * 2 + 1, bootstrapModel.Parameters.Table.Count);
            Assert.DoesNotContain(ParameterType.Nvalue1, bootstrapModel.Parameters.Table.Keys);
        }

        static void AssertGlobalSequentialRoundTrip(AnalysisResult result, int count)
        {
            Assert.Equal(AnalysisModel.SequentialBindingSites, result.Model.ModelType);
            Assert.Equal(2, result.Solution.Solutions.Count);
            Assert.All(result.Model.Models, member =>
            {
                var sequential = Assert.IsType<SequentialBindingSites>(member);
                Assert.Equal(count, sequential.SiteCount);
                Assert.Equal(count * 2 + 1, sequential.Parameters.Table.Count);
                Assert.DoesNotContain(ParameterType.Nvalue1, sequential.Parameters.Table.Keys);
            });
            Assert.All(result.Solution.Solutions,
                solution => Assert.IsType<SequentialBindingSites.ModelSolution>(solution));
        }

        static JsonObject FindOption(JsonObject state, string id) => state["modelOptions"]
            .AsArray().Select(node => node.AsObject())
            .Single(option => option["key"].GetValue<string>() == id);

        static void RemoveOption(JsonObject state, string id)
        {
            var options = state["modelOptions"].AsArray();
            options.Remove(options.Single(node => node["key"].GetValue<string>() == id));
        }

        static void RemoveParameter(JsonArray parameters, string id) =>
            parameters.Remove(parameters.Single(node => node["id"].GetValue<string>() == id));

        static MemoryStream RewriteAuthenticatedPackage(
            Stream source,
            Func<string, byte[], byte[]> transform)
        {
            var items = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            source.Position = 0;
            using (var input = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (var entry in input.Entries.Where(entry =>
                    entry.FullName != FTXTCFormat.ManifestPath))
                {
                    using var entryStream = entry.Open();
                    using var copy = new MemoryStream();
                    entryStream.CopyTo(copy);
                    items.Add(entry.FullName, transform(entry.FullName, copy.ToArray()));
                }
            }

            var manifest = new FtxtcManifest { WriterVersion = "sequential-persistence-test" };
            manifest.Entries = items.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new FtxtcManifestEntry
                {
                    Path = item.Key,
                    MediaType = item.Key.EndsWith(".json", StringComparison.Ordinal)
                        ? "application/json"
                        : "application/x-ftxb",
                    Length = item.Value.LongLength,
                    Sha256 = FTXTCFormat.Sha256(item.Value),
                }).ToList();

            var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, FTXTCFormat.ManifestPath, FTXTCFormat.JsonBytes(manifest));
                foreach (var item in items.OrderBy(item => item.Key, StringComparer.Ordinal))
                    WriteEntry(archive, item.Key, item.Value);
            }
            output.Position = 0;
            return output;
        }

        static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
        {
            var entry = archive.CreateEntry(path);
            using var output = entry.Open();
            output.Write(bytes, 0, bytes.Length);
        }

        static SolverConvergence Convergence() =>
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());

        sealed class RecordingClipboardService : IClipboardService
        {
            public string Value { get; private set; } = string.Empty;
            public void SetString(string value) => Value = value ?? string.Empty;
        }
    }
}
