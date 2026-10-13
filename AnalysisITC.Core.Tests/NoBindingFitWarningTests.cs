using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Viewer;

using Xunit;

namespace AnalysisITC.Core.Tests;

// ITC-053: fit warnings of members assessed as no binding are annotated as not counted in result health,
// and their parameter-bound contacts are left out of the status-bar warning.
[Collection(SolverEventCollectionDefinition.Name)]
public sealed class NoBindingFitWarningTests
{
    const string BestFit = "Best fit reached a parameter boundary.";
    const string NotCounted = "Not counted in result health: no binding detected.";

    [Fact]
    public async Task NoBindingMemberWarningsAreAnnotatedAsNotCounted()
    {
        var (result, binding, flat) = await IndependentResult();

        Assert.Equal(new[] { BestFit }, Messages(result, binding));
        Assert.Equal(new[] { BestFit, NotCounted }, Messages(result, flat));
    }

    [Fact]
    public async Task ManualAssessmentsDecideWhetherWarningsCount()
    {
        var (result, binding, flat) = await IndependentResult();

        result.SetMemberBindingAssessmentOverride(flat.Guid, BindingAssessmentOutcome.BindingDetected);
        result.SetMemberBindingAssessmentOverride(binding.Guid, BindingAssessmentOutcome.NoBindingDetected);

        Assert.Equal(new[] { BestFit, NotCounted }, Messages(result, binding));
        Assert.Equal(new[] { BestFit }, Messages(result, flat));
    }

    [Fact]
    public async Task MemberWithoutWarningsGetsNoAnnotation()
    {
        var (result, _, flat) = await IndependentResult(markBoundaryHits: false);

        Assert.Empty(Messages(result, flat));
    }

    [Fact]
    public async Task StatusWarningOmitsContactsOfNoBindingMembers()
    {
        var (result, binding, flat) = await IndependentResult();
        var contacts = new[]
        {
            Local(binding, "Binding", ParameterBoundarySide.Lower),
            Local(flat, "Flat", ParameterBoundarySide.Upper),
        };

        Assert.Equal("Warning: parameter bound reached: Enthalpy (lower, Binding)",
            ParameterBoundaryWarningFormatter.Format(contacts, result.MemberAssessments));

        // Only No binding detected is left out; a member without an assessment still counts.
        var notAssessed = new[] { new BindingAssessmentMember(flat, BindingAssessmentState.FromComparison(null), null) };
        Assert.Equal("Warning: parameter bound reached: Enthalpy (lower, Binding), Enthalpy (upper, Flat)",
            ParameterBoundaryWarningFormatter.Format(contacts, notAssessed));

        result.SetMemberBindingAssessmentOverride(flat.Guid, BindingAssessmentOutcome.NoBindingDetected);
        result.SetMemberBindingAssessmentOverride(binding.Guid, BindingAssessmentOutcome.NoBindingDetected);
        Assert.Equal("", ParameterBoundaryWarningFormatter.Format(contacts, result.MemberAssessments));
    }

    [Fact]
    public async Task SharedContactsFollowThePooledAssessment()
    {
        var shared = new[] { Shared() };

        var flatPool = await Pooled(Flat("Flat 1"), Flat("Flat 2"));
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, new AnalysisResult(flatPool).BindingAssessment.EffectiveOutcome);
        Assert.Equal("", ParameterBoundaryWarningFormatter.Format(shared, BindingAssessmentMember.ForFit(flatPool)));

