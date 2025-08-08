using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.wrappers;
using pengdows.crud.infrastructure;

namespace pengdows.crud.connection;

internal sealed class SingleConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    private readonly ITrackedConnection _connection;

    public SingleConnectionStrategy(ITrackedConnection connection)
    {
        _connection = connection;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        return _connection;
    }

    public void ReleaseConnection(ITrackedConnection? connection)
    {
        // no-op
    }

    public ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        return ValueTask.CompletedTask;
    }

    protected override void DisposeManaged()
    {
        _connection.Dispose();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (_connection is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        _connection.Dispose();
    }
}

