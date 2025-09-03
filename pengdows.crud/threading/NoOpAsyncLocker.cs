using pengdows.crud.infrastructure;

namespace pengdows.crud.threading;

internal sealed class NoOpAsyncLocker : SafeAsyncDisposableBase, ILockerAsync
{
    public static readonly NoOpAsyncLocker Instance = new();

    private NoOpAsyncLocker()
    {
    }

    public Task LockAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

}