        var bindingPool = await Pooled(Binding("Binding 1"), Binding("Binding 2"));
        Assert.Equal(BindingAssessmentOutcome.BindingDetected, new AnalysisResult(bindingPool).BindingAssessment.EffectiveOutcome);
        Assert.Equal("Warning: parameter bound reached: Enthalpy (lower)",
            ParameterBoundaryWarningFormatter.Format(shared, BindingAssessmentMember.ForFit(bindingPool)));
    }

    [Fact]
    public async Task FitAssessmentsMatchANewResult()
    {
        var binding = Binding("Binding");
        var flat = Flat("Flat");
        var global = Independent(binding, flat);
        await Run(new GlobalSolver { Model = global, ErrorEstimationMethod = ErrorEstimationMethod.None, CanCreateAnalysisResult = false });

        var fit = BindingAssessmentMember.ForFit(global.Solution);

        Assert.Equal(new[] { BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.NoBindingDetected },
            fit.Select(member => member.Assessment.EffectiveOutcome));
        Assert.Equal(new AnalysisResult(global.Solution).MemberAssessments.Select(member => member.Assessment.EffectiveOutcome),
            fit.Select(member => member.Assessment.EffectiveOutcome));

        var single = Flat("Single");
        await Run(new Solver { Model = single, ErrorEstimationMethod = ErrorEstimationMethod.None, CanCreateAnalysisResult = false });
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected,
            Assert.Single(BindingAssessmentMember.ForFit(single.Solution)).Assessment.EffectiveOutcome);
    }

    [Fact]
    public async Task ViewerResultWarningsCarryTheAnnotation()
    {
        var (result, _, _) = await IndependentResult();
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, result.Solution.Solutions.Select(member => member.Data), new[] { result });
        package.Position = 0;

        var document = await new ViewerDocumentReader().ReadAsync(package, "itc-053.ftxtc", ViewerFileFormat.Ftxtc);

        var warnings = Assert.Single(document.AnalysisResults).Warnings;
        Assert.Contains("Binding: " + BestFit, warnings);
        Assert.DoesNotContain("Binding: " + NotCounted, warnings);
        Assert.Contains("Flat: " + BestFit, warnings);
        Assert.Contains("Flat: " + NotCounted, warnings);
    }

    static string[] Messages(AnalysisResult result, SolutionInterface member)
        => ParameterBoundaryWarningFormatter.ResultMemberMessages(result, member, ErrorEstimationMethod.None).ToArray();

    static async Task<(AnalysisResult result, SolutionInterface binding, SolutionInterface flat)> IndependentResult(bool markBoundaryHits = true)
    {
        var binding = Binding("Binding");
        var flat = Flat("Flat");
        var global = Independent(binding, flat);
        await Run(new GlobalSolver { Model = global, ErrorEstimationMethod = ErrorEstimationMethod.None, CanCreateAnalysisResult = false });
        var result = new AnalysisResult(global.Solution);
        var bindingSolution = result.Solution.Solutions.Single(member => member.Data.UniqueID == binding.Data.UniqueID);
        var flatSolution = result.Solution.Solutions.Single(member => member.Data.UniqueID == flat.Data.UniqueID);
        Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.GetMemberBindingAssessment(bindingSolution).EffectiveOutcome);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, result.GetMemberBindingAssessment(flatSolution).EffectiveOutcome);
        if (markBoundaryHits)
        {
            bindingSolution.RestoreParameterBoundaryHit(true);
            flatSolution.RestoreParameterBoundaryHit(true);
        }
        return (result, bindingSolution, flatSolution);
    }

    static ParameterBoundaryContact Local(SolutionInterface member, string name, ParameterBoundarySide side)
        => new ParameterBoundaryContact(ParameterType.Enthalpy1, ParameterBoundaryScope.Local, member.Data.UniqueID, name, side, 0, 0);

    static ParameterBoundaryContact Shared()
        => new ParameterBoundaryContact(ParameterType.Enthalpy1, ParameterBoundaryScope.Shared, null, null, ParameterBoundarySide.Lower, 0, 0);

    static Model Binding(string name)
    {
        var model = InjectionProcessingMethodTests.FittedModel();
        model.Data.SetID(name + "-" + model.Data.UniqueID);
        model.Data.Name = name;
        return model;
    }

    /// <summary>The binding reference with every heat replaced by a nearly constant value.</summary>
    static Model Flat(string name)
    {
        var model = Binding(name);
        var mean = model.Data.Injections.Average(injection => injection.PeakArea.Value);
        foreach (var injection in model.Data.Injections)
            injection.SetPeakArea(new FloatWithError(mean * (1 + 0.01 * Math.Sin(injection.ID)), injection.PeakArea.SD));
        return model;
    }

    static GlobalModel Independent(params Model[] members)
    {
        var global = new GlobalModel(members.ToList())
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
        };
        foreach (var member in members) global.Parameters.AddIndivdualParameter(member.Parameters);
        return global;
    }

    static async Task<GlobalSolution> Pooled(params Model[] members)
    {
        var global = Independent(members);
        global.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, VariableConstraint.SameForAll);
        global.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, members[0].Parameters.Table[ParameterType.Enthalpy1].Value);
        await Run(new GlobalSolver { Model = global, ErrorEstimationMethod = ErrorEstimationMethod.None, CanCreateAnalysisResult = false });
        return global.Solution;
    }

    static async Task<SolverConvergence> Run(SolverInterface solver)
    {
        var finished = new TaskCompletionSource<SolverConvergence>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SolverConvergence> handler = (sender, value) =>
        {
            if (ReferenceEquals(sender, solver)) finished.TrySetResult(value);
        };
        SolverInterface.AnalysisFinished += handler;
        try
        {
            SolverInterface.TerminateAnalysisFlag.Lower();
            solver.Analyze();
            return await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            SolverInterface.AnalysisFinished -= handler;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }
}
