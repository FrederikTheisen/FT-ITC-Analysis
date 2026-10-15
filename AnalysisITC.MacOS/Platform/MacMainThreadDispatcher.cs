using System;
using AppKit;
using Foundation;

namespace AnalysisITC.Platform.MacOS
{
    public sealed class MacMainThreadDispatcher : IMainThreadDispatcher, IMainThreadAccessChecker
    {
        public bool IsMainThread => NSThread.IsMain;
        public void Invoke(Action action)
        {
            if (action == null) return;

            NSApplication.SharedApplication.InvokeOnMainThread(action);
        }
    }
}
