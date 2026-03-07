using System.Threading;

namespace CrudBenchmarks;

internal static class BenchmarkConcurrency
{
    internal static Task RunConcurrent(int operations, int maxConcurrency, Func<Task> operation)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new Task[operations];
        for (var i = 0; i < operations; i++)
        {
            tasks[i] = RunWithSemaphoreAsync(semaphore, operation);
        }
        return Task.WhenAll(tasks);
    }

    internal static Task RunConcurrentWithErrors(
        int operations,
        int maxConcurrency,
        Func<Task> operation,
        Action<Exception> onError)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new Task[operations];
        for (var i = 0; i < operations; i++)
        {
            tasks[i] = RunWithSemaphoreAsync(semaphore, operation, onError);
        }
        return Task.WhenAll(tasks);
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