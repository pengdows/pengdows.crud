#nullable enable
using System.Runtime.CompilerServices;

namespace pengdows.crud.infrastructure;

public abstract class SafeAsyncDisposableBase : ISafeAsyncDisposableBase, IDisposable, IAsyncDisposable
{
    // 0 = active, 1 = disposed
    private int _disposed;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (!TryBeginDispose())
        {
            return;
        }

        try
        {
            DisposeManaged();
            DisposeUnmanaged();
        }
        catch
        {
            // Non-throwing by policy; derived classes should log if needed.
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!TryBeginDispose())
        {
            return;
        }

        try
        {
            await DisposeManagedAsync().ConfigureAwait(false);
            DisposeUnmanaged();
        }
        finally
        {
            // Even if async cleanup throws, object is considered disposed.
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Override for synchronous managed cleanup. Default no-op.</summary>
    protected virtual void DisposeManaged() { }

    /// <summary>
    /// Override for asynchronous managed cleanup. Default bridges to DisposeManaged().
    /// Prefer overriding this when you hold IAsyncDisposable resources.
    /// </summary>
    protected virtual ValueTask DisposeManagedAsync()
    {
        DisposeManaged();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Override for unmanaged cleanup. Prefer SafeHandle; no finalizer provided.
    /// </summary>
    protected virtual void DisposeUnmanaged() { }

    /// <summary>Throw ObjectDisposedException if already disposed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            ThrowObjectDisposed();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowObjectDisposed() =>
        throw new ObjectDisposedException(GetType().FullName);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryBeginDispose() => Interlocked.Exchange(ref _disposed, 1) == 0;
}

