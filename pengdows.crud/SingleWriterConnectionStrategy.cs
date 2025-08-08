using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud;

internal sealed class SingleWriterConnectionStrategy : IConnectionStrategy
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

    public void CloseAndDisposeConnection(ITrackedConnection? connection)
    {
        if (connection != null)
        {
            if (!ReferenceEquals(connection, _writerConnection))
            {
                connection.Dispose();
            }
        }
    }

    public async ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection)
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
}

