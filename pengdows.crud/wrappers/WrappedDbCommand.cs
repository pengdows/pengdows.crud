#region

using System.Data;
using System.Data.Common;

#endregion

namespace pengdows.crud.wrappers;

internal sealed class WrappedDbCommand : DbCommand
{
    private readonly DbCommand _inner;
    private readonly DbConnection _connection;
    private CommandType? _fakeCommandType;
    private readonly bool _isSqlite;

    public WrappedDbCommand(DbCommand inner, DbConnection connection)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        var ns = _inner.GetType().Namespace ?? string.Empty;
        _isSqlite = ns.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
    }

    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value;
    }

    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => _fakeCommandType ?? _inner.CommandType;
        set
        {
            if (_isSqlite && value == CommandType.StoredProcedure)
            {
                // Preserve requested type for callers, but do not forward to providers
                // that do not support it (e.g., Sqlite).
                _fakeCommandType = value;
                return;
            }

            _fakeCommandType = null;
            _inner.CommandType = value;
        }
    }

    protected override DbConnection? DbConnection
    {
        get => _inner.Connection;
        set => _inner.Connection = value;
    }

    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = value;
    }

    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    public override void Cancel()
    {
        _inner.Cancel();
    }

    public override int ExecuteNonQuery()
    {
        return _inner.ExecuteNonQuery();
    }

    public override object? ExecuteScalar()
    {
        return _inner.ExecuteScalar();
    }

    public override void Prepare()
    {
        _inner.Prepare();
    }

    protected override DbParameter CreateDbParameter()
    {
        return _inner.CreateParameter();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        return _inner.ExecuteReader(behavior);
    }
}

