using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class ViewerDocumentReaderTests
    {
        readonly ViewerDocumentReader reader = new ViewerDocumentReader();

        [Fact]
        public async Task ReadsRawMicroCalFileWithActualSamplesAndInjections()
        {
            var path = Fixture("data_1.itc");
            using var stream = File.OpenRead(path);

            var document = await reader.ReadAsync(stream, "data_1.itc", ViewerFileFormat.Itc);

            var experiment = Assert.Single(document.Experiments);
            Assert.Equal(19, experiment.InjectionCount);
            Assert.NotNull(experiment.Raw);
            Assert.True(experiment.Raw.TimeSeconds.Length > 2_900);
            Assert.Equal(experiment.Raw.TimeSeconds.Length, experiment.Raw.PowerMicrowatts.Length);
            Assert.Equal(experiment.Raw.TimeSeconds.Length, experiment.Raw.TemperatureCelsius.Length);
            Assert.Equal(19, experiment.Raw.InjectionTimesSeconds.Length);
            Assert.Equal(37, experiment.TargetTemperatureCelsius.GetValueOrDefault(), 3);
            Assert.Equal(2_020, experiment.SyringeConcentrationMicromolar.GetValueOrDefault(), 1);
            Assert.Contains(experiment.Metadata, item => item.Value.Contains("°C"));
            Assert.Contains(experiment.Metadata, item => item.Value.Contains("µM"));
            Assert.Contains(experiment.Metadata, item => item.Value.Contains("µL"));
            Assert.Contains("raw", experiment.AvailableViews);
            Assert.DoesNotContain("processed", experiment.AvailableViews);
            Assert.DoesNotContain("fit", experiment.AvailableViews);
        }

        [Fact]
        public async Task ReadsNestedFtitcProjectWithIntegratedProcessedAndFitData()
        {
            using var stream = File.OpenRead(Fixture("one-set.ftitc"));

            var document = await reader.ReadAsync(stream, "data.ftitc", ViewerFileFormat.Ftitc);

            Assert.Equal(new[] { 19, 19, 16 }, document.Experiments.Select(item => item.InjectionCount).ToArray());
            var result = Assert.Single(document.AnalysisResults);
            Assert.Equal(3, result.ExperimentCount);
            Assert.Equal(3, result.Members.Count);
            Assert.Equal(4, result.CorrelationViews.Count);
            Assert.Equal("result-1:correlation-shared", result.CorrelationViews[0].Key);
            Assert.Equal("shared", result.CorrelationViews[0].Scope);
            Assert.Equal(3, result.CorrelationViews.Count(view => view.MemberIndex.HasValue));
            Assert.All(result.CorrelationViews, view => Assert.Equal("Residual bootstrap (Pearson)", view.Method));
            Assert.Contains(result.CorrelationViews, view => view.IsAvailable);
            Assert.All(result.CorrelationViews, view =>
            {
                Assert.False(string.IsNullOrWhiteSpace(view.AvailabilityStatus));
                Assert.Equal(view.IsAvailable, view.CorrelationMatrix != null);
                if (view.CorrelationMatrix != null)
                {
                    Assert.Equal(view.Parameters.Count, view.CorrelationMatrix.Length);
                    Assert.Equal(view.Parameters.Count, view.Parameters.Select(parameter => parameter.Key).Distinct().Count());
                    Assert.All(view.CorrelationMatrix, row => Assert.Equal(view.Parameters.Count, row.Length));
                    Assert.All(view.CorrelationMatrix.SelectMany(row => row), value => Assert.InRange(value, -1.0, 1.0));
                    for (var row = 0; row < view.CorrelationMatrix.Length; row++)
                    {
                        Assert.Equal(1.0, view.CorrelationMatrix[row][row], 12);
                        for (var column = 0; column < view.CorrelationMatrix.Length; column++)
                            Assert.Equal(view.CorrelationMatrix[row][column], view.CorrelationMatrix[column][row], 12);
                    }
                }
            });
            Assert.All(result.CorrelationViews.Where(view => view.MemberIndex.HasValue), view =>
            {
                var member = result.Members[view.MemberIndex.Value];
                Assert.Equal(member.ExperimentKey, view.ExperimentKey);
                Assert.All(view.Parameters.Where(parameter => parameter.Scope == "member"), parameter =>
                {
                    Assert.Equal(view.MemberIndex, parameter.MemberIndex);
                    Assert.Equal(member.ExperimentKey, parameter.ExperimentKey);
                });
            });
            Assert.All(result.Members, member =>
            {
                Assert.False(string.IsNullOrWhiteSpace(member.ExperimentKey));
                Assert.False(string.IsNullOrWhiteSpace(member.FitKey));
                var experiment = Assert.Single(document.Experiments, item => item.Key == member.ExperimentKey);
                var fit = Assert.Single(experiment.Fits, item => item.Key == member.FitKey);
                Assert.Equal(result.Key, fit.ResultKey);
                Assert.Equal(member.Loss, fit.Loss);
            });
            foreach (var experiment in document.Experiments)
            {
                Assert.NotNull(experiment.Raw);
                Assert.NotNull(experiment.Integrated);
                Assert.Equal(experiment.InjectionCount, experiment.Integrated.InjectionNumbers.Length);
                Assert.Equal(experiment.InjectionCount, experiment.Integrated.CorrectedHeatMicrojoules.Length);
                Assert.Contains(experiment.Integrated.RawHeatMicrojoules, item => item.HasValue && Math.Abs(item.Value) > 0);
                Assert.NotNull(experiment.Processed);
                Assert.Equal(experiment.Raw.TimeSeconds.Length, experiment.Processed.CorrectedPowerMicrowatts.Length);
                Assert.Equal(experiment.InjectionCount, experiment.Processed.IntegrationStartSeconds.Length);
                Assert.Equal(experiment.InjectionCount, experiment.Processed.IntegrationEndSeconds.Length);
                Assert.All(
                    experiment.Processed.IntegrationStartSeconds.Zip(experiment.Processed.IntegrationEndSeconds, (start, end) => (start, end)),
                    range => Assert.True(range.end > range.start));
                Assert.NotEmpty(experiment.Fits);
                Assert.All(experiment.Fits, fit =>
                {
                    Assert.Contains(fit.Parameters, item => !item.IsDerived && item.Key == "Offset");
                    Assert.Contains(fit.Parameters, item => item.IsDerived && item.Key == "Gibbs1");
                    Assert.Contains(fit.Parameters, item => item.IsDerived && item.Key == "EntropyContribution1");
                });
                var affinityParameters = experiment.Fits
                    .SelectMany(item => item.Parameters)
                    .Where(item => item.Key.Contains("Affinity"))
                    .ToList();
                Assert.NotEmpty(affinityParameters);
                Assert.All(affinityParameters, item =>
                {
                    Assert.Contains(item.Unit, new[] { "M", "mM", "µM", "nM", "pM", "fM" });
                    Assert.True(double.IsFinite(item.Value));
                });
                Assert.Contains(experiment.Fits.SelectMany(item => item.Parameters), item => item.Unit == "kJ/mol");
                Assert.Contains(experiment.Fits.SelectMany(item => item.FittedKilojoulesPerMole), item => item.HasValue);
                Assert.All(experiment.Fits, fit => Assert.Equal(experiment.InjectionCount, fit.ResidualKilojoulesPerMole.Length));
            }

            var confidenceFit = document.Experiments
                .SelectMany(experiment => experiment.Fits)
                .FirstOrDefault(fit => fit.ConfidenceLowerKilojoulesPerMole != null
                    && fit.ConfidenceUpperKilojoulesPerMole != null
                    && fit.ConfidenceLowerKilojoulesPerMole.Zip(
                        fit.ConfidenceUpperKilojoulesPerMole,
                        (lower, upper) => lower.HasValue && upper.HasValue && upper.Value > lower.Value)
                        .Any(valid => valid));
            Assert.NotNull(confidenceFit);
        }

        [Fact]
        public async Task RestoresIndependentSavedBootstrapModelsForEvaluation()
        {
            using var stream = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(stream);
            var experiment = containers.OfType<ExperimentData>().First();
            var solution = experiment.Solution;

            Assert.NotNull(solution);
            Assert.NotNull(solution.BootstrapSolutions);
            Assert.True(solution.BootstrapSolutions.Count > 1);
            Assert.Equal(
                solution.BootstrapSolutions.Count,
                solution.BootstrapSolutions.Select(item => item.Model).Distinct().Count());

            var primaryEnthalpy = solution.Parameters[ParameterType.Enthalpy1].Value;
            Assert.Equal(-34943.4928996423, primaryEnthalpy, 6);

            var evaluated = solution.Model.EvaluateBootstrap(0, true).DistributionConfidence95;
            Assert.NotNull(evaluated);
            Assert.True(evaluated[0] < evaluated[1]);
        }

        [Fact]
        public async Task PreservesSerializedTandemConcentrationsInLegacyExperimentFiles()
        {
            var lines = File.ReadAllLines(Fixture("one-set.ftitc")).ToList();
            var injectionList = lines.FindIndex(line => line == "LIST:InjectionList");
            Assert.True(injectionList >= 0);

            var firstInjection = lines[injectionList + 1].Split(',');
            firstInjection[9] = "0.000111111";
            firstInjection[10] = "0.000222222";
            lines[injectionList + 1] = string.Join(",", firstInjection);

            var injectionListEnd = lines.FindIndex(injectionList + 1, line => line == "ENDLIST");
            lines.InsertRange(injectionListEnd + 1, new[]
            {
                "LIST:SegmentList",
                "0,0.000111111,0",
                "10,0.0001,0.0001",
                "ENDLIST"
            });

            using var stream = TextStream(string.Join("\n", lines));
            var document = await reader.ReadAsync(stream, "legacy-tandem.ftitc", ViewerFileFormat.Ftitc);

            var experiment = document.Experiments[0];
            Assert.NotNull(experiment.Integrated);
            Assert.Equal(111.111, experiment.Integrated.CellConcentrationMicromolar[0], 6);
            Assert.Equal(222.222, experiment.Integrated.TitrantConcentrationMicromolar[0], 6);
            Assert.Equal(2.0, experiment.Integrated.AnalysisX[0], 6);
            Assert.All(experiment.Fits, fit => Assert.Equal(2.0, fit.X[0], 6));
        }

        [Fact]
        public async Task ProjectsSavedParameterLocksSeparatelyFromDerivedParameters()
        {
            // Git commonly checks text fixtures out with CRLF on Windows. Exercise
            // that form explicitly so the lock insertion cannot silently miss it.
            var project = File.ReadAllText(Fixture("one-set.ftitc")).ReplaceLineEndings("\r\n");
            var originalProject = project;
            project = Regex.Replace(
                project,
                "(?m)^(Nvalue1:[0-9]+:[^:\\r\\n]+)(?=\\r?$)",
                "$1:1",
                RegexOptions.None,
                TimeSpan.FromSeconds(1));
            Assert.NotEqual(originalProject, project);
            using var stream = TextStream(project);

            var document = await reader.ReadAsync(stream, "locked.ftitc", ViewerFileFormat.Ftitc);

            Assert.Contains(
                document.Experiments.SelectMany(experiment => experiment.Fits).SelectMany(fit => fit.Parameters),
                parameter => parameter.Key == "Nvalue1" && parameter.IsLocked && !parameter.IsDerived);
            Assert.All(
                document.Experiments.SelectMany(experiment => experiment.Fits).SelectMany(fit => fit.Parameters).Where(parameter => parameter.IsDerived),
                parameter => Assert.False(parameter.IsLocked));
        }

        [Fact]
        public async Task ProjectsDistinctSavedResultsInNewestFirstOrder()
        {
            using var stream = File.OpenRead(Fixture("jors.ftxtc"));
            var document = await reader.ReadAsync(stream, "jors.ftxtc", ViewerFileFormat.Ftxtc);

            Assert.Equal(2, document.AnalysisResults.Count);
            Assert.Equal(document.AnalysisResults.OrderByDescending(item => item.Date).Select(item => item.Key),
                document.AnalysisResults.Select(item => item.Key));
            Assert.Equal(2, document.AnalysisResults.Select(item => item.Key).Distinct().Count());
            Assert.All(document.AnalysisResults, item => Assert.Matches("^result-[0-9]+$", item.Key));
            Assert.Contains(document.AnalysisResults, item => item.IsGlobal);
            Assert.Contains(document.AnalysisResults, item => !item.IsGlobal);
            Assert.All(document.AnalysisResults, result =>
            {
                Assert.Equal(3, result.Members.Count);
                Assert.False(string.IsNullOrWhiteSpace(result.ModelName));
                Assert.NotNull(result.Solver);
                Assert.False(string.IsNullOrWhiteSpace(result.Solver.Algorithm));
                Assert.NotNull(result.Solver.Iterations);
                Assert.NotNull(result.Validity);
                Assert.Contains(result.Validity.Status, new[] { "valid", "partialInvalid", "invalid", "unknown" });
                Assert.All(result.CorrelationViews, view => Assert.StartsWith(result.Key + ":correlation-", view.Key));
                Assert.All(result.Members, member =>
                {
                    var experiment = Assert.Single(document.Experiments, item => item.Key == member.ExperimentKey);
                    var fit = Assert.Single(experiment.Fits, item => item.Key == member.FitKey);
                    Assert.Equal(result.Key, fit.ResultKey);
                    Assert.StartsWith(result.Key + ":member-", fit.Key);
                    Assert.NotNull(member.Loss);
                });
            });
            Assert.All(document.AnalysisResults.Where(item => item.IsGlobal), item => Assert.NotEmpty(item.Constraints));
            Assert.Contains(document.AnalysisResults, item => item.ModelOptions.Count > 0);
            Assert.Contains(document.AnalysisResults, item => item.Solver.WeightedFitting);
            Assert.Contains(document.AnalysisResults, item => item.Solver.BootstrapIterations > 0);
        }

        [Theory]
        [InlineData("two-sites.ftxtc", true)]
        [InlineData("competitive.ftxtc", false)]
        public async Task ProjectsModelSpecificResultMembersWithoutCrossResultCollapse(string fixture, bool expectSecondSite)
        {
            using var stream = File.OpenRead(Fixture(fixture));
            var document = await reader.ReadAsync(stream, fixture, ViewerFileFormat.Ftxtc);

            Assert.NotEmpty(document.AnalysisResults);
            Assert.All(document.AnalysisResults.SelectMany(item => item.Members), member =>
            {
                var experiment = Assert.Single(document.Experiments, item => item.Key == member.ExperimentKey);
                var fit = Assert.Single(experiment.Fits, item => item.Key == member.FitKey);
                Assert.NotEmpty(fit.Parameters);
                Assert.Equal(document.AnalysisResults.Single(item => item.Key == fit.ResultKey).Key, fit.ResultKey);
            });
            if (expectSecondSite)
                Assert.Contains(document.Experiments.SelectMany(item => item.Fits).SelectMany(item => item.Parameters),
                    item => item.Key.EndsWith("2", StringComparison.Ordinal));
        }

        [Fact]
        public async Task EmbeddedTemperatureSeriesFitsAreNotSynthesizedIntoResults()
        {
            using var stream = File.OpenRead(Fixture("temperature-series.ftxtc"));
            var document = await reader.ReadAsync(stream, "temperature-series.ftxtc", ViewerFileFormat.Ftxtc);

            Assert.Empty(document.AnalysisResults);
            Assert.All(document.Experiments, experiment =>
            {
                Assert.NotEmpty(experiment.Fits);
                Assert.All(experiment.Fits, fit => Assert.Null(fit.ResultKey));
            });
        }

        [Theory]
        [InlineData("temperature-series.ftxtc", 4)]
        [InlineData("jors.ftxtc", 3)]
        [InlineData("two-sites.ftxtc", 2)]
        [InlineData("competitive.ftxtc", 5)]
        public async Task ReadsRepresentativeProjectModels(string fixture, int experimentCount)
        {
            using var stream = File.OpenRead(Fixture(fixture));
            var document = await reader.ReadAsync(stream, fixture, ViewerFileFormat.Ftxtc);

            Assert.Equal(experimentCount, document.Experiments.Count);
            Assert.All(document.Experiments, item => Assert.NotNull(item.Raw));
            Assert.Contains(document.Experiments, item => item.Fits.Count > 0);
        }

        [Fact]
        public async Task ValidFtitcWithoutProcessingOrFitMarksViewsUnavailable()
        {
            using var stream = TextStream(MinimalFtitc);
            var document = await reader.ReadAsync(stream, "minimal.ftitc", ViewerFileFormat.Ftitc);

            var experiment = Assert.Single(document.Experiments);
            Assert.Equal(1, experiment.InjectionCount);
            Assert.Equal("Shown in the dedicated comment box", experiment.Comments);
            Assert.DoesNotContain(experiment.Metadata, item => item.Label == "Comments");
            Assert.Contains("raw", experiment.AvailableViews);
            Assert.DoesNotContain("integrated", experiment.AvailableViews);
            Assert.DoesNotContain("processed", experiment.AvailableViews);
            Assert.DoesNotContain("fit", experiment.AvailableViews);
        }

        [Fact]
        public async Task MissingOptionalTemperatureChannelIsReported()
        {
            using var stream = TextStream(MinimalFtitc.Replace(",25,25", ",NaN,25"));
            var document = await reader.ReadAsync(stream, "missing-temperature.ftitc", ViewerFileFormat.Ftitc);

            var raw = Assert.Single(document.Experiments).Raw;
            Assert.Null(raw.TemperatureCelsius);
            Assert.Contains(raw.UnavailableChannels, message => message.Contains("Temperature"));
        }

        [Fact]
        public async Task RejectsMismatchedAndMalformedFilesWithSafeCodes()
        {
            using var mismatch = TextStream("$ITC\n$ 1\n");
            var mismatchError = await Assert.ThrowsAsync<ViewerFileException>(() =>
                reader.ReadAsync(mismatch, "wrong.ftitc", ViewerFileFormat.Ftitc));
            Assert.Equal("format_mismatch", mismatchError.Code);

            using var malformed = TextStream("FTITCVersion:1.1\nFILE:Experiment:broken.itc\nLIST:InjectionList\nnot,a,valid,row\n");
            var malformedError = await Assert.ThrowsAsync<ViewerFileException>(() =>
                reader.ReadAsync(malformed, "broken.ftitc", ViewerFileFormat.Ftitc));
            Assert.Equal("malformed_file", malformedError.Code);

            using var incompleteRaw = TextStream("$ITC\n$ 1\n");
            var rawError = await Assert.ThrowsAsync<ViewerFileException>(() =>
                reader.ReadAsync(incompleteRaw, "broken.itc", ViewerFileFormat.Itc));
            Assert.Equal("malformed_file", rawError.Code);

            using var missingEndFile = TextStream(MinimalFtitc.Replace("ENDFILE", string.Empty));
            var endFileError = await Assert.ThrowsAsync<ViewerFileException>(() =>
                reader.ReadAsync(missingEndFile, "missing-end-file.ftitc", ViewerFileFormat.Ftitc));
            Assert.Equal("malformed_file", endFileError.Code);

            var lastEndList = MinimalFtitc.LastIndexOf("ENDLIST", StringComparison.Ordinal);
            using var truncatedList = TextStream(MinimalFtitc.Substring(0, lastEndList));
            var endListError = await Assert.ThrowsAsync<ViewerFileException>(() =>
                reader.ReadAsync(truncatedList, "missing-end-list.ftitc", ViewerFileFormat.Ftitc));
            Assert.Equal("malformed_file", endListError.Code);
        }

        [Fact]
        public async Task ConcurrentReadsDoNotShareProjectState()
        {
            async Task<ViewerDocument> Read(string fixture, ViewerFileFormat format)
            {
                using var stream = File.OpenRead(Fixture(fixture));
                return await new ViewerDocumentReader().ReadAsync(stream, fixture, format);
            }

            var documents = await Task.WhenAll(
                Read("one-set.ftitc", ViewerFileFormat.Ftitc),
                Read("temperature-series.ftxtc", ViewerFileFormat.Ftxtc));

            Assert.Equal(3, documents[0].Experiments.Count);
            Assert.Equal(4, documents[1].Experiments.Count);
            Assert.DoesNotContain(documents[0].Experiments.Select(item => item.Name),
                name => documents[1].Experiments.Any(item => item.Name == name));
        }

        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        static MemoryStream TextStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

        const string MinimalFtitc = """
FTITCVersion:1.1
FILE:Experiment:minimal.itc
Name:Minimal
ID:minimal-id
Date:2026-01-01T00:00:00.0000000Z
Source:0
Comments:Shown in the dedicated comment box
Include:1
SyringeConcentration:0.001,0
CellConcentration:0.0001,0
CellVolume:0.0002
StirringSpeed:750
TargetTemperature:25
MeasuredTemperature:25
InitialDelay:10
TargetPowerDiff:10
FeedBackMode:1
Instrument:1
LIST:InjectionList
0,0,1,0.000001,10,1,25,0,8
ENDLIST
LIST:DataPointList
0,0,25,25
1,0.000001,25,25
2,0,25,25
ENDLIST
ENDFILE
""";
    }
}
