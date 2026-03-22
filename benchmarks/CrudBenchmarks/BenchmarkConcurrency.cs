using System.Threading;

namespace CrudBenchmarks;

internal static class BenchmarkConcurrency
{
    internal static async Task RunConcurrent(int operations, int maxConcurrency, Func<Task> operation)
    {
        // MUST be async: the using disposes the SemaphoreSlim when the method exits.
        // A non-async "return Task.WhenAll(tasks)" would dispose the semaphore before
        // the tasks finish, causing Release() in finally blocks to throw
        // ObjectDisposedException and leaving pending WaitAsync() callers hung forever.
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new Task[operations];
        for (var i = 0; i < operations; i++)
        {
            tasks[i] = RunWithSemaphoreAsync(semaphore, operation);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    internal static async Task RunConcurrentWithErrors(
        int operations,
        int maxConcurrency,
        Func<Task> operation,
        Action<Exception> onError)
    {
        // Same reason as RunConcurrent — must be async to keep the semaphore alive.
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new Task[operations];
        for (var i = 0; i < operations; i++)
        {
            tasks[i] = RunWithSemaphoreAsync(semaphore, operation, onError);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task RunWithSemaphoreAsync(SemaphoreSlim semaphore, Func<Task> operation)
    {
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task RunWithSemaphoreAsync(SemaphoreSlim semaphore, Func<Task> operation, Action<Exception> onError)
    {
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            onError(ex);
        }
        finally
        {
            semaphore.Release();
        }
    }
}