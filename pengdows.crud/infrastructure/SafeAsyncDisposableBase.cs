#nullable enable
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace pengdows.crud.infrastructure;

public abstract class SafeAsyncDisposableBase : ISafeAsyncDisposableBase, IDisposable, IAsyncDisposable
{
    // 0 = active, 1 = disposed (or disposing)
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
        }
        catch (Exception ex)
        {
            OnDisposeException(ex, nameof(DisposeManaged));
        }

        try
        {
            DisposeUnmanaged();
        }
        catch (Exception ex)
        {
            OnDisposeException(ex, nameof(DisposeUnmanaged));
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

        Exception? first = null;

        try
        {
            await DisposeManagedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            first = ex; // capture but continue to unmanaged cleanup
        }

        try
        {
            await DisposeUnmanagedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (first is null)
            {
                first = ex; // propagate unmanaged failure if managed succeeded
            }
            else
            {
                OnDisposeException(ex, nameof(DisposeUnmanagedAsync)); // don't mask the first
            }
        }
        finally
        {
            GC.SuppressFinalize(this);
        }

        if (first is not null)
        {
            ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    // ---- Overridables (minimal surface area) ----
    /// <summary>Override for synchronous managed cleanup. Default no-op.</summary>
    protected virtual void DisposeManaged() { }

    /// <summary>Override for asynchronous managed cleanup. Defaults to sync bridge.</summary>
    protected virtual ValueTask DisposeManagedAsync()
    {
        DisposeManaged();
        return ValueTask.CompletedTask;
    }

    /// <summary>Override for synchronous unmanaged cleanup. Prefer SafeHandle.</summary>
    protected virtual void DisposeUnmanaged() { }

    /// <summary>Override for asynchronous unmanaged cleanup. Defaults to sync bridge.</summary>
    protected virtual ValueTask DisposeUnmanagedAsync()
    {
        DisposeUnmanaged();
        return ValueTask.CompletedTask;
    }

    /// <summary>Optional visibility hook for swallowed exceptions. Default no-op.</summary>
    protected virtual void OnDisposeException(Exception ex, string phase) { }

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
