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
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class NullComparisonPersistenceTests
{
    [Theory]
    [InlineData(DilutionMethod.MicroCal, "legacy", 1, false, false)]
    [InlineData(DilutionMethod.Exponential, "dumas-simpson", 2, false, false)]
    [InlineData(DilutionMethod.DiscreteDisplacement, "pytc-discrete", 3, false, false)]
    [InlineData(DilutionMethod.MicroCal, "legacy", 1, true, false)]
    [InlineData(DilutionMethod.Exponential, "dumas-simpson", 2, true, false)]
    [InlineData(DilutionMethod.DiscreteDisplacement, "pytc-discrete", 3, true, false)]
    [InlineData(DilutionMethod.MicroCal, "legacy", 1, false, true)]
    [InlineData(DilutionMethod.Exponential, "dumas-simpson", 2, false, true)]
    [InlineData(DilutionMethod.DiscreteDisplacement, "pytc-discrete", 3, false, true)]
    [InlineData(DilutionMethod.MicroCal, "legacy", 1, true, true)]
    [InlineData(DilutionMethod.Exponential, "dumas-simpson", 2, true, true)]
    [InlineData(DilutionMethod.DiscreteDisplacement, "pytc-discrete", 3, true, true)]
    public async Task SavedNullHeatMethodMetadataSurvivesReopening(
        DilutionMethod method, string heatMethod, int modelSchema, bool includeResult, bool reprocessExperiment)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false, method: method);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        Assert.Single(model.Solution.NullComparison.NullSolutions).SetID("saved-null-fit/" + heatMethod);
        model.Data.UpdateSolution(model);
        var result = includeResult ? CreateResult(model) : null;
        result?.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        var expectedComparison = model.Solution.NullComparison;
        var expectedAssessment = result?.BindingAssessment;
        var expected = new Dictionary<string, (string, int)> { [model.Data.UniqueID] = (heatMethod, modelSchema) };
        using var initial = new MemoryStream();
        await FTXTCWriter.WriteStream(initial, new[] { model.Data },
            result == null ? Array.Empty<AnalysisResult>() : new[] { result });
        AssertSavedNullHeatMethods(initial, expected);
        var bytes = initial.ToArray();

        for (var generation = 0; generation < 2; generation++)
        {
            using var input = new MemoryStream(bytes);
            var restored = await FTXTCReader.ReadWithRecovery(input, FtxtcReadPolicy.Strict);
            Assert.Empty(restored.Issues);
            var experiment = Assert.Single(restored.Containers.OfType<ExperimentData>());
            if (reprocessExperiment && generation == 0)
            {
                RawDataReader.ReprocessInjections(experiment, method == DilutionMethod.MicroCal
                    ? DilutionMethod.DiscreteDisplacement : DilutionMethod.MicroCal);
                // These result objects are outside DataManager, which normally invalidates them.
                foreach (var savedResult in restored.Containers.OfType<AnalysisResult>()) savedResult.Solution.Invalidate();
            }
            using var resaved = new MemoryStream();
            await FTXTCWriter.WriteStream(resaved, restored.Containers.OfType<ExperimentData>(),
                restored.Containers.OfType<AnalysisResult>());
            AssertSavedNullHeatMethods(resaved, expected);
            FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(resaved);
            Assert.Equal(model.HeatMethod, experiment.Solution.Model.HeatMethod);
            if (reprocessExperiment) Assert.NotEqual(model.HeatMethod, experiment.HeatMethod);
            else Assert.Equal(model.Data.HeatMethod, experiment.HeatMethod);
            Assert.Equal(!reprocessExperiment, experiment.Solution.IsValid);
            AssertComparisonEvidence(expectedComparison, experiment.Solution.NullComparison);
            AssertNullSolutionIds(expectedComparison, experiment.Solution.NullComparison);
            Assert.NotSame(experiment, Assert.Single(experiment.Solution.NullComparison.NullSolutions).Data);
            if (includeResult)
            {
                var restoredResult = Assert.Single(restored.Containers.OfType<AnalysisResult>());
                AssertComparisonEvidence(expectedComparison, restoredResult.NullComparison);
                AssertNullSolutionIds(expectedComparison, restoredResult.NullComparison);
                Assert.Equivalent(expectedAssessment, restoredResult.BindingAssessment, strict: true);
                Assert.False(restoredResult.IsModified);
                Assert.NotSame(restoredResult.Solution.Solutions[0].Data,
                    Assert.Single(restoredResult.NullComparison.NullSolutions).Data);
            }
            bytes = resaved.ToArray();
        }
    }

    static void AssertSavedNullHeatMethods(MemoryStream package,
        IReadOnlyDictionary<string, (string HeatMethod, int ModelSchema)> expected)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var count = 0;
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith("/solution.json", StringComparison.Ordinal)
                     || entry.FullName.EndsWith("/result.json", StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            var comparison = JsonNode.Parse(stream)["nullComparison"];
            if (comparison == null) continue;
            foreach (var member in comparison["members"].AsArray())
            {
                var saved = expected[member["experimentId"].GetValue<string>()];
                Assert.Equal(saved, (member["solution"]["heatMethod"].GetValue<string>(),
                    member["solution"]["modelSchemaVersion"].GetValue<int>()));
                count++;
            }
        }
        Assert.True(count > 0, "The package must contain an embedded Null solution.");
    }

    [Theory]
    [InlineData("single", SolverTermination.IterationLimit)]
    [InlineData("independent", SolverTermination.EvaluationLimit)]
    [InlineData("pooled", SolverTermination.TimeLimit)]
    public async Task LimitTerminatedComparisonAndAssessmentSurviveStrictReopening(
        string scope, SolverTermination termination)
    {
        var models = Enumerable.Range(0, scope == "single" ? 1 : 2)
            .Select(_ => InjectionProcessingMethodTests.FittedModel(bootstrap: false)).ToArray();
        for (var index = 0; index < models.Length; index++)
        {
            var model = models[index];
            foreach (var injection in model.Data.Injections)
                injection.SetPeakArea(new FloatWithError(injection.PeakArea.Value + 1e-8 * (1 + injection.ID % 3)));
            model.Parameters.Table[ParameterType.Offset].SetValue(0, false);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(
                new SolverConvergenceSnapshot
                {
                    Algorithm = SolverAlgorithm.LevenbergMarquardt,
                    Termination = index == 0 ? termination : SolverTermination.Converged,
                }));
        }

        AnalysisResult result;
        if (scope == "single")
        {
            NullModelComparisonCalculator.Calculate(models[0].Solution, false,
                SolverAlgorithm.LevenbergMarquardt, 3000, 1);
            result = CreateResult(models[0]);
        }
        else
        {
            var globalModel = new GlobalModel(models.ToList()) { Parameters = new GlobalModelParameters() };
            foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
            if (scope == "pooled")
            {
                globalModel.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
                globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0);
                globalModel.Parameters.SetIndividualFromGlobal();
            }
            var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel },
                models.Select(model => model.Solution).ToList(), models[0].Solution.Convergence,
                reconstructBootstrap: false);
            globalModel.Solution = globalSolution;
            NullModelComparisonCalculator.Calculate(globalSolution, false,
                SolverAlgorithm.LevenbergMarquardt, 3000, 1);
            result = new AnalysisResult(globalSolution);
        }
        foreach (var model in models) model.Data.UpdateSolution(model);
        var expected = result.NullComparison;
        var expectedMembers = models.ToDictionary(model => model.Data.UniqueID, model => model.Solution.NullComparison);
        Assert.False(expected.BindingFitSucceeded);
        Assert.NotNull(expected.DeltaAicc);
        Assert.Equal(BindingAssessmentSummaryOutcome.BindingDetected, result.CollectionAssessmentOutcome);
        Assert.Equal(scope == "independent", result.IsIndependentAssessmentCollection);
        var experiments = models.Select(model => model.Data).ToArray();

        for (var generation = 0; generation < 2; generation++)
        {
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, experiments, new[] { result });
            FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(package);
            package.Position = 0;
            var restored = await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict);
            Assert.Empty(restored.Issues);
            result = Assert.Single(restored.Containers.OfType<AnalysisResult>());
            experiments = restored.Containers.OfType<ExperimentData>().ToArray();

            AssertComparisonEvidence(expected, result.NullComparison);
            Assert.False(result.Solution.Convergence.Success);
            Assert.Equal(termination, result.Solution.Convergence.Termination);
            Assert.Equal(BindingAssessmentSummaryOutcome.BindingDetected, result.CollectionAssessmentOutcome);
            Assert.Equal(scope == "independent", result.IsIndependentAssessmentCollection);
            Assert.All(result.MemberAssessments, member =>
                Assert.Equal(BindingAssessmentOutcome.BindingDetected, member.Assessment.AutomaticOutcome));
            Assert.False(result.IsModified);
            foreach (var member in result.Solution.Solutions)
            {
                AssertComparisonEvidence(expectedMembers[member.Data.UniqueID], member.NullComparison);
                var expectedTermination = member.Data.UniqueID == models[0].Data.UniqueID
                    ? termination : SolverTermination.Converged;
                Assert.Equal(expectedTermination, member.Convergence.Termination);
            }
            foreach (var experiment in experiments)
                AssertComparisonEvidence(expectedMembers[experiment.UniqueID], experiment.Solution.NullComparison);
        }
    }

    [Theory]
    [InlineData(DilutionMethod.MicroCal, false)]
    [InlineData(DilutionMethod.MicroCal, true)]
    [InlineData(DilutionMethod.Exponential, false)]
    [InlineData(DilutionMethod.Exponential, true)]
    [InlineData(DilutionMethod.DiscreteDisplacement, false)]
    [InlineData(DilutionMethod.DiscreteDisplacement, true)]
    public async Task ExperimentWithoutAnalysisResultRetainsNullComparisonAfterReopening(DilutionMethod method, bool weighted)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false, method: method);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromFixedFit(0, 0));
        model.Solution.UseWeightedFitting = weighted;
        NullModelComparisonCalculator.Calculate(model.Solution, weighted,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        model.Data.UpdateSolution(model);
        var expected = model.Solution.NullComparison;
        Assert.True(expected.NullFitSucceeded, expected.NullFitReason);
        Assert.NotNull(expected.DeltaAicc);

        // Reopening must use the captured evidence, even after the experiment is edited.
        model.Data.Injections[0].SetPeakArea(new FloatWithError(model.Data.Injections[0].PeakArea.Value + 5e-6));
        var experiment = model.Data;
        for (var generation = 0; generation < 2; generation++)
        {
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { experiment });
            FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(package);
            package.Position = 0;
            experiment = Assert.IsType<ExperimentData>(Assert.Single(await FTXTCReader.ReadStream(package)));

            Assert.Equal(model.Solution.Guid, experiment.Solution.Guid);
            Assert.Equal(model.Solution.Parameters, experiment.Solution.Parameters);
            AssertComparisonEvidence(expected, experiment.Solution.NullComparison);
            AssertNullSolutionIds(expected, experiment.Solution.NullComparison);
            Assert.NotSame(experiment, experiment.Solution.NullComparison.NullSolutions.Single().Data);
        }
    }

    [Theory]
    [InlineData(false, false, false, FtxtcReadPolicy.Strict)]
    [InlineData(false, true, false, FtxtcReadPolicy.Strict)]
    [InlineData(true, false, false, FtxtcReadPolicy.Strict)]
    [InlineData(true, true, false, FtxtcReadPolicy.Strict)]
    [InlineData(false, false, true, FtxtcReadPolicy.Strict)]
    [InlineData(false, false, true, FtxtcReadPolicy.RecoverUsableContent)]
    [InlineData(true, false, true, FtxtcReadPolicy.Strict)]
    [InlineData(true, false, true, FtxtcReadPolicy.RecoverUsableContent)]
    public async Task MultiExperimentComparisonsRetainScopeAndHeatMethods(
        bool pooled, bool selectedOnly, bool includeResult, FtxtcReadPolicy policy)
    {
        var globalSolution = CreateMixedMethodComparison(pooled);
        var models = globalSolution.Model.Models;
        var result = includeResult ? new AnalysisResult(globalSolution) : null;
        if (includeResult)
        {
            if (pooled) result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
            else result.SetMemberBindingAssessmentOverride(models[1].Solution.Guid, BindingAssessmentOutcome.NoBindingDetected);
        }
        var expectedAssessments = result?.MemberAssessments.Select(member => member.Assessment).ToArray();
        var expectedAssessment = result?.BindingAssessment;
        var expected = models.ToDictionary(model => model.Data.UniqueID, model => model.Solution.NullComparison);
        var expectedResultComparison = result?.NullComparison;
        var expectedHeatMethods = new Dictionary<string, (string, int)>
        {
            [models[0].Data.UniqueID] = ("legacy", 1),
            [models[1].Data.UniqueID] = ("dumas-simpson", 2),
            [models[2].Data.UniqueID] = ("pytc-discrete", 3),
        };
        var experiments = models.Take(selectedOnly ? 1 : models.Count).Select(model => model.Data).ToArray();

        for (var generation = 0; generation < 2; generation++)
        {
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, experiments,
                result == null ? Array.Empty<AnalysisResult>() : new[] { result });
            AssertSavedNullHeatMethods(package, expectedHeatMethods);
            FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(package);
            package.Position = 0;
            var restored = await FTXTCReader.ReadWithRecovery(package, policy);
            Assert.Empty(restored.Issues);
            if (includeResult)
            {
                result = Assert.Single(restored.Containers.OfType<AnalysisResult>());
                AssertComparisonEvidence(expectedResultComparison, result.NullComparison);
                AssertNullSolutionIds(expectedResultComparison, result.NullComparison);
                Assert.Equivalent(expectedAssessment, result.BindingAssessment, strict: true);
                Assert.Equivalent(expectedAssessments, result.MemberAssessments.Select(member => member.Assessment).ToArray(), strict: true);
                Assert.False(result.IsModified);
                foreach (var member in result.Solution.Solutions)
                {
                    AssertComparisonEvidence(expected[member.Data.UniqueID], member.NullComparison);
                    AssertNullSolutionIds(expected[member.Data.UniqueID], member.NullComparison);
                    Assert.NotSame(member.Data, Assert.Single(result.NullComparison.NullSolutions,
                        solution => solution.Data.UniqueID == member.Data.UniqueID).Data);
                }
            }
            else Assert.DoesNotContain(restored.Containers, container => container is AnalysisResult);
            experiments = restored.Containers.OfType<ExperimentData>().ToArray();
            Assert.Equal(selectedOnly ? 1 : models.Count, experiments.Length);
            foreach (var experiment in experiments)
            {
                var comparison = experiment.Solution.NullComparison;
                AssertComparisonEvidence(expected[experiment.UniqueID], comparison);
                AssertNullSolutionIds(expected[experiment.UniqueID], comparison);
                Assert.Equal(!pooled, comparison.IsIndependentMemberComparison);
                Assert.Equal(pooled ? models.Count : 1, comparison.Members.Count);
                Assert.NotSame(experiment, Assert.Single(comparison.NullSolutions,
                    solution => solution.Data.UniqueID == experiment.UniqueID).Data);
            }
        }
    }

    static GlobalSolution CreateMixedMethodComparison(bool pooled)
    {
        var models = new[] { DilutionMethod.MicroCal, DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement }
            .Select(method => InjectionProcessingMethodTests.FittedModel(bootstrap: false, method: method)).ToArray();
        var globalModel = new GlobalModel(models.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var model in models)
        {
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromFixedFit(0, 0));
            globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        }
        if (pooled)
        {
            globalModel.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
            globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0);
            globalModel.Parameters.SetIndividualFromGlobal();
        }
        var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel },
            models.Select(model => model.Solution).ToList(), SolverConvergence.FromFixedFit(0, 0), reconstructBootstrap: false);
        globalModel.Solution = globalSolution;
        NullModelComparisonCalculator.Calculate(globalSolution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        foreach (var model in models) model.Data.UpdateSolution(model);
        return globalSolution;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MissingOrDefaultedEmbeddedMetadataUsesPairedBindingConvention(bool includeResult, bool missingSolution)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false, method: DilutionMethod.Exponential);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        model.Data.UpdateSolution(model);
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data },
            includeResult ? new[] { CreateResult(model) } : Array.Empty<AnalysisResult>());
        using var historical = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/solution.json", StringComparison.Ordinal)
                && !path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes);
            if (node["nullComparison"] is not { } comparison) return bytes;
            foreach (var member in comparison["members"].AsArray())
            {
                if (missingSolution) member.AsObject().Remove("solution");
                else
                {
                    member["solution"]["heatMethod"] = "legacy";
                    member["solution"]["modelSchemaVersion"] = 1;
                }
            }
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);
        historical.Position = 0;
        var restored = await FTXTCReader.ReadWithRecovery(historical, FtxtcReadPolicy.Strict);
        Assert.Empty(restored.Issues);
        var experiment = Assert.Single(restored.Containers.OfType<ExperimentData>());
        AssertComparisonEvidence(model.Solution.NullComparison, experiment.Solution.NullComparison);
        if (includeResult)
            AssertComparisonEvidence(model.Solution.NullComparison,
                Assert.Single(restored.Containers.OfType<AnalysisResult>()).NullComparison);
        using var resaved = new MemoryStream();
        await FTXTCWriter.WriteStream(resaved, restored.Containers.OfType<ExperimentData>(),
            restored.Containers.OfType<AnalysisResult>());
        AssertSavedNullHeatMethods(resaved, new Dictionary<string, (string, int)>
        {
            [model.Data.UniqueID] = ("dumas-simpson", 2),
        });
    }

    [Theory]
    [InlineData("solution")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    public async Task MissingEmbeddedNullIdsAreGeneratedAndRetainedAfterSaving(string missingState)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        model.Data.UpdateSolution(model);
        using var package = await Write(model.Data, CreateResult(model));
        using var historical = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/solution.json", StringComparison.Ordinal)
                && !path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes);
            if (node["nullComparison"] is not { } comparison) return bytes;
            foreach (var member in comparison["members"].AsArray())
            {
                if (missingState == "solution") member.AsObject().Remove("solution");
                else if (missingState == "missing") member["solution"].AsObject().Remove("id");
                else member["solution"]["id"] = missingState switch
                {
                    "empty" => "",
                    "whitespace" => " \t",
                    _ => null,
                };
            }
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);
        historical.Position = 0;
        var restored = await FTXTCReader.ReadWithRecovery(historical, FtxtcReadPolicy.Strict);
        Assert.Empty(restored.Issues);
        var experiment = Assert.Single(restored.Containers.OfType<ExperimentData>());
        var result = Assert.Single(restored.Containers.OfType<AnalysisResult>());
        var expectedAttached = experiment.Solution.NullComparison;
        var expectedResult = result.NullComparison;
        foreach (var comparison in new[] { expectedAttached, expectedResult })
        {
            AssertComparisonEvidence(model.Solution.NullComparison, comparison);
            var solution = Assert.Single(comparison.NullSolutions);
            Assert.False(string.IsNullOrWhiteSpace(solution.Guid));
            Assert.NotEqual(model.Solution.Guid, solution.Guid);
        }

        for (var generation = 0; generation < 2; generation++)
        {
            using var resaved = await Write(experiment, result);
            FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(resaved);
            resaved.Position = 0;
            restored = await FTXTCReader.ReadWithRecovery(resaved, FtxtcReadPolicy.Strict);
            Assert.Empty(restored.Issues);
            experiment = Assert.Single(restored.Containers.OfType<ExperimentData>());
            result = Assert.Single(restored.Containers.OfType<AnalysisResult>());
            AssertComparisonEvidence(expectedAttached, experiment.Solution.NullComparison);
            AssertNullSolutionIds(expectedAttached, experiment.Solution.NullComparison);
            AssertComparisonEvidence(expectedResult, result.NullComparison);
            AssertNullSolutionIds(expectedResult, result.NullComparison);
        }
    }

    [Fact]
    public async Task MissingBindingAndEmbeddedSolutionRetainsHistoricalConvention()
    {
        var fitted = CreateMixedMethodComparison(pooled: true);
        var selected = fitted.Solutions[1].Data;
        var expected = selected.Solution.NullComparison;
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { selected });
        using var historical = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/solution.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes);
            foreach (var member in node["nullComparison"]["members"].AsArray())
                if (member["experimentId"].GetValue<string>() != selected.UniqueID)
                    member.AsObject().Remove("solution");
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);
        historical.Position = 0;
        var restored = await FTXTCReader.ReadWithRecovery(historical, FtxtcReadPolicy.Strict);
        Assert.Empty(restored.Issues);
        var experiment = Assert.IsType<ExperimentData>(Assert.Single(restored.Containers));
        var comparison = experiment.Solution.NullComparison;
        Assert.All(comparison.NullSolutions, solution =>
        {
            var expectedMethod = solution.Data.UniqueID == selected.UniqueID
                ? InjectionHeatMethod.IdealContinuousMixing : InjectionHeatMethod.MicroCal;
            Assert.Equal(expectedMethod, solution.Model.HeatMethod);
            Assert.Equal(expectedMethod, solution.Data.HeatMethod);
        });
        Assert.Equal(expected.Members.Select(member => (member.ExperimentId, member.Offset, member.Scope)),
            comparison.Members.Select(member => (member.ExperimentId, member.Offset, member.Scope)));
        Assert.Equal(expected.Members.SelectMany(member => member.Points).Select(point => (point.InjectionId,
                point.InjectionMass, point.Ratio, point.ObservedHeatJoules, point.PredictedHeatJoules, point.Included)),
            comparison.Members.SelectMany(member => member.Points).Select(point => (point.InjectionId,
                point.InjectionMass, point.Ratio, point.ObservedHeatJoules, point.PredictedHeatJoules, point.Included)));
        Assert.Equivalent(expected.BindingInformationCriteria, comparison.BindingInformationCriteria, strict: true);
        Assert.Equivalent(expected.NullInformationCriteria, comparison.NullInformationCriteria, strict: true);
        Assert.Equal(expected.DeltaAicc, comparison.DeltaAicc);
        using var resaved = new MemoryStream();
        await FTXTCWriter.WriteStream(resaved, new[] { experiment });
        AssertSavedNullHeatMethods(resaved, expected.Members.ToDictionary(member => member.ExperimentId,
            member => member.ExperimentId == selected.UniqueID ? ("dumas-simpson", 2) : ("legacy", 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachedSolutionRetainsMissingOrFailedNullEvidence(bool failed)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        if (failed)
            model.Solution.NullComparison = NullModelComparisonCalculator.Failure(model.Solution,
                new InvalidOperationException("The saved Null fit failed."));
        model.Data.UpdateSolution(model);
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data });
        package.Position = 0;
        var restored = Assert.IsType<ExperimentData>(Assert.Single(await FTXTCReader.ReadStream(package)));

        Assert.True(restored.Solution.IsValid);
        if (!failed) Assert.Null(restored.Solution.NullComparison);
        else
        {
            Assert.False(restored.Solution.NullComparison.NullFitSucceeded);
            Assert.Equal(model.Solution.NullComparison.NullFitReason, restored.Solution.NullComparison.NullFitReason);
            Assert.Empty(restored.Solution.NullComparison.NullSolutions);
        }
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("member")]
    [InlineData("point")]
    public async Task MalformedAttachedComparisonIsRejectedOrRecoveredWithoutLosingThePrimaryFit(string damage)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        model.Data.UpdateSolution(model);
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data });
        using var malformed = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/solution.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            var comparison = node["nullComparison"];
            if (damage == "schema") comparison["schemaVersion"] = 999;
            else if (damage == "member") comparison["members"][0]["experimentId"] = "another-experiment";
            else comparison["members"][0]["points"][0]["injectionId"] = -1;
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        malformed.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.Strict));
        malformed.Position = 0;
        var recovery = await FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.RecoverUsableContent);
        var experiment = Assert.IsType<ExperimentData>(Assert.Single(recovery.Containers));
        Assert.True(experiment.Solution.IsValid);
        Assert.Equal(model.Solution.Parameters, experiment.Solution.Parameters);
        Assert.Null(experiment.Solution.NullComparison);
        var issue = Assert.Single(recovery.Issues);
        Assert.Equal("null-comparison-skipped", issue.Code);
        Assert.Equal(model.Solution.Guid, issue.ComponentId);
    }

    internal static void AssertNullSolutionIds(NullModelComparison expected, NullModelComparison actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.NullSolutions.Select(solution => (solution.Data.UniqueID, solution.Guid)),
            actual.NullSolutions.Select(solution => (solution.Data.UniqueID, solution.Guid)));
    }

    internal static void AssertComparisonEvidence(NullModelComparison expected, NullModelComparison actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.NullModelId, actual.NullModelId);
        Assert.Equal(expected.IsIndependentMemberComparison, actual.IsIndependentMemberComparison);
        Assert.Equal(expected.BindingFitSucceeded, actual.BindingFitSucceeded);
        Assert.Equal(expected.BindingFitReason, actual.BindingFitReason);
        Assert.Equal(expected.NullFitSucceeded, actual.NullFitSucceeded);
        Assert.Equal(expected.NullFitReason, actual.NullFitReason);
        Assert.Equal(expected.ComparisonUnavailableReason, actual.ComparisonUnavailableReason);
        Assert.Equivalent(expected.BindingInformationCriteria, actual.BindingInformationCriteria, strict: true);
        Assert.Equivalent(expected.NullInformationCriteria, actual.NullInformationCriteria, strict: true);
        Assert.Equal(expected.DeltaAicc, actual.DeltaAicc);
        Assert.Equal(expected.Members.Select(member => member.ExperimentId), actual.Members.Select(member => member.ExperimentId));
        foreach (var (before, after) in expected.Members.Zip(actual.Members))
        {
            Assert.Equal(before.Offset, after.Offset);
            Assert.Equal(before.Scope, after.Scope);
            Assert.Equal(before.Convergence.Algorithm, after.Convergence.Algorithm);
            Assert.Equal(before.Convergence.Termination, after.Convergence.Termination);
            Assert.Equal(before.Convergence.ErrorEstimationOutcome, after.Convergence.ErrorEstimationOutcome);
            Assert.Equal(before.Convergence.Loss, after.Convergence.Loss);
            Assert.Equal(before.Convergence.Objective, after.Convergence.Objective);
            Assert.Equal(before.Convergence.FailureReason, after.Convergence.FailureReason);
            Assert.Equal(before.Points.Select(point => (point.InjectionId, point.InjectionMass, point.Ratio,
                    point.ObservedHeatJoules, point.PredictedHeatJoules, point.Included)),
                after.Points.Select(point => (point.InjectionId, point.InjectionMass, point.Ratio,
                    point.ObservedHeatJoules, point.PredictedHeatJoules, point.Included)));
            var solution = Assert.Single(actual.NullSolutions, solution => solution.Data.UniqueID == after.ExperimentId);
            var original = Assert.Single(expected.NullSolutions, solution => solution.Data.UniqueID == before.ExperimentId);
            Assert.Equal(original.Model.HeatMethod, solution.Model.HeatMethod);
            Assert.Equal(solution.Model.HeatMethod, solution.Data.HeatMethod);
            Assert.Equal(original.UseWeightedFitting, solution.UseWeightedFitting);
            Assert.Equal(after.Offset, solution.Parameters[ParameterType.Offset].Value);
            foreach (var point in after.Points)
                Assert.Equal(point.PredictedHeatJoules, solution.Model.Evaluate(point.InjectionId), 12);
        }
    }

    [Fact]
    public async Task SuccessfulComparisonRoundTripsAsCapturedEvidenceAfterSourceDataChanges()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        var expected = result.NullComparison;
        var expectedObservedHeats = expected.Members.Single().Points.Select(point => point.ObservedHeatJoules).ToArray();

        // The saved comparison describes the data used when it was calculated.
        // A later edit must not cause loading the project to solve again.
        model.Data.Injections[0].SetPeakArea(new FloatWithError(model.Data.Injections[0].PeakArea.Value + 5e-6,
            model.Data.Injections[0].PeakArea.SD));
        using var package = await Write(model.Data, result);
        package.Position = 0;

        var viewer = await new ViewerDocumentReader().ReadAsync(package, "null-comparison.ftxtc", ViewerFileFormat.Ftxtc);
        var viewerFits = Assert.Single(viewer.Experiments).Fits
            .Where(fit => fit.ModelName == model.ModelType.GetProperties().Name)
            .ToList();
        Assert.NotEmpty(viewerFits);
        var viewerFit = viewerFits.First();
        Assert.Equal(model.Data.Injections.Count, viewerFit.FittedKilojoulesPerMole.Length);

        package.Position = 0;

        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
        var actual = Assert.IsType<NullModelComparison>(restored.NullComparison);
        Assert.True(actual.NullFitSucceeded, actual.NullFitReason);
        Assert.Equal(result.BindingAssessment.AutomaticOutcome, restored.BindingAssessment.AutomaticOutcome);
        Assert.Equal(result.BindingAssessment.AutomaticRuleId, restored.BindingAssessment.AutomaticRuleId);
        Assert.Null(restored.BindingAssessment.ManualOverride);
        Assert.Equal(expected.NullModelId, actual.NullModelId);
        Assert.Equal(expected.BindingFitSucceeded, actual.BindingFitSucceeded);
        Assert.Equal(expected.BindingInformationCriteria?.Aicc, actual.BindingInformationCriteria?.Aicc);
        Assert.Equal(expected.NullInformationCriteria?.Aicc, actual.NullInformationCriteria?.Aicc);
        Assert.Equal(expected.DeltaAicc, actual.DeltaAicc);

        var expectedMember = Assert.Single(expected.Members);
        var actualMember = Assert.Single(actual.Members);
        Assert.Equal(expectedMember.ExperimentId, actualMember.ExperimentId);
        Assert.Equal(expectedMember.Offset, actualMember.Offset, 12);
        Assert.Equal(expectedMember.Scope, actualMember.Scope);
        Assert.Equal("local", actualMember.Scope);
        Assert.Equal(expectedMember.Convergence.Algorithm, actualMember.Convergence.Algorithm);
        Assert.Equal(expectedObservedHeats, actualMember.Points.Select(point => point.ObservedHeatJoules));
        Assert.Equal(expectedMember.Points.Select(point => point.PredictedHeatJoules),
            actualMember.Points.Select(point => point.PredictedHeatJoules));
        Assert.Equal(expectedMember.Points.Select(point => point.Ratio), actualMember.Points.Select(point => point.Ratio));
        Assert.NotEqual(actualMember.Points[0].ObservedHeatJoules, restored.Solution.Solutions[0].Data.Injections[0].PeakArea.Value);
        var detachedNullSolution = Assert.Single(actual.NullSolutions);
        Assert.Equal(AnalysisModel.Offset, detachedNullSolution.Model.ModelType);
        Assert.NotSame(restored.Solution.Solutions[0].Model.Data, detachedNullSolution.Model.Data);
        foreach (var point in actualMember.Points)
            Assert.Equal(point.PredictedHeatJoules, detachedNullSolution.Model.Evaluate(point.InjectionId), 12);
    }

    [Theory]
    [InlineData(BindingAssessmentOutcome.BindingDetected)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected)]
    public async Task ManualAssessmentRoundTripsWithoutChangingSavedComparison(BindingAssessmentOutcome outcome)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        var comparison = result.NullComparison;
        var date = result.Date;
        result.SetBindingAssessmentOverride(outcome);
        using var package = await Write(model.Data, result);
        package.Position = 0;

        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
        Assert.Equal(outcome, restored.BindingAssessment.ManualOverride);
        Assert.Equal(outcome, restored.BindingAssessment.EffectiveOutcome);
        Assert.True(restored.BindingAssessment.IsManual);
        Assert.Equal(date, restored.Date);
        Assert.Equal(comparison.DeltaAicc, restored.NullComparison.DeltaAicc);
        Assert.False(restored.IsModified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedGlobalComparisonRoundTripsPooledCriteriaAndPerMemberPredictions(bool locked)
    {
        var first = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var second = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var members = new[] { first, second };
        var globalModel = new GlobalModel(members.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var member in members) globalModel.Parameters.AddIndivdualParameter(member.Parameters);
        globalModel.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
        globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0, locked);
        globalModel.Parameters.SetIndividualFromGlobal();

        var convergence = SolverConvergence.FromFixedFit(0, 0);
        var memberSolutions = members.Select(member => SolutionInterface.FromModel(member, convergence)).ToList();
        for (var index = 0; index < members.Length; index++) members[index].Solution = memberSolutions[index];
        var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel }, memberSolutions,
            convergence, reconstructBootstrap: false);
        globalModel.Solution = globalSolution;
        NullModelComparisonCalculator.Calculate(globalSolution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = new AnalysisResult(globalSolution);
        var expected = Assert.IsType<NullModelComparison>(result.NullComparison);
        Assert.True(expected.NullFitSucceeded, expected.NullFitReason);

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, members.Select(member => member.Data), new[] { result });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
        var actual = Assert.IsType<NullModelComparison>(restored.NullComparison);

        Assert.True(actual.NullFitSucceeded, actual.NullFitReason);
        Assert.Equal(2, actual.Members.Count);
        Assert.All(actual.Members, member => Assert.Equal("local", member.Scope));
        Assert.Equal(2, actual.NullInformationCriteria.FittedParameterCount);
        Assert.Equal(expected.NullInformationCriteria.ObservationCount, actual.NullInformationCriteria.ObservationCount);
        Assert.Equal(expected.NullInformationCriteria.FittedParameterCount, actual.NullInformationCriteria.FittedParameterCount);
        Assert.Equal(expected.NullInformationCriteria.Aicc, actual.NullInformationCriteria.Aicc);
        Assert.Equal(expected.DeltaAicc, actual.DeltaAicc);
        Assert.Equal(members.Select(member => member.Data.UniqueID).OrderBy(id => id),
            actual.Members.Select(member => member.ExperimentId).OrderBy(id => id));

        Assert.Equal(2, actual.NullSolutions.Count);
        foreach (var comparisonMember in actual.Members)
        {
            var nullSolution = Assert.Single(actual.NullSolutions,
                solution => solution.Model.Data.UniqueID == comparisonMember.ExperimentId);
            Assert.Equal(AnalysisModel.Offset, nullSolution.Model.ModelType);
            Assert.False(nullSolution.Model.Parameters.Table[ParameterType.Offset].IsLocked);
            var restoredBinding = Assert.Single(restored.Solution.Solutions,
                solution => solution.Data.UniqueID == comparisonMember.ExperimentId);
            Assert.NotSame(restoredBinding.Model.Data, nullSolution.Model.Data);
            foreach (var point in comparisonMember.Points)
                Assert.Equal(point.PredictedHeatJoules, nullSolution.Model.Evaluate(point.InjectionId), 12);
        }
    }

    [Fact]
    public async Task FailedNullComparisonSnapshotRoundTripsAlongsidePrimaryFit()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var failed = Assert.IsType<NullModelComparison>(model.Solution.NullComparison);
        failed.NullFitSucceeded = false;
        failed.NullFitReason = "The automatic null fit did not converge.";
        failed.NullInformationCriteria = null;
        failed.DeltaAicc = null;
        failed.Members.Clear();
        var result = CreateResult(model);

        using var package = await Write(model.Data, result);
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());

        Assert.True(restored.Solution.IsValid);
        Assert.Equal(model.Solution.ReportParameters[ParameterType.Enthalpy1].Value,
            restored.Solution.Solutions[0].ReportParameters[ParameterType.Enthalpy1].Value);
        Assert.False(restored.NullComparison.NullFitSucceeded);
        Assert.Equal("The automatic null fit did not converge.", restored.NullComparison.NullFitReason);
        Assert.Null(restored.NullComparison.NullInformationCriteria);
        Assert.Null(restored.NullComparison.DeltaAicc);
    }

    [Fact]
    public async Task HistoricalResultWithoutNullComparisonRemainsReadable()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        using var package = await Write(model.Data, result);
        using var historical = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node.Remove("nullComparison");
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        historical.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(historical)).OfType<AnalysisResult>());
        Assert.True(restored.Solution.IsValid);
        Assert.Null(restored.NullComparison);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, restored.BindingAssessment.AutomaticOutcome);
        Assert.False(restored.IsModified);
    }

    [Fact]
    public async Task LegacyComparisonWithoutAssessmentInitializesFromSavedEvidenceAndStaysClean()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        var expected = result.BindingAssessment.AutomaticOutcome;
        using var package = await Write(model.Data, result);
        using var historical = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node.Remove("bindingAssessment");
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        historical.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(historical)).OfType<AnalysisResult>());
        Assert.NotNull(restored.NullComparison);
        Assert.Equal(expected, restored.BindingAssessment.AutomaticOutcome);
        Assert.False(restored.IsModified);
    }

    [Theory]
    [InlineData(false, FtxtcReadPolicy.Strict, "aicc-0-10-v1", 12d, BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.Inconclusive, null)]
    [InlineData(true, FtxtcReadPolicy.Strict, "aicc-6-10-v1", 6d, BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentOutcome.Inconclusive, BindingAssessmentOutcome.BindingDetected)]
    [InlineData(false, FtxtcReadPolicy.RecoverUsableContent, "future-rule-9", 20d, BindingAssessmentOutcome.Inconclusive, BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.NoBindingDetected)]
    [InlineData(true, FtxtcReadPolicy.Strict, "aicc-0-15-v1", 0d, BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentOutcome.BindingDetected)]
    [InlineData(false, FtxtcReadPolicy.Strict, "aicc-0-15-v1", 15d, BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentOutcome.BindingDetected, null)]
    [InlineData(true, FtxtcReadPolicy.Strict, "future-rule-9", null, BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.NotAssessed, BindingAssessmentOutcome.NoBindingDetected)]
    public async Task SavedSingleAndPooledAssessmentsAreReevaluatedAndRetainOverrides(
        bool pooled, FtxtcReadPolicy readPolicy, string ruleId, double? delta,
        BindingAssessmentOutcome savedOutcome, BindingAssessmentOutcome expectedOutcome,
        BindingAssessmentOutcome? manualOverride)
    {
        var models = Enumerable.Range(0, pooled ? 2 : 1)
            .Select(_ => InjectionProcessingMethodTests.FittedModel(bootstrap: false)).ToArray();
        AnalysisResult result;
        if (pooled)
        {
            var globalModel = new GlobalModel(models.ToList()) { Parameters = new GlobalModelParameters() };
            foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
            globalModel.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
            globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0);
            globalModel.Parameters.SetIndividualFromGlobal();
            var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel },
                models.Select(model => model.Solution).ToList(), models[0].Solution.Convergence,
                reconstructBootstrap: false);
            globalModel.Solution = globalSolution;
            NullModelComparisonCalculator.Calculate(globalSolution, false,
                SolverAlgorithm.LevenbergMarquardt, 3000, 1);
            result = new AnalysisResult(globalSolution);
        }
        else
        {
            NullModelComparisonCalculator.Calculate(models[0].Solution, false,
                SolverAlgorithm.LevenbergMarquardt, 3000, 1);
            result = CreateResult(models[0]);
        }
        Assert.False(result.IsIndependentAssessmentCollection);
        var comparison = result.NullComparison;
        Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
        FitInformationCriteria Criteria(double aicc) => FitInformationCriteria.Restore(
            20, 1, 2, GaussianLikelihoodMode.EstimatedCommonVariance,
            aicc - 5, aicc - 2, aicc, true, true, string.Empty, string.Empty, 1, 1, 1, 0);
        comparison.BindingInformationCriteria = delta.HasValue ? Criteria(100) : null;
        comparison.NullInformationCriteria = delta.HasValue ? Criteria(100 + delta.Value) : null;
        comparison.DeltaAicc = delta;
        comparison.ComparisonUnavailableReason = delta.HasValue ? string.Empty : "Comparison unavailable.";
        result.RestoreBindingAssessment(BindingAssessmentState.Restore(savedOutcome, ruleId, manualOverride));

        // Reassessment must use captured evidence, even when the live observations have changed.
        models[0].Data.Injections[0].SetPeakArea(new FloatWithError(models[0].Data.Injections[0].PeakArea.Value + 5e-6));
        var savedEnthalpies = result.Solution.Solutions.Select(member => member.ReportParameters[ParameterType.Enthalpy1].Value).ToArray();
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, models.Select(model => model.Data), new[] { result });
        package.Position = 0;
        var read = await FTXTCReader.ReadWithRecovery(package, readPolicy);
        var restored = Assert.Single(read.Containers.OfType<AnalysisResult>());

        Assert.Equal(expectedOutcome, restored.BindingAssessment.AutomaticOutcome);
        Assert.Equal(BindingAssessmentState.CurrentRuleId, restored.BindingAssessment.AutomaticRuleId);
        Assert.Equal(manualOverride, restored.BindingAssessment.ManualOverride);
        Assert.Equal(manualOverride ?? expectedOutcome, restored.BindingAssessment.EffectiveOutcome);
        Assert.Equal(delta, restored.NullComparison.DeltaAicc);
        Assert.Equal(comparison.Members.SelectMany(member => member.Points).Select(point => point.ObservedHeatJoules),
            restored.NullComparison.Members.SelectMany(member => member.Points).Select(point => point.ObservedHeatJoules));
        Assert.Equal(savedEnthalpies, restored.Solution.Solutions.Select(member => member.ReportParameters[ParameterType.Enthalpy1].Value));
        Assert.Empty(read.Issues);
        Assert.False(restored.IsModified);
    }

    [Fact]
    public async Task MalformedAssessmentIsDiscardedInRecoveryAndRejectedInStrictMode()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        var expected = result.BindingAssessment.AutomaticOutcome;
        using var package = await Write(model.Data, result);
        using var malformed = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node["bindingAssessment"]["schemaVersion"] = 999;
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        malformed.Position = 0;
        var recovery = await FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.RecoverUsableContent);
        var restored = Assert.Single(recovery.Containers.OfType<AnalysisResult>());
        Assert.Equal(expected, restored.BindingAssessment.AutomaticOutcome);
        Assert.Null(restored.BindingAssessment.ManualOverride);
        Assert.Contains(recovery.Issues, issue => issue.Code == "binding-assessment-skipped");
        malformed.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.Strict));
    }

    [Fact]
    public async Task MalformedOptionalComparisonIsDroppedDuringRecoveryButRejectedInStrictMode()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        using var package = await Write(model.Data, result);
        using var malformed = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node["nullComparison"] = new JsonObject { ["schemaVersion"] = 999 };
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        malformed.Position = 0;
        var recovery = await FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.RecoverUsableContent);
        var recovered = Assert.Single(recovery.Containers.OfType<AnalysisResult>());
        Assert.True(recovered.Solution.IsValid);
        Assert.Null(recovered.NullComparison);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, recovered.BindingAssessment.AutomaticOutcome);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, recovered.BindingAssessment.ManualOverride);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, recovered.BindingAssessment.EffectiveOutcome);
        Assert.Contains(recovery.Issues, issue => issue.Code == "null-comparison-skipped");

        malformed.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.Strict));
    }

    static AnalysisResult CreateResult(AnalysisITC.Core.Analysis.Models.Model model)
    {
        var solver = new Solver { Model = model };
        var global = GlobalSolution.FromSingleExperimentSolver(solver);
        return new AnalysisResult(global);
    }

    static async Task<MemoryStream> Write(ExperimentData data, AnalysisResult result)
    {
        var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { data }, new[] { result });
        return package;
    }
}
