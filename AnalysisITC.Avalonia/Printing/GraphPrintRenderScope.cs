using System;

public static class GraphPrintRenderScope
{
    [ThreadStatic]
    static int depth;

    public static bool IsActive => depth > 0;

    public static IDisposable Enter()
    {
        depth++;
        return new Scope();
    }

    sealed class Scope : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            depth = Math.Max(0, depth - 1);
        }
    }
}
