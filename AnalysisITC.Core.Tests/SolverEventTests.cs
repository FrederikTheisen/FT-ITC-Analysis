using System;

using AnalysisITC.Core.Analysis;

using Xunit;

namespace AnalysisITC.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SolverEventCollectionDefinition
{
    public const string Name = "Solver events";
}

[Collection(SolverEventCollectionDefinition.Name)]
public sealed class SolverEventTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void SilentSolversDoNotReportAnalysisStepFinished(bool silent, int expectedEvents)
    {
        var solver = new Solver { Silent = silent };
        var eventCount = 0;
        EventHandler handler = (_, _) => eventCount++;
        SolverInterface.AnalysisStepFinished += handler;

        try
        {
            solver.ReportAnalysisStepFinished();

            Assert.Equal(expectedEvents, eventCount);
        }
        finally
        {
            SolverInterface.AnalysisStepFinished -= handler;
        }
    }
}
