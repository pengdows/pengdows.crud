namespace pengdow.crud.infrastructure;

public interface ISafeAsyncDisposableBase : IAsyncDisposable, IDisposable
{
    public bool IsDisposed { get; }
}