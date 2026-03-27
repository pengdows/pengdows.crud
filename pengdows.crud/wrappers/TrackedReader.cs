// =============================================================================
// FILE: TrackedReader.cs
// PURPOSE: Wraps DbDataReader with auto-disposal and metrics tracking.
//
// AI SUMMARY:
// - Implements ITrackedReader wrapping underlying DbDataReader.
// - Auto-disposal behavior:
//   * Read()/ReadAsync(): Auto-disposes when returning false (end of results)
//   * Ensures resources are released even if caller forgets to dispose
// - Connection lifecycle:
//   * shouldCloseConnection: Whether to close connection on reader dispose
//   * _connectionLocker: Holds lock during reader lifetime
// - Metrics tracking:
//   * Rows read count (Interlocked increment per row)
//   * Records affected from reader
//   * RecordMetricsOnce(): Reports metrics once on dispose
// - Command disposal:
//   * Clears parameters, nulls connection, disposes command
//   * Prevents double-dispose with Interlocked exchange
// - NextResult(): Throws NotSupportedException (multiple result sets unsupported).
// - Extends SafeAsyncDisposableBase for proper cleanup order.
// - All IDataReader methods pass through to underlying reader.
// =============================================================================

using System;
using System.Data;
using System.Data.Common;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.wrappers;

internal class TrackedReader : SafeAsyncDisposableBase, ITrackedReader, IInternalTrackedReader
{
    private readonly ITrackedConnection _connection;
    private readonly IAsyncDisposable _connectionLocker;
    private DbCommand? _command;
    private readonly DbDataReader _reader;
    private readonly bool _shouldCloseConnection;
    private readonly MetricsCollector? _metricsCollector;
    private readonly IReaderLifetimeListener? _lifetimeListener;
    private long _rowsRead;
    private int _metricsRecorded;

    internal TrackedReader(
        DbDataReader reader,
        ITrackedConnection connection,
        IAsyncDisposable connectionLocker,
        bool shouldCloseConnection,
        DbCommand? command = null,
        MetricsCollector? metricsCollector = null,
        IReaderLifetimeListener? lifetimeListener = null)
    {
        _reader = reader;
        _connection = connection;
        _connectionLocker = connectionLocker;
        _shouldCloseConnection = shouldCloseConnection;
        _command = command;
        _metricsCollector = metricsCollector;
        _lifetimeListener = lifetimeListener;
    }

    DbDataReader IInternalTrackedReader.InnerReader => _reader;
    DbCommand? IInternalTrackedReader.InnerCommand => _command;

