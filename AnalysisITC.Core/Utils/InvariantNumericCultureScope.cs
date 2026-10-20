using System;
using System.Globalization;

namespace AnalysisITC.Core.Utilities
{
    /// <summary>Temporarily uses invariant culture for synchronous numeric presentation.</summary>
    internal sealed class InvariantNumericCultureScope : IDisposable
    {
        readonly CultureInfo previousCulture;
        bool disposed;

        public InvariantNumericCultureScope()
        {
            previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        public void Dispose()
        {
            if (disposed) return;
            CultureInfo.CurrentCulture = previousCulture;
            disposed = true;
        }
    }
}
