using System;
using System.Threading;

namespace AnalysisITC.Core.Application
{
    // Tracks admission through publication, including queued and silent work.
    public static class ProcessingActivity
    {
        static readonly object gate = new();
        static int operations;
        static bool clearing;
        public static event EventHandler Changed;
        public static bool IsBusy { get { lock (gate) return operations != 0 || clearing; } }

        internal static IDisposable Begin()
        {
            bool notify;
            lock (gate)
            {
                if (clearing) throw new InvalidOperationException("Processing is being cleared.");
                notify = operations++ == 0;
            }
            if (notify) Changed?.Invoke(null, EventArgs.Empty);
            return new Lease();
        }

        internal static bool TryClear(Func<bool> apply)
        {
            lock (gate)
            {
                if (operations != 0 || clearing) return false;
                clearing = true;
                try { return apply(); }
                finally { clearing = false; }
            }
        }

        sealed class Lease : IDisposable
        {
            int disposed;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                bool notify;
                lock (gate) notify = --operations == 0;
                if (notify) Changed?.Invoke(null, EventArgs.Empty);
            }
        }
    }
}
