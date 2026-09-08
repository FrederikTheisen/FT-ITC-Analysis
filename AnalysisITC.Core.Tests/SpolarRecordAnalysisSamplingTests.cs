using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class SpolarRecordAnalysisSamplingTests
{
    [Fact]
    public async Task ExactEvaluationIsDeterministicAndUsesCentralValues()
    {
        var analysis = await LoadAnalysis();
        var evaluate = EvaluateMethod();

        var first = (FTSRMethod.SROutput)evaluate.Invoke(analysis, new object[] { true });
        var second = (FTSRMethod.SROutput)evaluate.Invoke(analysis, new object[] { true });

        Assert.Equal(first.HydrationEntropy.Value, second.HydrationEntropy.Value);
        Assert.Equal(first.ConformationalEntropy.Value, second.ConformationalEntropy.Value);
        Assert.Equal(first.Rvalue.Value, second.Rvalue.Value);
        Assert.Equal(analysis.EvalutationTemperature(sample: false), first.ReferenceTemperature.Value);

        var temperature = Math.Abs(273.15 + analysis.EvalutationTemperature(sample: false));
        var heatCapacity = ((FloatWithError)GetPrivateField(analysis, "HeatCapacityChange")).Value;
        var gts = FTSRMethod.GlobalZeroEntropy.Value;
        var ratio = FTSRMethod.RatioGlob.Value;
        var danpCoefficient = FTSRMethod.AnpCoeff.Value
            / (FTSRMethod.AnpCoeff.Value + ratio * FTSRMethod.ApCoeff.Value);
        var expectedHydration = heatCapacity * danpCoefficient * Math.Log(temperature / gts);
        var expectedConformational = -expectedHydration - FTSRMethod.RototranslationalEntropy.Value;

        Assert.Equal(expectedHydration, first.HydrationEntropy.Value, 12);
        Assert.Equal(expectedConformational, first.ConformationalEntropy.Value, 12);
        Assert.Equal(expectedConformational / FTSRMethod.PerResidueEntropyLoss.Value,
            first.Rvalue.Value, 12);
    }

    [Fact]
    public async Task UncertaintyEvaluationSamplesRototranslationalEntropy()
    {
        var analysis = await LoadAnalysis();
        var evaluate = EvaluateMethod();
        var original = FTSRMethod.RototranslationalEntropy;

        try
        {
            // Make this input dominate the variation so the sampling behavior
            // is tested independently of the other fitted uncertainties.
            FTSRMethod.RototranslationalEntropy = new FloatWithError(-110, 1000);
            var values = Enumerable.Range(0, 8)
                .Select(_ => ((FTSRMethod.SROutput)evaluate.Invoke(analysis, new object[] { false }))
                    .ConformationalEntropy.Value)
                .ToArray();

            Assert.True(values.Distinct().Count() > 1,
                "Uncertainty iterations should sample rototranslational entropy.");

            var exactTemperature = analysis.EvalutationTemperature(sample: false);
            var sampledTemperatures = Enumerable.Range(0, 8)
                .Select(_ => analysis.EvalutationTemperature(sample: true))
                .ToArray();
            Assert.Contains(sampledTemperatures, value => value != exactTemperature);
        }
        finally
        {
            FTSRMethod.RototranslationalEntropy = original;
        }
    }

    static MethodInfo EvaluateMethod() => typeof(FTSRMethod).GetMethod(
        "Evaluate", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(FTSRMethod).FullName, "Evaluate");

    static object GetPrivateField(FTSRMethod analysis, string propertyName)
    {
        var property = typeof(FTSRMethod).GetProperty(propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(FTSRMethod).FullName, propertyName);
        return property.GetValue(analysis);
    }

    static async Task<FTSRMethod> LoadAnalysis()
    {
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc"));
        var sourceResult = (await FTITCReader.ReadStream(source)).OfType<AnalysisResult>().Single();
        var members = sourceResult.Solution.Solutions;
        for (var index = 0; index < members.Count; index++)
            members[index].Data.MeasuredTemperature = 20 + index * 5;

        var globalModel = new GlobalModel(members.Select(solution => solution.Model).ToList())
        {
            Parameters = sourceResult.Model.Parameters,
            ModelCloneOptions = sourceResult.Model.ModelCloneOptions,
        };
        var globalSolver = new GlobalSolver
        {
            Model = globalModel,
            ErrorEstimationMethod = sourceResult.Solution.ErrorEstimationMethod,
            UseErrorWeightedFitting = sourceResult.Solution.UseWeightedFitting,
        };
        var globalSolution = new GlobalSolution(globalSolver, members, sourceResult.Solution.Convergence);
        globalModel.Solution = globalSolution;
        var result = new AnalysisResult(globalSolution);
        return result.SpolarRecordAnalysis
            ?? throw new InvalidOperationException("Fixture does not contain temperature dependence for Spolar analysis.");
    }
}
