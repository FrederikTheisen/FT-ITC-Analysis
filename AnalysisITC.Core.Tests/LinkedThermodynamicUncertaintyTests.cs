using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json.Nodes;
using AnalysisITC.Core.Viewer;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

public class LinkedThermodynamicUncertaintyTests
{
    static readonly ThermodynamicParameterSlot Slot = ThermodynamicParameterSlots.ForStep(1);

    [Theory]
    [InlineData(VariableConstraint.None)]
    [InlineData(VariableConstraint.SameForAll)]
    [InlineData(VariableConstraint.TemperatureDependent)]
    public void ProfilesNeverChangeMemberCenters(VariableConstraint constraint)
    {
        var solution = Create(constraint);
        var before = solution.Solutions.Select(m => m.ReportParameters.ToDictionary(x => x.Key, x => x.Value.Value)).ToArray();
        foreach (var incomplete in new[] { false, true })
        {
            solution.ProfileLikelihoodRun = Run(Coordinate(ParameterType.Gibbs1, -25000, 500, 700, incomplete: incomplete));
            Check();
            solution.ApplyProfileTemperatureCoordinates(solution.ProfileLikelihoodRun);
            Check();
        }
        void Check()
        {
            for (var i = 0; i < before.Length; i++)
                foreach (var key in new[] { Slot.Affinity, Slot.Gibbs, Slot.Enthalpy, Slot.EntropyContribution })
                    Assert.Equal(before[i][key], solution.Solutions[i].ReportParameters[key].Value, 10);
        }
        if (constraint == VariableConstraint.SameForAll)
            Assert.Equal(Math.Exp(-24000 / (Energy.R * 320)), solution.Solutions[2].ReportParameters[Slot.Affinity].Value, 12);
    }

    [Theory]
    [InlineData(280)]
    [InlineData(300)]
    [InlineData(320)]
    public void SharedEnthalpyProfileUsesSignedWeights(double temperature)
    {
        var solution = Create(VariableConstraint.SameForAll);
        solution.ProfileLikelihoodRun = Run(Coordinate(Slot.Gibbs, -25000, 500, 700),
            Coordinate(Slot.Enthalpy, -40000, 4000, 6000));
        var value = Curve(solution, Slot.Gibbs).Evaluate(temperature - 273.15);
        var g = temperature / 300;
        var h = 1 - g;
        var center = g * -25000 + h * -40000;
        Assert.Equal(center, value.Value, 9);
        Assert.Equal(center - Math.Sqrt(Math.Pow(g * 500, 2) + Math.Pow(h * (h < 0 ? 6000 : 4000), 2)), value.Lower, 9);
        Assert.Equal(center + Math.Sqrt(Math.Pow(g * 700, 2) + Math.Pow(h * (h < 0 ? 4000 : 6000), 2)), value.Upper, 9);
        var member = solution.Solutions.Single(m => Math.Abs(m.TempKelvin - temperature) < .001);
        var kd = member.ReportParameters[Slot.Affinity];
        Assert.Equal(Math.Exp(value.Lower / (Energy.R * temperature)), kd.Lower, 12);
        Assert.Equal(Math.Exp(value.Upper / (Energy.R * temperature)), kd.Upper, 12);
    }

    [Fact]
    public void RegressionCombinesOriginalCoordinatesForMemberEntropy()
    {
        var solution = Create(VariableConstraint.None);
        var coordinates = solution.Solutions.Select(m => Coordinate(Slot.Enthalpy, m.Parameters[Slot.Enthalpy].Value,
            1000, 2000, m.Data.UniqueID)).ToList();
        coordinates.Add(Coordinate(Slot.Gibbs, -25000, 500, 500));
        solution.ProfileLikelihoodRun = Run(coordinates.ToArray());
        var curve = Curve(solution, Slot.Gibbs);
        Assert.Equal(-28000, Curve(solution, Slot.Enthalpy).Intercept, 8);
        Assert.Equal(650, Curve(solution, Slot.Enthalpy).Slope, 8);
        var member = solution.Solutions[1];
        var entropy = member.ReportParameters[Slot.EntropyContribution];
        // At Tr Gibbs has no enthalpy contribution. Member entropy subtracts only its own H.
        Assert.Equal(5000, entropy.Value, 8);
        Assert.Equal(5000 - Math.Sqrt(500 * 500 + 2000 * 2000), entropy.Lower, 8);
        Assert.Equal(5000 + Math.Sqrt(500 * 500 + 1000 * 1000), entropy.Upper, 8);
        const double t = 320;
        var basis = (t - 300) - t * Math.Log(t / 300);
        var weights = new[] { -1.0 / 45 - basis / 40, -1.0 / 45, -1.0 / 45 + basis / 40 };
        var width = Math.Sqrt(Math.Pow(t / 300 * 500, 2) + weights.Sum(w => Math.Pow(w * (w < 0 ? 2000 : 1000), 2)));
        Assert.Equal(width, curve.Evaluate(t - 273.15).LowerWidth, 8);
        Assert.Equal(-30000, member.ReportParameters[Slot.Enthalpy].Value);
    }

