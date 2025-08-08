using pengdows.crud.enums;
using pengdows.crud.wrappers;
using pengdows.crud.infrastructure;

namespace pengdows.crud.connection;

internal sealed class SingleWriterConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    private readonly ITrackedConnection _writerConnection;
    private readonly Func<ITrackedConnection> _factory;

    public SingleWriterConnectionStrategy(ITrackedConnection writerConnection, Func<ITrackedConnection> factory)
    {
        _writerConnection = writerConnection;
        _factory = factory;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        return executionType switch
        {
            ExecutionType.Read => _factory(),
            _ => _writerConnection,
        };
    }

    public void ReleaseConnection(ITrackedConnection? connection)
    {
        if (connection != null)
        {
            if (!ReferenceEquals(connection, _writerConnection))
            {
                connection.Dispose();
            }
        }
    }

    public async ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        if (connection != null && !ReferenceEquals(connection, _writerConnection))
        {
            if (connection is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                connection.Dispose();
            }
        }
    }

    protected override void DisposeManaged()
    {
        _writerConnection.Dispose();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (_writerConnection is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        _writerConnection.Dispose();
    }
}

