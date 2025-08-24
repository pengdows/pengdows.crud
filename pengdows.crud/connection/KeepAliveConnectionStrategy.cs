using pengdows.crud.enums;
using pengdows.crud.wrappers;
using pengdows.crud.infrastructure;

namespace pengdows.crud.connection;

internal sealed class KeepAliveConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    private readonly Func<ITrackedConnection> _factory;
    private readonly ITrackedConnection _sentinelConnection;

    public KeepAliveConnectionStrategy(Func<ITrackedConnection> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _sentinelConnection = factory();
        _sentinelConnection.Open();
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        return _factory();
    }

    public void ReleaseConnection(ITrackedConnection? connection)
    {
        if (connection != null && !ReferenceEquals(connection, _sentinelConnection))
        {
            connection.Dispose();
        }
    }

    public async ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        if (connection != null && !ReferenceEquals(connection, _sentinelConnection))
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
        _sentinelConnection.Dispose();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        await _sentinelConnection.DisposeAsync().ConfigureAwait(false);
    }
}