    [Fact]
    public void MissingIntervalsAreIgnoredOnlyForZeroWeightsOrLocks()
    {
        var solution = Create(VariableConstraint.SameForAll);
        solution.ProfileLikelihoodRun = Run(Coordinate(Slot.Gibbs, -25000, 500, 500));
        Assert.Equal(500, Curve(solution, Slot.Gibbs).Evaluate(26.85).LowerWidth, 8);
        Assert.True(double.IsNaN(Curve(solution, Slot.Gibbs).Evaluate(46.85).Lower));
        solution.Model.Parameters.GlobalTable[Slot.Enthalpy].Update(-40000, true);
        Assert.Equal(320.0 / 300 * 500, Curve(solution, Slot.Gibbs).Evaluate(46.85).LowerWidth, 8);
    }

    [Fact]
    public void ReplicatesEvaluateJointCoordinatesAndKeepPrimaryCenter()
    {
        var solution = Create(VariableConstraint.SameForAll);
        var first = Create(VariableConstraint.SameForAll, gibbs: -24900, enthalpy: -38400);
        var second = Create(VariableConstraint.SameForAll, gibbs: -25100, enthalpy: -41600);
        solution.RestoreGlobalBootstrapSolutions(new List<GlobalSolution> { first, second });
        var value = Curve(solution, Slot.Gibbs).Evaluate(46.85);
        // At 320 K the correlated changes cancel exactly: (320/300)*100 - (20/300)*1600 = 0.
        Assert.Equal(-24000, value.Value, 8);
        Assert.Equal(0, value.SD, 8);
        Assert.Equal(-24000, value.Lower, 8);
        Assert.Equal(-24000, value.Upper, 8);
    }

