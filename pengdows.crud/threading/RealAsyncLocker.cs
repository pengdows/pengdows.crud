namespace pengdows.crud.threading;

internal sealed class RealAsyncLocker : ILockerAsync
{
    private readonly SemaphoreSlim _semaphore;
    private int _disposed;
    private bool _locked = false;

    public RealAsyncLocker(SemaphoreSlim semaphore)
    {
        _semaphore = semaphore;
    }

    public async Task LockAsync()
    {
        //Console.WriteLine("Acquiring lock...");
        await _semaphore.WaitAsync().ConfigureAwait(false);
        _locked = true;
    }

    public ValueTask DisposeAsync()
    {
        //Console.WriteLine("Disposing real-async-locker");
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (!_locked) throw new InvalidOperationException("Lock has not been acquired.");

            _semaphore.Release();
        }

        return ValueTask.CompletedTask;
    }
}