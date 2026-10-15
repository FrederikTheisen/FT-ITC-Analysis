using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Platform;

namespace AnalysisITC.Core.Application
{
    // Tracks admission through publication, including queued and silent work.
    public static class ProcessingActivity
    {
        static readonly object gate = new();
        static readonly AsyncLocal<ProcessingOperation> context = new();
        static readonly Queue<TaskCompletionSource<ProcessingLease>> pending = new();
        static ProcessingOperation active;
        static Guid latestOperation;
        static int leases;
        static bool clearing;
        public static event EventHandler Changed;
        public static bool IsBusy { get { lock (gate) return active != null || pending.Count != 0 || clearing; } }
        public static ProcessingOperation Current
        {
            get { lock (gate) return ReferenceEquals(context.Value, active) ? active : null; }
        }

        public static bool IsCurrentOperation(Guid id)
        { lock (gate) return id != Guid.Empty && latestOperation == id; }

        // User starts never join an existing operation, even from an event raised
        // inside that operation. Admission precedes preparation and input edits.
        public static ProcessingLease TryBegin()
        {
            ProcessingLease lease;
            lock (gate)
            {
                if (active != null || pending.Count != 0 || clearing) return null;
                lease = StartLocked();
            }
            Changed?.Invoke(null, EventArgs.Empty);
            return lease;
        }

        internal static ProcessingLease Begin()
        {
            var mayWait = PlatformServices.MainThreadDispatcher is not IMainThreadAccessChecker dispatcher
                || !dispatcher.IsMainThread;
            var lease = RequestAdmission(mayWait).GetAwaiter().GetResult();
            lease.Activate();
            return lease;
        }

        // Required background work waits rather than being dropped. Async callers
        // explicitly Enter after awaiting: ExecutionContext changes in a callee do
        // not establish ownership in the caller's continuation.
        internal static Task<ProcessingLease> BeginAsync() => RequestAdmission(mayWait: true);

        static Task<ProcessingLease> RequestAdmission(bool mayWait)
        {
            ProcessingLease lease;
            lock (gate)
            {
                if (clearing) throw new InvalidOperationException("Processing is being cleared.");
                if (active != null && ReferenceEquals(context.Value, active))
                {
                    leases++;
                    return Task.FromResult(new ProcessingLease(active));
                }
                if (active != null || pending.Count != 0)
                {
                    if (!mayWait)
                        throw new InvalidOperationException("A synchronous calculation cannot wait for admission on the main thread. Enter the owning calculation or await admission before continuing.");
                    var request = new TaskCompletionSource<ProcessingLease>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pending.Enqueue(request);
                    return request.Task;
                }
                lease = StartLocked();
            }
            Changed?.Invoke(null, EventArgs.Empty);
            return Task.FromResult(lease);
        }

        static ProcessingLease StartLocked()
        {
            active = new ProcessingOperation();
            latestOperation = active.Id;
            leases = 1;
            return new ProcessingLease(active);
        }

        internal static bool TryClear(Func<bool> apply)
        {
            lock (gate)
            {
                if (active != null || pending.Count != 0 || clearing) return false;
                clearing = true;
                try { return apply(); }
                finally { clearing = false; }
            }
        }

        // Serialize an input edit with operation admission. The edit must be
        // synchronous; notify observers after releasing the activity gate.
        internal static bool TryEditInput(Action edit)
        {
            lock (gate)
            {
                if (active != null || pending.Count != 0 || clearing) return false;
                edit();
                return true;
            }
        }

        // Capture owned payload bytes synchronously. Disk I/O must happen after
        // this gate is released, so an admitted save does not hold up processing.
        internal static bool TryCaptureSnapshot<T>(Func<T> capture, out T snapshot)
        {
            lock (gate)
            {
                snapshot = default;
                if (active != null || pending.Count != 0 || clearing) return false;
                snapshot = capture();
                return true;
            }
        }

        public sealed class ProcessingLease : IDisposable
        {
            int disposed;
            IDisposable activation;
            public ProcessingOperation Operation { get; }
            internal ProcessingLease(ProcessingOperation operation) => Operation = operation;
            public IDisposable Enter()
            {
                lock (gate)
                    if (disposed != 0 || !ReferenceEquals(active, Operation))
                        throw new InvalidOperationException("The calculation is no longer active.");
                return new ContextScope(Operation);
            }
            internal void Activate() => activation = Enter();
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                activation?.Dispose();
                bool notify = false;
                TaskCompletionSource<ProcessingLease> next = null;
                ProcessingLease nextLease = null;
                lock (gate)
                {
                    if (!ReferenceEquals(active, Operation)) throw new InvalidOperationException("Calculation ownership changed.");
                    if (--leases == 0)
                    {
                        active.Finish();
                        active = null;
                        if (pending.Count > 0)
                        {
                            next = pending.Dequeue();
                            nextLease = StartLocked();
                        }
                        else notify = true;
                    }
                }
                next?.TrySetResult(nextLease);
                if (notify) Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        sealed class ContextScope : IDisposable
        {
            readonly ProcessingOperation previous = context.Value;
            bool disposed;
            internal ContextScope(ProcessingOperation operation) => context.Value = operation;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                context.Value = previous;
            }
        }
    }

    public sealed class ProcessingOperation
    {
        readonly object timingGate = new();
        readonly Stopwatch timer = Stopwatch.StartNew();
        int pauses;
        bool finished;
        public Guid Id { get; } = Guid.NewGuid();
        public TimeSpan Elapsed { get { lock (timingGate) return timer.Elapsed; } }

        // Dialogs keep admission, but time spent waiting for the user is not
        // calculation time. Nested or asynchronous dialogs may overlap.
        public IDisposable PauseElapsed()
        {
            lock (timingGate)
            {
                if (++pauses == 1) timer.Stop();
            }
            return new TimingPause(this);
        }

        internal void Finish()
        {
            lock (timingGate) { finished = true; timer.Stop(); }
        }

        sealed class TimingPause : IDisposable
        {
            ProcessingOperation operation;
            internal TimingPause(ProcessingOperation operation) => this.operation = operation;
            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref operation, null);
                if (owner == null) return;
                lock (owner.timingGate)
                    if (--owner.pauses == 0 && !owner.finished) owner.timer.Start();
            }
        }
    }
}
