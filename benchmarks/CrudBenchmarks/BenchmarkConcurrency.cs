using System.Threading;

namespace CrudBenchmarks;

internal static class BenchmarkConcurrency
{
    internal static async Task RunConcurrent(int operations, int maxConcurrency, Func<Task> operation)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new Task[operations];

        for (var i = 0; i < operations; i++)
        {
            await semaphore.WaitAsync();
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    await operation();
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }

        await Task.WhenAll(tasks);
    }

    internal static async Task RunConcurrentWithErrors(
        int operations,
        int maxConcurrency,
        Func<Task> operation,
        Action<Exception> onError)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new Task[operations];

        for (var i = 0; i < operations; i++)
        {
            await semaphore.WaitAsync();
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    await operation();
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }

        await Task.WhenAll(tasks);
    }
}