    [Theory]
    [InlineData(VariableConstraint.ThermodynamicallyLinked)]
    [InlineData(VariableConstraint.None)]
    [InlineData(VariableConstraint.SameForAll)]
    [InlineData(VariableConstraint.TemperatureDependent)]
    public async Task LeaveOneOutRoundTripRetainsMembershipCoordinatesAndCapturedTemperatures(VariableConstraint affinity)
    {
        var solution = Create(VariableConstraint.None, affinity: affinity);
        var replicates = new List<GlobalSolution>();
        for (var omitted = 0; omitted < 3; omitted++)
        {
            var replicate = Create(VariableConstraint.None, omitted: omitted, gibbs: -25000 + omitted * 100, affinity: affinity);
            foreach (var member in replicate.Solutions)
            {
                var primary = solution.Solutions.Single(m => m.Data.FileName == member.Data.FileName);
                member.Data.SetID(primary.Data.UniqueID);
                member.BootstrapReplicateIndex = omitted;
            }
            replicates.Add(replicate);
        }
        solution.SetBootstrapSolutions(replicates);
        var expected = Curve(solution, Slot.Gibbs).Evaluate(36.85);
        if (affinity == VariableConstraint.ThermodynamicallyLinked)
            foreach (var member in solution.Solutions) member.Data.MeasuredTemperature += 7;
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, solution.Model.Models.Select(m => m.Data), new[] { new AnalysisResult(solution) });
        stream.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>()).Solution;
        Assert.Equal(300, restored.ReferenceTemperatureKelvin);
        Assert.Equal(new[] { 280.0, 300, 320 }, restored.Solutions.Select(m => m.TempKelvin));
        Assert.Equal(3, restored.BootstrapSolutions.Count);
        Assert.All(restored.BootstrapSolutions, replicate => Assert.Equal(2, replicate.Solutions.Count));
        var actual = Curve(restored, Slot.Gibbs).Evaluate(36.85);
        Assert.Equal(expected.Value, actual.Value, 8);
        Assert.Equal(expected.Lower, actual.Lower, 8);
        Assert.Equal(expected.Upper, actual.Upper, 8);
    }

    [Theory]
    [InlineData(AnalysisModel.OneSetOfSites, 1)]
    [InlineData(AnalysisModel.TwoSetsOfSites, 2)]
    [InlineData(AnalysisModel.Dissociation, 1)]
    [InlineData(AnalysisModel.CompetitiveBinding, 1)]
    [InlineData(AnalysisModel.SequentialBindingSites, 2)]
    [InlineData(AnalysisModel.SequentialBindingSites, 3)]
    [InlineData(AnalysisModel.SequentialBindingSites, 4)]
    public void EveryModelAndSlotPreservesCentersAndUsesItsOwnRegression(AnalysisModel type, int steps)
    {
        var solution = Create(VariableConstraint.None, modelType: type, steps: steps);
        var centers = solution.Solutions.Select(m => m.ReportParameters.ToDictionary(x => x.Key, x => x.Value.Value)).ToArray();
        var coordinates = Enumerable.Range(1, steps).Select(ThermodynamicParameterSlots.ForStep).SelectMany(slot =>
            solution.Solutions.Select(m => Coordinate(slot.Enthalpy, m.Parameters[slot.Enthalpy].Value, 1000, 1000, m.Data.UniqueID))
                .Append(Coordinate(slot.Gibbs, solution.Model.Parameters.GlobalTable[slot.Gibbs].Value, 500, 500))).ToArray();
        solution.ProfileLikelihoodRun = Run(coordinates);
        solution.ApplyProfileTemperatureCoordinates(solution.ProfileLikelihoodRun);
        foreach (var slot in Enumerable.Range(1, steps).Select(ThermodynamicParameterSlots.ForStep))
        {
            Assert.Equal(-28000 - 1000 * (slot.Index - 1), Curve(solution, slot.Enthalpy).Intercept, 8);
            foreach (var (member, index) in solution.Solutions.Select((member, index) => (member, index)))
            {
                Assert.Equal(centers[index][slot.Affinity], member.ReportParameters[slot.Affinity].Value, 12);
                Assert.True(double.IsFinite(member.ReportParameters[slot.Affinity].Lower));
                Assert.Equal(centers[index][slot.EntropyContribution], member.ReportParameters[slot.EntropyContribution].Value, 8);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegressionIncludesRepeatedTemperaturesAndLockedEnthalpies(bool zeroSpread)
    {
        var solution = Create(VariableConstraint.None, temperaturesOverride: zeroSpread ? new[] { 300.0, 300, 300 } : new[] { 280.0, 280, 320 });
        solution.Solutions[0].Model.Parameters.Table[Slot.Enthalpy].Update(-40000, true);
        var h = Curve(solution, Slot.Enthalpy);
        Assert.Equal(zeroSpread ? -28000 : -24500, h.Intercept, 8);
        Assert.Equal(zeroSpread ? 0 : 525, h.Slope, 8);
        solution.Model.Models.Reverse();
        Assert.Equal(h.Intercept, Curve(solution, Slot.Enthalpy).Intercept, 8);
        Assert.Equal(h.Slope, Curve(solution, Slot.Enthalpy).Slope, 8);
    }

    [Fact]
    public async Task ProfileRoundTripKeepsExactCurvesAndUnavailableIntervals()
    {
        var solution = Create(VariableConstraint.TemperatureDependent);
        solution.ProfileLikelihoodRun = Run(Coordinate(Slot.Gibbs, -25000, 500, 700),
            Coordinate(Slot.Enthalpy, -40000, 4000, 6000), Coordinate(Slot.HeatCapacity, 300, 20, 40));
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, solution.Model.Models.Select(m => m.Data), new[] { new AnalysisResult(solution) });
        stream.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>()).Solution;
        foreach (var t in new[] { 280.0, 300, 320 })
        {
            var basis = (t - 300) - t * Math.Log(t / 300);
            var expected = t / 300 * -25000 + (1 - t / 300) * -40000 + 300 * basis;
            var actual = Curve(restored, Slot.Gibbs).Evaluate(t - 273.15);
            Assert.Equal(expected, actual.Value, 8);
            Assert.Equal(Curve(solution, Slot.Gibbs).Evaluate(t - 273.15).Lower, actual.Lower, 8);
        }
        stream.Position = 0;
        var viewer = await new ViewerDocumentReader().ReadAsync(stream, "linked.ftxtc", ViewerFileFormat.Ftxtc);
        var dependence = Assert.Single(Assert.Single(viewer.AnalysisResults).TemperatureParameterEvaluation.Dependences, d => d.Key == Slot.Gibbs.ToString());
        Assert.Equal(.3, dependence.HeatCapacityTerm, 10);
        Assert.Contains(dependence.Contributions, c => c.WeightHeatCapacityTerm == 1);
    }

    [Fact]
    public async Task InvalidGlobalReplicateMetadataFailsStrictAndRecoveryKeepsPrimary()
    {
        var solution = Create(VariableConstraint.None);
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, solution.Model.Models.Select(m => m.Data), new[] { new AnalysisResult(solution) });
        using var corrupt = FTXTCFormatTests.RewriteAuthenticatedPackage(stream, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json")) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node["globalReplicates"] = new JsonArray(new JsonObject { ["index"] = 0,
                ["memberSolutionIds"] = new JsonArray("unknown-member"), ["globalParameters"] = new JsonArray() });
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, FTXTCFormat.SchemaMinor);
        await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(corrupt));
        corrupt.Position = 0;
        var recovered = await FTXTCReader.ReadWithRecovery(corrupt, FtxtcReadPolicy.RecoverUsableContent);
        var primary = Assert.Single(recovered.Containers.OfType<AnalysisResult>());
        Assert.Empty(primary.Solution.BootstrapSolutions);
        Assert.Equal(-25000, Curve(primary.Solution, Slot.Gibbs).Intercept, 8);
    }

    [Fact]
    public async Task BootstrapWithDifferentOriginUsesCompleteCurveAndPersistsAtParentReference()
    {
        var solution = Create(VariableConstraint.TemperatureDependent);
        var replica = Create(VariableConstraint.TemperatureDependent);
        const double newReference = 310;
        const double h = -40000, g = -25000, cp = 300;
        var transportedG = newReference / 300 * g + (1 - newReference / 300) * h
            + cp * ((newReference - 300) - newReference * Math.Log(newReference / 300));
        replica.Model.Parameters.SetReferenceTemperatureKelvin(newReference);
        replica.Model.Parameters.GlobalTable[Slot.Enthalpy].Update(h + cp * (newReference - 300));
        replica.Model.Parameters.GlobalTable[Slot.Gibbs].Update(transportedG);
        for (var i = 0; i < replica.Solutions.Count; i++)
        {
            replica.Solutions[i].Data.SetID(solution.Solutions[i].Data.UniqueID);
            replica.Solutions[i].BootstrapReplicateIndex = 7;
        }
        solution.SetBootstrapSolutions(new List<GlobalSolution> { replica });
        foreach (var t in new[] { 280.0, 300, 320 })
        {
            var value = Curve(solution, Slot.Gibbs).Evaluate(t - 273.15);
            Assert.Equal(value.Value, value.Lower, 8);
            Assert.Equal(value.Value, value.Upper, 8);
        }
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, solution.Model.Models.Select(m => m.Data), new[] { new AnalysisResult(solution) });
        stream.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>()).Solution;
        var restoredReplica = Assert.Single(restored.BootstrapSolutions);
        Assert.Equal(300, restoredReplica.ReferenceTemperatureKelvin);
        Assert.Equal(g, restoredReplica.Model.Parameters.GlobalTable[Slot.Gibbs].Value, 8);
        Assert.Equal(h, restoredReplica.Model.Parameters.GlobalTable[Slot.Enthalpy].Value, 8);
        stream.Position = 0;
        var viewer = await new ViewerDocumentReader().ReadAsync(stream, "linked-bootstrap.ftxtc", ViewerFileFormat.Ftxtc);
        var curve = Assert.Single(Assert.Single(viewer.AnalysisResults).TemperatureParameterEvaluation.Dependences, d => d.Key == Slot.Gibbs.ToString());
        Assert.Single(curve.Replicates);
        Assert.Equal(.3, curve.Replicates[0].HeatCapacityTerm, 10);
    }

    [Fact]
    public async Task MissingOptionalMetadataLoadsWithoutRefittingPrimary()
    {
        var solution = Create(VariableConstraint.None);
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, solution.Model.Models.Select(m => m.Data), new[] { new AnalysisResult(solution) });
        using var legacy = FTXTCFormatTests.RewriteAuthenticatedPackage(stream, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json")) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node.Remove("referenceTemperatureKelvin"); node.Remove("memberTemperaturesKelvin"); node.Remove("globalReplicates");
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, FTXTCFormat.SchemaMinor);
        var restored = Assert.Single((await FTXTCReader.ReadStream(legacy)).OfType<AnalysisResult>()).Solution;
        Assert.Equal(300, restored.ReferenceTemperatureKelvin);
        for (var i = 0; i < 3; i++)
            Assert.Equal(solution.Solutions[i].Parameters[Slot.Affinity].Value, restored.Solutions[i].Parameters[Slot.Affinity].Value);
    }

    [Fact]
    public void StandardReportingUsesTheSameLinkedRelationship()
    {
        var solution = Create(VariableConstraint.TemperatureDependent);
        solution.ProfileLikelihoodRun = Run(Coordinate(Slot.Gibbs, -25000, 500, 700),
            Coordinate(Slot.Enthalpy, -40000, 4000, 6000), Coordinate(Slot.HeatCapacity, 300, 20, 40));
        foreach (var key in new[] { Slot.Gibbs, Slot.Enthalpy, Slot.EntropyContribution })
        {
            var expected = Curve(solution, key).Evaluate(AppSettings.ReferenceTemperature);
            var value = solution.GetStandardParameterValue(key);
            Assert.Equal(expected.Value, value.Value);
            Assert.Equal(expected.Lower, value.Lower);
            Assert.Equal(expected.Upper, value.Upper);
        }
    }

    [Fact]
    public void ResultDescriptionUsesPropagatedLinkedHeatCapacity()
    {
        var solution = Create(VariableConstraint.TemperatureDependent);
        solution.ProfileLikelihoodRun = Run(Coordinate(Slot.Gibbs, -25000, 500, 700),
            Coordinate(Slot.Enthalpy, -40000, 4000, 6000), Coordinate(Slot.HeatCapacity, 300, 20, 40));
        var result = new AnalysisResult(solution);
        var cp = new AnalysisResultAggregateSummaryCalculator(result).EvaluateHeatCapacity(Slot).Value;
        var unit = EnergyUnitResolver.Resolve(AppSettings.EnergyUnitFamily, new[] { cp.Value });
        Assert.Contains(new Energy(cp).ToFormattedString(unit, permole: true, perK: true), result.GetResultString());
    }

    [Fact]
    public void BootstrapAffinitySummaryTransformsSamplesBeforeComputingSpread()
    {
        var solution = Create(VariableConstraint.SameForAll);
        solution.RestoreGlobalBootstrapSolutions(new List<GlobalSolution> {
            Create(VariableConstraint.SameForAll, gibbs: -24000), Create(VariableConstraint.SameForAll, gibbs: -27000) });
        var actual = new AnalysisResultAggregateSummaryCalculator(new AnalysisResult(solution)).EvaluateSummaryParameter(Slot.Affinity, 26.85).Value;
        var center = Math.Exp(-25000 / (300 * Energy.R));
        var first = Math.Exp(-24000 / (300 * Energy.R));
        var second = Math.Exp(-27000 / (300 * Energy.R));
        Assert.Equal(center, actual.Value, 12);
        Assert.Equal(Math.Sqrt(((first - center) * (first - center) + (second - center) * (second - center)) / 2), actual.SD, 12);
        Assert.Equal(second, actual.Lower, 12);
        Assert.Equal(first, actual.Upper, 12);
    }

    static SummaryDependence Curve(GlobalSolution solution, ParameterType key) =>
        new AnalysisResultAggregateSummaryCalculator(new AnalysisResult(solution)).BuildDependence(key);

    internal static ProfileCoordinateResult Coordinate(ParameterType key, double value, double lower, double upper,
        string member = null, bool incomplete = false) => new(
            new ProfileCoordinateId(key, member == null ? ParameterBoundaryScope.Shared : ParameterBoundaryScope.Local, member),
            value, -1e7, 1e7,
            new ProfileSideResult(incomplete ? ProfileSideOutcome.SearchExhausted : ProfileSideOutcome.EndpointFound, value - lower),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, value + upper));

    internal static ProfileLikelihoodRunResult Run(params ProfileCoordinateResult[] coordinates) => new(.95,
        ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 30, 4, 1, 26, 1, 1,
        SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
        ErrorEstimationOutcome.Completed, coordinates);

    internal static GlobalSolution Create(VariableConstraint constraint, int omitted = -1, double gibbs = -25000, double enthalpy = -40000,
        VariableConstraint affinity = VariableConstraint.ThermodynamicallyLinked, AnalysisModel modelType = AnalysisModel.OneSetOfSites, int steps = 1,
        double[] temperaturesOverride = null)
    {
        var models = new List<Model>();
        var temperatures = temperaturesOverride ?? new[] { 280.0, 300, 320 };
        var hs = new[] { -40000.0, -30000, -14000 };
        for (var i = 0; i < 3; i++)
        {
            if (i == omitted) continue;
            var data = new ExperimentData($"linked-{i}.itc") { MeasuredTemperature = temperatures[i] - 273.15,
                CellVolume = 200e-6, CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6) };
            for (var index = 0; index < 3; index++)
            {
                var injection = new InjectionData(data, index, 2e-6, 2e-10, include: true)
                {
                    ActualCellConcentration = 10e-6, ActualTitrantConcentration = (index + 1) * 2e-6, Ratio = index + 1,
                };
                injection.SetPeakArea(new FloatWithError(-2e-6 * (index + 1), 1e-8));
                data.Injections.Add(injection);
            }
            Model model = modelType switch
            {
                AnalysisModel.TwoSetsOfSites => new TwoSetsOfSites(data),
                AnalysisModel.SequentialBindingSites => new SequentialBindingSites(data),
                AnalysisModel.CompetitiveBinding => new CompetitiveBinding(data),
                AnalysisModel.Dissociation => new Dissociation(data),
                _ => new OneSetOfSites(data),
            };
            model.InitializeParameters(data);
            if (model is SequentialBindingSites)
            {
                model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = steps;
                model.ApplyModelOptions();
            }
            if (model.Parameters.Table.ContainsKey(ParameterType.Nvalue1)) model.Parameters.Table[ParameterType.Nvalue1].Update(1);
            foreach (var slot in Enumerable.Range(1, steps).Select(ThermodynamicParameterSlots.ForStep))
            {
                model.Parameters.AddOrUpdateParameter(slot.Enthalpy, hs[i] - 1000 * (slot.Index - 1));
                model.Parameters.AddOrUpdateParameter(slot.Affinity, 5);
            }
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            models.Add(model);
        }
        var global = new GlobalModel(models) { ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut } };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        global.Parameters.SetReferenceTemperatureKelvin(300);
        foreach (var slot in Enumerable.Range(1, steps).Select(ThermodynamicParameterSlots.ForStep))
        {
            global.Parameters.SetConstraintForParameter(slot.Affinity, affinity);
            global.Parameters.SetConstraintForParameter(slot.Enthalpy, constraint);
            if (affinity == VariableConstraint.SameForAll) global.Parameters.AddorUpdateGlobalParameter(slot.Affinity, 5);
            else if (affinity != VariableConstraint.None) global.Parameters.AddorUpdateGlobalParameter(slot.Gibbs, gibbs - 1000 * (slot.Index - 1));
            if (constraint != VariableConstraint.None) global.Parameters.AddorUpdateGlobalParameter(slot.Enthalpy, enthalpy);
            if (constraint == VariableConstraint.TemperatureDependent) global.Parameters.AddorUpdateGlobalParameter(slot.HeatCapacity, 300);
        }
        global.Parameters.SetIndividualFromGlobal();
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }));
        global.Solution = solution;
        return solution;
    }
}
