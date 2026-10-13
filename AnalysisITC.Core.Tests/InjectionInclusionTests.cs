using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(AnalysisBuilderConstraintCollectionDefinition.Name)]
public sealed class InjectionInclusionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InclusionAndNotificationsStayUnchangedUntilAllProcessingFinishes(bool included)
    {
        var experiment = new ExperimentData("inclusion.itc");
        var injection = new InjectionData(experiment, 0, 2e-6, 1e-9, include: included);
        experiment.Injections.Add(injection);
        experiment.MarkClean();
        var changes = 0;
        experiment.InjectionIncludeChanged += (_, _) => changes++;

        using (ProcessingActivity.Begin())
        {
            using (ProcessingActivity.Begin())
            {
                injection.ToggleDataPointActive();
                Assert.Equal(included, injection.Include);
                Assert.Equal(0, changes);
                Assert.False(experiment.IsModified);
            }
            injection.ToggleDataPointActive();
            Assert.Equal(included, injection.Include);
            Assert.Equal(0, changes);
            Assert.False(experiment.IsModified);
        }

        // Rejected edits are not queued for later application.
        Assert.Equal(included, injection.Include);
        injection.ToggleDataPointActive();
        Assert.Equal(!included, injection.Include);
        Assert.Equal(1, changes);
        Assert.True(experiment.IsModified);
    }
}
