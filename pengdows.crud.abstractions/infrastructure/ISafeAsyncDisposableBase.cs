namespace pengdows.crud.infrastructure;

public interface ISafeAsyncDisposableBase : IAsyncDisposable, IDisposable
{
    public bool IsDisposed { get; }
}