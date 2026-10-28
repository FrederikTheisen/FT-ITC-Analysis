using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Avalonia;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

public sealed class StartupFileActivationCoordinatorTests
{
    [Fact]
    public async Task DefaultPolicyPreservesCaseDistinctPathsExceptOnWindows()
    {
        var coordinator = new StartupFileActivationCoordinator();
        var first = Path.Combine(Path.GetTempPath(), "run.itc");
        var second = Path.Combine(Path.GetTempPath(), "Run.itc");
        var batches = new List<string[]>();
        var request = coordinator.Request(new[] { first, second, first });

        await coordinator.InitializeAsync(
            () => Task.FromResult(true),
            () => Task.CompletedTask,
            paths => { batches.Add(paths.ToArray()); return Task.CompletedTask; },
            () => { });

        Assert.True(await request);
        Assert.Equal(OperatingSystem.IsWindows() ? new[] { first } : new[] { first, second },
            Assert.Single(batches));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaseDistinctPathsFollowComparerAndPreserveFirstOccurrence(bool ignoreCase)
    {
        var coordinator = new StartupFileActivationCoordinator(
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var first = Path.Combine(Path.GetTempPath(), "Run.itc");
        var second = Path.Combine(Path.GetTempPath(), "run.itc");
        var third = Path.Combine(Path.GetTempPath(), "other.itc");
        var batches = new List<string[]>();
        var request = coordinator.Request(new[] { first, second, first, third, second });

        await InitializeAsync(coordinator, batches);

        Assert.True(await request);
        Assert.Equal(ignoreCase ? new[] { first, third } : new[] { first, second, third },
            Assert.Single(batches));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelativeAndAbsolutePathsAreNormalizedBeforeDeduplication(bool ignoreCase)
    {
        var coordinator = new StartupFileActivationCoordinator(
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var relative = Path.Combine("path-normalization", "run.itc");
        var absolute = Path.GetFullPath(relative);
        var withParent = Path.Combine(Environment.CurrentDirectory, "path-normalization", "nested", "..", "run.itc");
        var batches = new List<string[]>();
        var request = coordinator.Request(new[] { relative, absolute, withParent });

        await InitializeAsync(coordinator, batches);

        Assert.True(await request);
        Assert.Equal(new[] { absolute }, Assert.Single(batches));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingRequestsAndActivationsUseOneDeduplicatedBatch(bool ignoreCase)
    {
        var coordinator = new StartupFileActivationCoordinator(
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var first = Path.Combine(Path.GetTempPath(), "run.itc");
        var second = Path.Combine(Path.GetTempPath(), "Run.itc");
        var third = Path.Combine(Path.GetTempPath(), "other.itc");
        var batches = new List<string[]>();
        var firstRequest = coordinator.Request(new[] { first, first });
        coordinator.QueueActivation(new[] { second, first });
        var secondRequest = coordinator.Request(new[] { second, third });

        await InitializeAsync(coordinator, batches);

        Assert.True(await firstRequest);
        Assert.True(await secondRequest);
        Assert.Equal(ignoreCase ? new[] { first, third } : new[] { first, second, third },
            Assert.Single(batches));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedBatchDoesNotSuppressLaterImport(bool ignoreCase)
    {
        var coordinator = new StartupFileActivationCoordinator(
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var first = Path.Combine(Path.GetTempPath(), "run.itc");
        var second = Path.Combine(Path.GetTempPath(), "Run.itc");
        var batches = new List<string[]>();
        await InitializeAsync(coordinator, batches);

        Assert.True(await coordinator.Request(new[] { first, second, first }));
        Assert.True(await coordinator.Request(new[] { first, second, first }));

        var expected = ignoreCase ? new[] { first } : new[] { first, second };
        Assert.Equal(new[] { expected, expected }, batches);
        Assert.True(coordinator.IsReady);
    }

    static Task<bool> InitializeAsync(StartupFileActivationCoordinator coordinator, List<string[]> batches) =>
        coordinator.InitializeAsync(
            () => Task.FromResult(true),
            () => Task.CompletedTask,
            paths => { batches.Add(paths.ToArray()); return Task.CompletedTask; },
            () => { });

    [Fact]
    public async Task ConfirmationRecoveryAndQueuedReadsFinishBeforeReadiness()
    {
        var coordinator = new StartupFileActivationCoordinator();
        var order = new List<string>();
        var batches = new List<string[]>();
        var activeReads = 0;
        var maxConcurrentReads = 0;
        var fromMenu = Path.Combine(Path.GetTempPath(), "from-menu.ftxtc");
        var initial = Path.Combine(Path.GetTempPath(), "initial.ftxtc");
        var duringConfirmation = Path.Combine(Path.GetTempPath(), "during-confirmation.ftxtc");
        var duringRecovery = Path.Combine(Path.GetTempPath(), "during-recovery.ftxtc");
        var duringRead = Path.Combine(Path.GetTempPath(), "during-read.ftxtc");
        var completion = coordinator.Request(new[] { fromMenu });
        coordinator.QueueActivation(new[] { initial, initial });

        var continued = await coordinator.InitializeAsync(
            async () =>
            {
                order.Add("confirm");
                coordinator.QueueActivation(new[] { duringConfirmation });
                await Task.Yield();
                return true;
            },
            async () =>
            {
                order.Add("recovery");
                coordinator.QueueActivation(new[] { duringRecovery });
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
                    coordinator.QueueActivation(new[] { duringRead });
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
            new[] { fromMenu, initial, duringConfirmation, duringRecovery },
            new[] { duringRead }
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
