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

    /// <summary>
    /// Same as RunConcurrentWithErrors, but bounds each individual operation with a real
    /// CancellationToken-driven timeout instead of only relying on the operation's own
    /// internal timeout handling. Added after a real incident (2026-08-27): MySqlData hung
    /// on async command execution at 128-way parallelism badly enough that the WHOLE
    /// 2000-operation batch — not any single operation — exceeded BenchmarkDotNet's own
    /// in-process "takes too long to run" watchdog and aborted the entire process. Each
    /// individual operation already throwing its own CommandTimeoutException wasn't enough,
    /// because Task.WhenAll over the whole batch was what actually timed out BenchmarkDotNet,
    /// not any one operation. A race-and-abandon pattern (Task.WhenAny against Task.Delay)
    /// was deliberately NOT used here: abandoning a still-running task would keep holding
    /// whatever connection/resource it has, while releasing this method's semaphore slot
    /// early — letting a NEW operation start on top of a resource the old one still holds,
    /// silently increasing effective concurrency beyond the configured limiter. A real
    /// CancellationToken, threaded through to the actual ADO.NET call, tells the operation to
    /// actually give up and release its resource before this method moves on.
    /// </summary>
    internal static async Task RunConcurrentWithTimeout(
        int operations,
        int maxConcurrency,
        TimeSpan perOperationTimeout,
        Func<CancellationToken, Task> operation,
        Action<Exception> onError,
        Action onTimeout)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new Task[operations];
        for (var i = 0; i < operations; i++)
        {
            tasks[i] = RunWithSemaphoreAndTimeoutAsync(semaphore, perOperationTimeout, operation, onError, onTimeout);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task RunWithSemaphoreAndTimeoutAsync(
        SemaphoreSlim semaphore,
        TimeSpan perOperationTimeout,
        Func<CancellationToken, Task> operation,
        Action<Exception> onError,
        Action onTimeout)
    {
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cts = new CancellationTokenSource(perOperationTimeout);
            try
            {
                await operation(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                onTimeout();
            }
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