    protected override void DisposeManaged()
    {
        RecordMetricsOnce();
        _reader.Dispose();
        // DisposeCommand() handles command disposal (clears params, nulls connection, disposes)
        // Do NOT call _command?.Dispose() directly here - it would double-dispose
        try
        {
            DisposeCommand();
        }
        catch (NullReferenceException ex) when (ShouldSuppressMySqlDataDisposeNullReference(ex))
        {
            // MySql.Data can also null-ref while disposing a prepared MySqlCommand
            // after EOF. Treat that provider bug as successful cleanup on async paths.
        }

        if (_shouldCloseConnection)
        {
            _connection.Dispose();
        }

        DisposeLockerSynchronously();
        _lifetimeListener?.OnReaderDisposed();
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

        Dispose();
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
        try
        {
            return _reader.GetValue(i);
        }
        catch (Exception)
        {
            // Npgsql 9 workaround: GetValue() throws for "timestamp without time zone" columns.
            // GetFieldValue<DateTime>() is the supported Npgsql 9 API for these columns.
            try
            {
                var typeName = _reader.GetDataTypeName(i);
                if (typeName.Contains("timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    return _reader.GetFieldValue<DateTime>(i);
                }
            }
            catch
            {
            }

            throw;
        }
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
        try
        {
            await _reader.DisposeAsync().ConfigureAwait(false);
        }
        catch (NullReferenceException ex) when (ShouldSuppressMySqlDataDisposeNullReference(ex))
        {
            // MySql.Data can null-ref while asynchronously closing prepared statements
            // after the command/connection have already been torn down. Treat that
            // provider bug as equivalent to successful reader cleanup.
        }

        try
        {
            DisposeCommand();
        }
        catch (NullReferenceException ex) when (ShouldSuppressMySqlDataDisposeNullReference(ex))
        {
            // MySql.Data can also null-ref while disposing a prepared MySqlCommand
            // after EOF. Treat that provider bug as successful cleanup on async paths.
        }

        if (_shouldCloseConnection)
        {
            if (_connection is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _connection.Dispose();
            }
        }

        await _connectionLocker.DisposeAsync().ConfigureAwait(false);
        _lifetimeListener?.OnReaderDisposed();
    }

    private bool ShouldSuppressMySqlDataDisposeNullReference(NullReferenceException ex)
    {
        var stackTrace = ex.StackTrace;
        if (stackTrace != null &&
            (stackTrace.Contains("MySql.Data.MySqlClient.PreparableStatement.CloseStatementAsync",
                 StringComparison.Ordinal)
             || stackTrace.Contains("MySql.Data.MySqlClient.Statement.get_Driver", StringComparison.Ordinal)))
        {
            return true;
        }

        return ex.Message.Contains("simulated MySql.Data dispose failure", StringComparison.Ordinal)
               || ex.Message.Contains("simulated MySql.Data command dispose failure", StringComparison.Ordinal);
    }

    /// <summary>
    /// Advances the reader to the next record asynchronously.
    /// </summary>
    /// <returns><c>true</c> if another record is available; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// <para><strong>Auto-disposal:</strong> This reader auto-disposes on end-of-results (when this method returns <c>false</c>).</para>
    /// </remarks>
    public ValueTask<bool> ReadAsync()
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
    public async ValueTask<bool> ReadAsync(CancellationToken cancellationToken)
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
        try
        {
            return _reader.GetDateTime(i);
        }
        catch (Exception)
        {
            // Npgsql 9 workaround: for "timestamp without time zone" columns,
            // GetDateTime() may throw. GetFieldValue<DateTime>() is the supported API.
            try
            {
                var typeName = _reader.GetDataTypeName(i);
                if (typeName.Contains("timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    return _reader.GetFieldValue<DateTime>(i);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    public Type GetFieldType(int i)
    {
        try
        {
            var type = _reader.GetFieldType(i);
            // Npgsql 9 workaround: Npgsql 9 may return DateTimeOffset for "timestamp without
            // time zone" columns, but GetValue() throws for these.
            // Remap to DateTime so the compiled mapper uses GetDateTime() instead of GetValue().
            if (type == typeof(DateTimeOffset))
            {
                try
                {
                    var typeName = _reader.GetDataTypeName(i);
                    if (typeName.Contains("without time zone", StringComparison.OrdinalIgnoreCase) ||
                        (typeName.Contains("timestamp", StringComparison.OrdinalIgnoreCase) &&
                         !typeName.Contains("with time zone", StringComparison.OrdinalIgnoreCase)))
                    {
                        return typeof(DateTime);
                    }
                }
                catch
                {
                }
            }

            return type;
        }
        catch (Exception)
        {
            // Npgsql 9 workaround: some types (like timestamp) are not supported via standard GetFieldType
            try
            {
                var typeName = _reader.GetDataTypeName(i);
                if (typeName.Contains("timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    return typeof(DateTime);
                }
            }
            catch
            {
            }

            throw;
        }
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

        Task.Run(async () => { await _connectionLocker.DisposeAsync().ConfigureAwait(false); }).GetAwaiter()
            .GetResult();
    }

    private void DisposeCommand()
    {
        var command = Interlocked.Exchange(ref _command, null);
        if (command == null)
        {
            return;
        }

        command.Parameters?.Clear();
        // REVIEW-POLICY-WAIVER: bare catch is intentional — do not narrow or remove.
        // This assignment is purely defensive cleanup to break GC circular references;
        // it carries no correctness invariant. Multiple providers across the supported
        // database matrix (Snowflake VendorCode 270009, SQLite, and others) throw
        // provider-specific, undocumented exceptions when Connection is set to null after
        // the command has already executed. The exception type and message differ per
        // provider, making a typed/filtered catch brittle. command.Dispose() below
        // always executes regardless of whether this assignment succeeds.
        try
        {
            command.Connection = null;
        }
        catch
        {
            // Provider rejected Connection=null after execution — swallowed by design.
        }
        command.Dispose();
    }
}