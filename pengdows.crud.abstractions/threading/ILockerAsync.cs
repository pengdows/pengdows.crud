using System;
using System.Threading;

namespace pengdows.crud.threading;

public interface ILockerAsync : IAsyncDisposable
{
    Task LockAsync(CancellationToken cancellationToken = default);

    Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
