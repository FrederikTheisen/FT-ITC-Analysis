using System;

using Avalonia.Threading;

using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaMainThreadDispatcher : IMainThreadDispatcher, IMainThreadAccessChecker
    {
        public bool IsMainThread => Dispatcher.UIThread.CheckAccess();
        public void Invoke(Action action)
        {
            if (action == null) return;

            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
        }
    }
}
