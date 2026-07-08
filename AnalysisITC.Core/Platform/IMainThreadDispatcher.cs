using System;

namespace AnalysisITC.Platform
{
    public interface IMainThreadDispatcher
    {
        void Invoke(Action action);
    }
}
