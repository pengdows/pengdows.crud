#region

using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;

#endregion

namespace pengdows.crud.wrappers;

public class TrackedReader : SafeAsyncDisposableBase, ITrackedReader
{
    private readonly ITrackedConnection _connection;
    private readonly IAsyncDisposable _connectionLocker;
    private DbCommand? _command;
    private readonly DbDataReader _reader;
    private readonly bool _shouldCloseConnection;
    private readonly MetricsCollector? _metricsCollector;
    private long _rowsRead;
    private int _metricsRecorded;

    internal TrackedReader(
        DbDataReader reader,
        ITrackedConnection connection,
        IAsyncDisposable connectionLocker,
        bool shouldCloseConnection,
        DbCommand? command = null,
        MetricsCollector? metricsCollector = null)
    {
        _reader = reader;
        _connection = connection;
        _connectionLocker = connectionLocker;
        _shouldCloseConnection = shouldCloseConnection;
        _command = command;
        _metricsCollector = metricsCollector;
    }

    protected override void DisposeManaged()
    {
        RecordMetricsOnce();
        // DisposeCommand() handles command disposal (clears params, nulls connection, disposes)
        // Do NOT call _command?.Dispose() directly here - it would double-dispose
        DisposeCommand();
        _reader.Dispose();
        // Connection lifecycle is managed by connection strategies, not by the reader
        // The _shouldCloseConnection parameter is retained for backwards compatibility but ignored

        DisposeLockerSynchronously();
    }

    /// <summary>
    /// Advances the reader to the next record.
    /// </summary>
    /// <returns><c>true</c> if another record is available; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// <para><strong>Auto-disposal:</strong> This reader auto-disposes on end-of-results (when this method returns <c>false</c>).</para>
    /// </remarks>
    public bool Read()
    {
        if (_reader.Read())
        {
            Interlocked.Increment(ref _rowsRead);
            return true;
        }

        try
        {
            Dispose();
        }
        catch
        {
            //ignore the error
        }

        return false;
    }


    public bool GetBoolean(int i)
    {
        return _reader.GetBoolean(i);
    }

    public byte GetByte(int i)
    {
        return _reader.GetByte(i);
    }

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
    {
        return _reader.GetBytes(i, fieldOffset, buffer, bufferoffset, length);
    }

    public char GetChar(int i)
    {
        return _reader.GetChar(i);
    }

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
    {
        return _reader.GetChars(i, fieldoffset, buffer, bufferoffset, length);
    }

    public IDataReader GetData(int i)
    {
        return _reader.GetData(i);
    }

    public string GetDataTypeName(int i)
    {
        return _reader.GetDataTypeName(i);
    }

    public decimal GetDecimal(int i)
    {
        return _reader.GetDecimal(i);
    }

    public double GetDouble(int i)
    {
        return _reader.GetDouble(i);
    }

    public float GetFloat(int i)
    {
        return _reader.GetFloat(i);
    }

    public short GetInt16(int i)
    {
        return _reader.GetInt16(i);
    }

    public int GetInt32(int i)
    {
        return _reader.GetInt32(i);
    }

    public long GetInt64(int i)
    {
        return _reader.GetInt64(i);
    }

    public string GetName(int i)
    {
        return _reader.GetName(i);
    }

    public int GetOrdinal(string name)
    {
        return _reader.GetOrdinal(name);
    }

    public string GetString(int i)
    {
        return _reader.GetString(i);
    }

    public object GetValue(int i)
    {
        return _reader.GetValue(i);
    }

    public int GetValues(object[] values)
    {
        return _reader.GetValues(values);
    }

    public bool IsDBNull(int i)
    {
        return _reader.IsDBNull(i);
    }

    public int FieldCount => _reader.FieldCount;
    public object this[int i] => _reader[i];
    public object this[string name] => _reader[name];

    public void Close()
    {
        _reader.Close();
    }

    public DataTable? GetSchemaTable()
    {
        return _reader.GetSchemaTable();
    }

    public bool NextResult()
    {
        // Multiple result sets are not supported by policy.
        throw new NotSupportedException("Multiple result sets are not supported.");
    }

    public int Depth => _reader.Depth;
    public bool IsClosed => _reader.IsClosed;
    public int RecordsAffected => _reader.RecordsAffected;

    protected override async ValueTask DisposeManagedAsync()
    {
        RecordMetricsOnce();
        await _reader.DisposeAsync();
        DisposeCommand();
        // Connection lifecycle is managed by connection strategies, not by the reader
        // The _shouldCloseConnection parameter is retained for backwards compatibility but ignored

        await _connectionLocker.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Advances the reader to the next record asynchronously.
    /// </summary>
    /// <returns><c>true</c> if another record is available; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// <para><strong>Auto-disposal:</strong> This reader auto-disposes on end-of-results (when this method returns <c>false</c>).</para>
    /// </remarks>
    public Task<bool> ReadAsync()
    {
        return ReadAsync(CancellationToken.None);
    }

    /// <summary>
    /// Advances the reader to the next record asynchronously with cancellation support.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><c>true</c> if another record is available; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// <para><strong>Auto-disposal:</strong> This reader auto-disposes on end-of-results (when this method returns <c>false</c>).</para>
    /// </remarks>
    public async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        if (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Increment(ref _rowsRead);
            return true;
        }

        await DisposeAsync().ConfigureAwait(false); // Auto-dispose when done reading
        return false;
    }

    public DateTime GetDateTime(int i)
    {
        return _reader.GetDateTime(i);
    }

    public Type GetFieldType(int i)
    {
        return _reader.GetFieldType(i);
    }

    public Guid GetGuid(int i)
    {
        return _reader.GetGuid(i);
    }

    private void RecordMetricsOnce()
    {
        if (_metricsCollector == null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _metricsRecorded, 1) != 0)
        {
            return;
        }

        if (_rowsRead > 0)
        {
            _metricsCollector.RecordRowsRead(_rowsRead);
        }

        var affected = _reader.RecordsAffected;
        if (affected > 0)
        {
            _metricsCollector.RecordRowsAffected(affected);
        }
    }

    private void DisposeLockerSynchronously()
    {
        if (_connectionLocker == null)
        {
            return;
        }

        if (_connectionLocker is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }

        Task.Run(async () =>
        {
            await _connectionLocker.DisposeAsync().ConfigureAwait(false);
        }).GetAwaiter().GetResult();
    }

    private void DisposeCommand()
    {
        var command = Interlocked.Exchange(ref _command, null);
        if (command == null)
        {
            return;
        }

        try
        {
            command.Parameters?.Clear();
        }
        catch
        {
            // Ignore failures while clearing parameters during disposal.
        }

        try
        {
            command.Connection = null;
        }
        catch
        {
            // Ignore providers that do not allow clearing the connection.
        }

        try
        {
            command.Dispose();
        }
        catch
        {
            // Ignore disposal failures so reader shutdown always succeeds.
        }
    }
}
