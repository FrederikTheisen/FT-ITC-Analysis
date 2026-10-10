using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AvaloniaDispatcherTests : IDisposable
{
    readonly int ownerThread;
    readonly Window window;

    public AvaloniaDispatcherTests()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        ownerThread = Environment.CurrentManagedThreadId;
        window = new Window();
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AsyncTestsResumeOnTheOwningThreadAndServiceWorkerDispatch(int iteration)
    {
        window.Show();
        await Task.Yield();
        AssertOwnerThread();

        // The UI test runner must keep dispatching while an async test awaits
        // work from another thread, rather than blocking the owner thread.
        var dispatchedThread = await Task.Run(() => Dispatcher.UIThread.Invoke(() =>
        {
            window.Title = $"Iteration {iteration}";
            return Environment.CurrentManagedThreadId;
        })).WaitAsync(TimeSpan.FromSeconds(5));

        AssertOwnerThread();
        Assert.Equal(ownerThread, dispatchedThread);
        Assert.Equal($"Iteration {iteration}", window.Title);
    }

    public void Dispose()
    {
        AssertOwnerThread();
        window.Close();
    }

    void AssertOwnerThread()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        Assert.Equal(ownerThread, Environment.CurrentManagedThreadId);
    }
}
