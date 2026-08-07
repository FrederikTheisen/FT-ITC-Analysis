using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Application
{
    public static class StatusBarManager
    {
        static StatusMessage DefaultStatus => new StatusMessage(StateManager.ProgramStateString, false);

        private static readonly object statusLock = new();
        private static readonly List<StatusMessage> status = new();
        private static readonly Queue<QueuedStatusMessage> queuedStatuses = new();
        private static TaskCompletionSource<bool> statusStateChanged = CreateStatusStateChangedSignal();
        private static CancellationTokenSource queueCancellation;
        private static QueuedStatusMessage activeQueuedStatus;
        private static string defaultSecondaryStatus = "";
        private static string transientSecondaryStatus = "";
        private static bool abortscroll;
        private static int secondaryStatusSuppressionCount;
        private static ProgressIndicatorEventData progressstate = new ProgressIndicatorEventData(1);

        public static event EventHandler UpdateContextButton;
        public static event EventHandler<string> StatusUpdated;
        public static event EventHandler<string> SecondaryStatusUpdated;
        public static event EventHandler<ProgressIndicatorEventData> ProgressUpdate;

        private static StatusMessage Status
        {
            get
            {
                lock (statusLock)
                {
                    return GetStatusLocked();
                }
            }
            set => SetStatusMessage(value);
        }

        public static void Invalidate() => StatusUpdated?.Invoke(null, Status.Message);

        static string SecondaryStatus
        {
            get
            {
                if (secondaryStatusSuppressionCount > 0) return "";
                if (!string.IsNullOrEmpty(transientSecondaryStatus)) return transientSecondaryStatus;
                return defaultSecondaryStatus;
            }
        }

        public static double Progress
        {
            private set
            {
                progressstate.Progress = value;

                ProgressUpdate?.Invoke(null, progressstate);
            }
            get
            {
                return progressstate.Progress;
            }
        }

        public static async void SetStatus(string msg, int delay = 10000, int priority = 0)
        {
            abortscroll = true;

            if (delay > 0)
            {
                var sm = new StatusMessage(msg, true, priority);

                Status = sm;

                await Task.Delay(delay);

                StatusExpired(sm);
            }
            else
            {
                Status = new StatusMessage(msg, false, priority);
            }
        }

        public static void QueueStatus(string message, int duration = 3000, int priority = 0)
        {
            if (duration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "Queued status duration must be greater than zero.");
            }

            abortscroll = true;

            CancellationTokenSource queueToStart = null;
            lock (statusLock)
            {
                queuedStatuses.Enqueue(new QueuedStatusMessage(message, duration, priority));
                if (queueCancellation == null)
                {
                    queueCancellation = new CancellationTokenSource();
                    queueToStart = queueCancellation;
                }
            }

            if (queueToStart != null) _ = ProcessStatusQueueAsync(queueToStart);
        }

        private static void StatusExpired(StatusMessage sm)
        {
            TaskCompletionSource<bool> stateChanged = null;
            string message = null;
            lock (statusLock)
            {
                if (status.Remove(sm))
                {
                    stateChanged = MarkStatusStateChangedLocked();
                    message = GetStatusLocked().Message;
                }
            }

            if (stateChanged == null) return;
            stateChanged.TrySetResult(true);
            PublishStatus(message);
        }

        public static void ClearAppStatus()
        {
            // Do not remember why this priority thing is here....
            //var priority = Status.Priority;
            //status.RemoveAll(s => s.Priority >= priority);
            CancellationTokenSource queueToCancel;
            TaskCompletionSource<bool> stateChanged;
            string message;
            lock (statusLock)
            {
                status.Clear();
                queuedStatuses.Clear();
                activeQueuedStatus = null;
                queueToCancel = queueCancellation;
                queueCancellation = null;
                stateChanged = MarkStatusStateChangedLocked();
                message = GetStatusLocked().Message;
            }

            queueToCancel?.Cancel();
            stateChanged.TrySetResult(true);
            StopIndeterminateProgress();
            PublishStatus(message);

            transientSecondaryStatus = "";
            PublishSecondaryStatus();
        }

        public static void SetProgress(double progress)
        {
            Progress = progress;
        }

        public static async void SetStatusScrolling(string status, int scrollcount = 2, int scrollspeed = 10)
        {
            var tmp = status;

            abortscroll = false;
            secondaryStatusSuppressionCount++;
            PublishSecondaryStatus();

            try
            {
                for (int i = 0; i < scrollcount; i++)
                {
                    tmp = status;

                    Status = new StatusMessage(tmp, false);
                    await Task.Delay(2000);

                    while (tmp.Length > 35)
                    {
                        Status = new StatusMessage(tmp, false);
                        tmp = tmp.Substring(1);

                        await Task.Delay(1000 / scrollspeed);

                        if (abortscroll) break;
                    }

                    if (abortscroll) break;
                    await Task.Delay(2000);
                    if (abortscroll) break;
                }
            }
            finally
            {
                secondaryStatusSuppressionCount = Math.Max(0, secondaryStatusSuppressionCount - 1);
                PublishSecondaryStatus();
            }

            ClearAppStatus();
        }

        public static async void SetSecondaryStatus(string status, int delay = 20000)
        {
            transientSecondaryStatus = status ?? "";
            PublishSecondaryStatus();

            string c = transientSecondaryStatus;

            if (delay > 0)
            {
                await Task.Delay(delay);

                if (transientSecondaryStatus == c)
                {
                    transientSecondaryStatus = "";
                    PublishSecondaryStatus();
                }
            }
        }

        public static void SetDefaultSecondaryStatus(string status)
        {
            defaultSecondaryStatus = status ?? "";
            PublishSecondaryStatus();
        }

        public static void StartInderminateProgress()
        {
            Progress = -0.5;
        }

        public static void StopIndeterminateProgress()
        {
            Progress = -1;
        }

        public static void SetSavingFileMessage()
        {
            StartInderminateProgress();

            SetStatus("Saving File...");
        }

        public static void SetFileSaveSuccessfulMessage(string path)
        {
            ClearAppStatus();

            SetStatusScrolling("File Saved: " + path);
        }

        static void PublishSecondaryStatus()
        {
            SecondaryStatusUpdated?.Invoke(null, SecondaryStatus);
        }

        private static async Task ProcessStatusQueueAsync(CancellationTokenSource owner)
        {
            try
            {
                while (!owner.IsCancellationRequested)
                {
                    TaskCompletionSource<bool> stateChanged;
                    string message;
                    lock (statusLock)
                    {
                        if (!ReferenceEquals(queueCancellation, owner) || queuedStatuses.Count == 0)
                        {
                            if (ReferenceEquals(queueCancellation, owner)) queueCancellation = null;
                            return;
                        }

                        activeQueuedStatus = queuedStatuses.Dequeue();
                        status.Add(activeQueuedStatus.Status);
                        stateChanged = MarkStatusStateChangedLocked();
                        message = GetStatusLocked().Message;
                    }

                    stateChanged.TrySetResult(true);
                    PublishStatus(message);

                    await DisplayActiveQueuedStatusAsync(owner);
                    if (owner.IsCancellationRequested) return;

                    bool publishRemoval;
                    lock (statusLock)
                    {
                        if (!ReferenceEquals(queueCancellation, owner)) return;

                        if (activeQueuedStatus != null) status.Remove(activeQueuedStatus.Status);
                        activeQueuedStatus = null;
                        publishRemoval = queuedStatuses.Count == 0;
                        stateChanged = MarkStatusStateChangedLocked();
                        message = GetStatusLocked().Message;
                    }

                    stateChanged.TrySetResult(true);
                    if (publishRemoval) PublishStatus(message);
                }
            }
            catch (OperationCanceledException) when (owner.IsCancellationRequested)
            {
            }
            finally
            {
                lock (statusLock)
                {
                    if (ReferenceEquals(queueCancellation, owner)) queueCancellation = null;
                }
                owner.Dispose();
            }
        }

        private static async Task DisplayActiveQueuedStatusAsync(CancellationTokenSource owner)
        {
            TimeSpan remaining;
            lock (statusLock)
            {
                remaining = activeQueuedStatus.RemainingDuration;
            }

            while (remaining > TimeSpan.Zero)
            {
                Task stateChangeTask;
                bool isVisible;
                lock (statusLock)
                {
                    if (!ReferenceEquals(queueCancellation, owner) || activeQueuedStatus == null) return;
                    isVisible = ReferenceEquals(GetStatusLocked(), activeQueuedStatus.Status);
                    stateChangeTask = statusStateChanged.Task;
                }

                if (!isVisible)
                {
                    await WaitForStateChangeAsync(stateChangeTask, owner.Token);
                    continue;
                }

                var stopwatch = Stopwatch.StartNew();
                using (var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token))
                {
                    var delayTask = Task.Delay(remaining, delayCancellation.Token);
                    var completedTask = await Task.WhenAny(delayTask, stateChangeTask);
                    stopwatch.Stop();
                    remaining -= stopwatch.Elapsed;

                    if (completedTask == delayTask)
                    {
                        await delayTask;
                        remaining = TimeSpan.Zero;
                    }
                    else
                    {
                        delayCancellation.Cancel();
                    }
                }

                lock (statusLock)
                {
                    if (ReferenceEquals(queueCancellation, owner) && activeQueuedStatus != null)
                    {
                        activeQueuedStatus.RemainingDuration = remaining;
                    }
                }
            }
        }

        private static async Task WaitForStateChangeAsync(Task stateChangeTask, CancellationToken cancellationToken)
        {
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var cancellationTask = Task.Delay(Timeout.Infinite, cancellation.Token);
                var completedTask = await Task.WhenAny(stateChangeTask, cancellationTask);
                if (completedTask == cancellationTask) await cancellationTask;
                cancellation.Cancel();
            }
        }

        private static void SetStatusMessage(StatusMessage value)
        {
            TaskCompletionSource<bool> stateChanged;
            string message;
            lock (statusLock)
            {
                status.RemoveAll(o => o.Timed == false && o.Priority < value.Priority);
                status.Add(value);
                stateChanged = MarkStatusStateChangedLocked();
                message = GetStatusLocked().Message;
            }

            stateChanged.TrySetResult(true);
            PublishStatus(message);
        }

        private static StatusMessage GetStatusLocked()
        {
            if (status.Count != 0) return status.OrderBy(sm => sm.Priority).Last();
            return DefaultStatus;
        }

        private static TaskCompletionSource<bool> MarkStatusStateChangedLocked()
        {
            var previous = statusStateChanged;
            statusStateChanged = CreateStatusStateChangedSignal();
            return previous;
        }

        private static TaskCompletionSource<bool> CreateStatusStateChangedSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static void PublishStatus(string message) => StatusUpdated?.Invoke(null, message);

        private sealed class QueuedStatusMessage
        {
            public QueuedStatusMessage(string message, int duration, int priority)
            {
                Status = new StatusMessage(message, true, priority);
                RemainingDuration = TimeSpan.FromMilliseconds(duration);
            }

            public StatusMessage Status { get; }
            public TimeSpan RemainingDuration { get; set; }
        }

        private sealed class StatusMessage
        {
            public StatusMessage(string msg, bool time, int priority = 0)
            {
                Message = msg;
                Timed = time;
                Priority = priority;
            }

            public string Message { get; }
            public bool Timed { get; }
            public int Priority { get; }
        }
    }

    public struct ProgressIndicatorEventData
    {
        public double Progress { get; set; }

        public bool Indeterminate => this.Progress < 0;
        public bool IsDeterminate => !Indeterminate;
        public bool HideProgressWheel => IsProgressFinished && Indeterminate;
        public bool IsProgressFinished => Math.Abs(Math.Abs(Progress) - 1) < double.Epsilon;
        

        public ProgressIndicatorEventData(double progress)
        {
            Progress = progress;
        }

        public enum Response
        {
            IndeterminateWheel,
            DeterminateWheel,
            IndeterminateFinished,
            DeterminateFinished
        }
    }
}
