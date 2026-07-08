using System;
using AppKit;

namespace AnalysisITC.Platform.MacOS
{
    public sealed class MacMainThreadDispatcher : IMainThreadDispatcher
    {
        public void Invoke(Action action)
        {
            if (action == null) return;

            NSApplication.SharedApplication.InvokeOnMainThread(action);
        }
    }
}
