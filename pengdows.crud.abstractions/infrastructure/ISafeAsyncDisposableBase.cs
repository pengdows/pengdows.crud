#region

#endregion

namespace pengdows.crud.infrastructure;

/// <summary>
/// Base interface for objects that can be disposed synchronously or asynchronously.
/// </summary>
public interface ISafeAsyncDisposableBase : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Indicates whether the instance has already been disposed.
    /// </summary>
    public bool IsDisposed { get; }
}
