using System;
using System.Threading;
using System.Threading.Tasks;

namespace pengdows.crud.infrastructure;

public abstract class SafeAsyncDisposableBase : ISafeAsyncDisposableBase
{
    private int _disposed;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try
        {
            DisposeManaged();
            DisposeUnmanaged();
        }
        catch
        {
            // optional log
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try
        {
            await DisposeManagedAsync().ConfigureAwait(false);
            DisposeUnmanaged();
        }
        catch
        {
            // optional log
        }

        GC.SuppressFinalize(this);
    }

    protected virtual void DisposeManaged()
    {
    }

    protected virtual ValueTask DisposeManagedAsync()
    {
        DisposeManaged();
        return ValueTask.CompletedTask;
    }

    protected virtual void DisposeUnmanaged()
    {
    }
}