using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class StatusBarManagerCollection
    {
        public const string Name = "StatusBarManager";
    }

    [Collection(StatusBarManagerCollection.Name)]
    public sealed class StatusBarManagerTests : IDisposable
    {
        private readonly ConcurrentQueue<string> statuses = new();

        public StatusBarManagerTests()
        {
            StatusBarManager.ClearAppStatus();
            StatusBarManager.StatusUpdated += OnStatusUpdated;
        }

        public void Dispose()
        {
            StatusBarManager.StatusUpdated -= OnStatusUpdated;
            StatusBarManager.ClearAppStatus();
        }

        [Fact]
        public async Task QueuedStatusesDisplayInFirstInFirstOutOrder()
        {
            StatusBarManager.QueueStatus("queue-first", 80);
            StatusBarManager.QueueStatus("queue-second", 80);

            await WaitUntilAsync(() => statuses.Contains("queue-second"));

            var queuedUpdates = statuses
                .Where(status => status == "queue-first" || status == "queue-second")
                .Distinct()
                .ToArray();
            Assert.Equal(new[] { "queue-first", "queue-second" }, queuedUpdates);
        }

        [Fact]
        public async Task QueuedStatusPausesWhileAnEqualPriorityStatusIsVisible()
        {
            var updates = new ConcurrentQueue<(string Message, long Elapsed)>();
            var stopwatch = Stopwatch.StartNew();
            EventHandler<string> handler = (_, message) => updates.Enqueue((message, stopwatch.ElapsedMilliseconds));
            StatusBarManager.StatusUpdated += handler;

            try
            {
                StatusBarManager.QueueStatus("queue-paused", 120);
                await WaitUntilAsync(() => updates.Any(update => update.Message == "queue-paused"));
                await Task.Delay(40);

                StatusBarManager.SetStatus("queue-interrupt", 90);
                await WaitUntilAsync(() => updates.Any(update => update.Message == "queue-interrupt"));
                await WaitUntilAsync(() => updates.Count(update => update.Message == "queue-paused") >= 2);
                await WaitUntilAsync(() =>
                    updates.Any(update => update.Elapsed >= 170
                        && update.Message != "queue-paused"
                        && update.Message != "queue-interrupt"));

                var completedAt = updates
                    .Where(update => update.Message != "queue-paused" && update.Message != "queue-interrupt")
                    .Max(update => update.Elapsed);
                Assert.True(completedAt >= 170, $"Queued status completed after only {completedAt} ms.");
            }
            finally
            {
                StatusBarManager.StatusUpdated -= handler;
            }
        }

        [Fact]
        public async Task LowerPriorityStatusDoesNotInterruptQueuedStatus()
        {
            StatusBarManager.QueueStatus("queue-priority", 120, priority: 2);
            await WaitUntilAsync(() => statuses.Contains("queue-priority"));

            StatusBarManager.SetStatus("lower-priority", 50, priority: 1);
            await Task.Delay(150);

            Assert.DoesNotContain("lower-priority", statuses);
        }

        [Fact]
        public async Task ClearAppStatusCancelsActiveAndPendingQueuedStatuses()
        {
            StatusBarManager.QueueStatus("queue-active", 150);
            StatusBarManager.QueueStatus("queue-pending", 50);
            await WaitUntilAsync(() => statuses.Contains("queue-active"));

            StatusBarManager.ClearAppStatus();
            var updateCountAfterClear = statuses.Count;
            await Task.Delay(220);

            Assert.DoesNotContain("queue-pending", statuses);
            Assert.Equal(updateCountAfterClear, statuses.Count);
        }

        [Fact]
        public async Task EnqueueingWhileActiveAppendsWithoutRestartingCurrentStatus()
        {
            StatusBarManager.QueueStatus("queue-current", 100);
            await WaitUntilAsync(() => statuses.Contains("queue-current"));
            await Task.Delay(30);

            StatusBarManager.QueueStatus("queue-appended", 50);
            await WaitUntilAsync(() => statuses.Contains("queue-appended"));

            Assert.Equal(1, statuses.Count(status => status == "queue-current"));
            Assert.Equal(1, statuses.Count(status => status == "queue-appended"));
        }

        [Fact]
        public async Task DirectTimedStatusesKeepExistingReplacementBehavior()
        {
            StatusBarManager.SetStatus("direct-long", 130);
            StatusBarManager.SetStatus("direct-short", 50);

            await WaitUntilAsync(() => statuses.Count(status => status == "direct-long") >= 2);

            var directUpdates = statuses
                .Where(status => status == "direct-long" || status == "direct-short")
                .Take(3)
                .ToArray();
            Assert.Equal(new[] { "direct-long", "direct-short", "direct-long" }, directUpdates);
        }

        [Fact]
        public void QueuedStatusRequiresAPositiveDuration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StatusBarManager.QueueStatus("invalid", 0));
        }

        private void OnStatusUpdated(object sender, string status) => statuses.Enqueue(status);

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 2000)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(stopwatch.ElapsedMilliseconds < timeoutMilliseconds, "Timed out waiting for a status update.");
                await Task.Delay(10);
            }
        }
    }
}
