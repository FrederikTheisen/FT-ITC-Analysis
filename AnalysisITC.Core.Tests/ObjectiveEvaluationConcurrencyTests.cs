using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.DataReaders;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class ObjectiveEvaluationTests
{
    public static TheoryData<string, DilutionMethod, bool> ForeignPredictionCases
    {
        get
        {
            var cases = new TheoryData<string, DilutionMethod, bool>();
            foreach (var id in new[] { "sequential-3", "two-realistic" })
            foreach (var method in new[] { DilutionMethod.MicroCal, DilutionMethod.Exponential })
            foreach (var enthalpy in new[] { false, true })
                cases.Add(id, method, enthalpy);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ForeignPredictionCases))]
    public void ForeignPredictionsLeaveOwnerObjectiveStateUntouched(string id, DilutionMethod method, bool enthalpy)
        => AssertForeignPredictionIsolation(id, method, enthalpy, changeParameters: false);

    [Theory]
    [InlineData(DilutionMethod.MicroCal, false)]
    [InlineData(DilutionMethod.MicroCal, true)]
    [InlineData(DilutionMethod.Exponential, false)]
    [InlineData(DilutionMethod.Exponential, true)]
    public void ForeignTwoSiteParametersCannotOverwriteOwnerPreparedState(DilutionMethod method, bool enthalpy)
        => AssertForeignPredictionIsolation("two-realistic", method, enthalpy, changeParameters: true);

    static void AssertForeignPredictionIsolation(string id, DilutionMethod method, bool enthalpy, bool changeParameters)
    {
        Model NewModel()
        {
            var created = CreateModel("PytcBookkeeping", id);
            SetMethod(created, method);
            created.ApplyModelOptions();
            if (method == DilutionMethod.Exponential)
                Assert.Equal(InjectionHeatMethod.IdealContinuousMixing, created.HeatMethod);
            return created;
        }

        var model = NewModel();
        var reference = NewModel();
        var undisturbed = NewModel();
        double[] Predictions(Model target, bool molar) => target.Data.Injections
            .Select(i => molar ? target.EvaluateEnthalpy(i.ID) : target.Evaluate(i.ID)).ToArray();
        double[] expectedOwner;
        OpenObjective(undisturbed);
        try { expectedOwner = Predictions(undisturbed, false); }
        finally { CloseObjective(undisturbed); }

        var changed = new[] { ParameterType.Affinity1, ParameterType.Affinity2, ParameterType.Nvalue1,
            ParameterType.Nvalue2, ParameterType.Enthalpy1, ParameterType.Enthalpy2 };
        var originalParameters = changeParameters
            ? changed.ToDictionary(key => key, key => model.Parameters.Table[key].Value)
            : new Dictionary<ParameterType, double>();
        var ownerResults = new double[model.Data.Injections.Count];
        var split = Math.Max(1, ownerResults.Length / 2);
        Assert.True(split < ownerResults.Length);
        OpenObjective(model);
        try
        {
            for (var i = 0; i < split; i++) ownerResults[i] = model.Evaluate(model.Data.Injections[i].ID);
            var before = CaptureOwnerState(model);
            try
            {
                if (changeParameters)
                    foreach (var key in changed)
                    {
                        var value = originalParameters[key];
                        var foreignValue = key == ParameterType.Affinity1 || key == ParameterType.Affinity2
                            ? value + .4 : value * .7;
                        model.Parameters.Table[key].Update(foreignValue);
                        reference.Parameters.Table[key].Update(foreignValue);
                    }
                var expectedForeign = Predictions(reference, enthalpy);
                if (changeParameters)
                    Assert.False(Bits(expectedForeign).SequenceEqual(Bits(Predictions(NewModel(), enthalpy))));
                double[] foreign = null;
                Exception failure = null;
                var thread = new Thread(() =>
                {
                    try { foreign = Predictions(model, enthalpy); }
                    catch (Exception ex) { failure = ex; }
                });
                thread.Start();
                thread.Join(); // Owner remains paused until every foreign prediction has completed.
                if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
                Assert.Equal(Bits(expectedForeign), Bits(foreign));
                AssertOwnerStateUnchanged(before, CaptureOwnerState(model));
            }
            finally
            {
                foreach (var pair in originalParameters) model.Parameters.Table[pair.Key].Update(pair.Value);
            }
            for (var i = split; i < ownerResults.Length; i++) ownerResults[i] = model.Evaluate(model.Data.Injections[i].ID);
            Assert.Equal(Bits(expectedOwner), Bits(ownerResults));
        }
        finally { CloseObjective(model); }
    }

    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static void OpenObjective(Model model) => model.GetType()
        .GetMethod("BeginObjectiveEvaluation", PrivateInstance)!.Invoke(model, null);

    static void CloseObjective(Model model) => model.GetType()
        .GetMethod("EndObjectiveEvaluation", PrivateInstance)!.Invoke(model, null);

    sealed record OwnerState(Dictionary<string, object> References, Dictionary<string, object> Values);

    static OwnerState CaptureOwnerState(Model model)
    {
        var references = new Dictionary<string, object>();
        var values = new Dictionary<string, object>();
        void Capture(string path, object value)
        {
            if (value == null) { values.Add(path, null); return; }
            var type = value.GetType();
            if (value is double number) { values.Add(path, BitConverter.DoubleToInt64Bits(number)); return; }
            if (type.IsPrimitive || type.IsEnum) { values.Add(path, value); return; }
            if (!type.IsValueType) references.Add(path, value);
            if (value is Array array)
            {
                values.Add(path + ".Length", array.Length);
                for (var i = 0; i < array.Length; i++) Capture(path + "[" + i + "]", array.GetValue(i));
                return;
            }
            foreach (var field in type.GetFields(PrivateInstance)) Capture(path + "." + field.Name, field.GetValue(value));
        }
        var context = model.GetType().GetField("evaluation", PrivateInstance)!.GetValue(model);
        Assert.NotNull(context);
        Capture("evaluation", context);
        // Include the old model-level prepared fields so this test also rejects a guard-only fix.
        foreach (var name in new[] { "Kd1", "Kd2", "N1", "N2" })
        {
            var field = model.GetType().GetField(name, PrivateInstance);
            if (field != null) Capture("model." + name, field.GetValue(model));
        }
        return new OwnerState(references, values);
    }

    static void AssertOwnerStateUnchanged(OwnerState before, OwnerState after)
    {
        Assert.Equal(before.References.Keys.OrderBy(k => k), after.References.Keys.OrderBy(k => k));
        foreach (var pair in before.References) Assert.Same(pair.Value, after.References[pair.Key]);
        Assert.Equal(before.Values.Keys.OrderBy(k => k), after.Values.Keys.OrderBy(k => k));
        foreach (var pair in before.Values) Assert.Equal(pair.Value, after.Values[pair.Key]);
    }
}
