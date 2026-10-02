using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Avalonia;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

public sealed class StartupFileActivationCoordinatorTests
{
    [Fact]
    public async Task ConfirmationRecoveryAndQueuedReadsFinishBeforeReadiness()
    {
        var coordinator = new StartupFileActivationCoordinator();
        var order = new List<string>();
        var batches = new List<string[]>();
        var activeReads = 0;
        var maxConcurrentReads = 0;
        var completion = coordinator.Request(new[] { "/tmp/from-menu.ftxtc" });
        coordinator.QueueActivation(new[] { "/tmp/initial.ftxtc", "/tmp/initial.ftxtc" });

        var continued = await coordinator.InitializeAsync(
            async () =>
            {
                order.Add("confirm");
                coordinator.QueueActivation(new[] { "/tmp/during-confirmation.ftxtc" });
                await Task.Yield();
                return true;
            },
            async () =>
            {
                order.Add("recovery");
                coordinator.QueueActivation(new[] { "/tmp/during-recovery.ftxtc" });
                await Task.Yield();
            },
            async paths =>
            {
                activeReads++;
                maxConcurrentReads = System.Math.Max(maxConcurrentReads, activeReads);
                batches.Add(paths.ToArray());
                order.Add("read");
                Assert.False(coordinator.IsReady);
                if (batches.Count == 1)
                    coordinator.QueueActivation(new[] { "/tmp/during-read.ftxtc" });
                await Task.Yield();
                activeReads--;
            },
            () =>
            {
                order.Add("ready");
                Assert.True(coordinator.IsReady);
            });

        Assert.True(continued);
        Assert.True(await completion);
        Assert.Equal(new[] { "confirm", "recovery", "read", "read", "ready" }, order);
        Assert.Equal(1, maxConcurrentReads);
        Assert.Equal(new[]
        {
            new[] { "/tmp/from-menu.ftxtc", "/tmp/initial.ftxtc", "/tmp/during-confirmation.ftxtc", "/tmp/during-recovery.ftxtc" },
            new[] { "/tmp/during-read.ftxtc" }
        }, batches);
        Assert.True(coordinator.IsReady);
    }

    [Fact]
    public async Task ClosingTraceabilityConfirmationDiscardsQueuedPathsWithoutRecovery()
    {
        var coordinator = new StartupFileActivationCoordinator();
        coordinator.QueueActivation(new[] { "/tmp/pending.ftxtc" });
        var request = coordinator.Request(new[] { "/tmp/requested.ftxtc" });
        var recoveryCalled = false;
        var readCalled = false;

        var continued = await coordinator.InitializeAsync(
            () => Task.FromResult(false),
            () => { recoveryCalled = true; return Task.CompletedTask; },
            _ => { readCalled = true; return Task.CompletedTask; },
            () => { });

        Assert.False(continued);
        Assert.False(await request);
        Assert.False(recoveryCalled);
        Assert.False(readCalled);
        Assert.False(coordinator.IsReady);
        Assert.False(await coordinator.Request(new[] { "/tmp/late.ftxtc" }));
    }
}
