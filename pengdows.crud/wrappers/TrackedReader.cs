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
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using pengdows.crud.@internal;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;

namespace pengdows.crud.wrappers;

internal class TrackedReader : SafeAsyncDisposableBase, ITrackedReader, IInternalTrackedReader
{
    private readonly ITrackedConnection _connection;
    private readonly IAsyncDisposable _connectionLocker;
    private readonly IAsyncDisposable? _contextLocker;
    // DbMode.SingleConnection transaction gate held by an ordinary reader for its lifetime.
    private readonly IAsyncDisposable? _singleConnectionTransactionGate;
    private DbCommand? _command;
    private readonly DbDataReader _reader;
    private readonly bool _shouldCloseConnection;
    private readonly MetricsCollector? _metricsCollector;
    private readonly IReaderLifetimeListener? _lifetimeListener;
    private long _rowsRead;
    private readonly long _leaseStartTimestamp = Stopwatch.GetTimestamp();
    private long _firstRowTimestamp;
    private int _metricsRecorded;

    // Translates a provider exception raised while fetching a row into the typed DatabaseException
    // hierarchy (null when it isn't a provider exception), like failures while executing the command.
    private readonly Func<Exception, Exception?>? _readFailureTranslator;

    // SqlDialect.ReadsInt64ThroughGetValue: Informix.Net.Core's GetInt64 rejects BIGSERIAL.
    private readonly bool _readsInt64ThroughGetValue;

    // SqlDialect.ReportsOutOfRangeDecimalAsNull: Informix.Net.Core returns null for an out-of-range DECIMAL.
    private readonly bool _reportsOutOfRangeDecimalAsNull;

    // SqlDialect.IsUnreadableStoredValue: a provider exception meaning the stored value has no .NET
    // representation (TYPE-005).
    private readonly Func<Exception, bool>? _isUnreadableStoredValue;

    // SqlDialect.ReadsUnresolvedColumns: the dialect reads some columns the provider reports no
    // field type for (TYPE-016). Per ordinal, the Type it reads them as or NotUnresolved; resolved
    // once, since a tracked reader never moves to another result set.
    private readonly SqlDialect? _unresolvedColumnDialect;

    // The dialect's coercion options: DataReaderMapper resolves provider-specific coercions with them.
    private readonly TypeCoercionOptions _coercionOptions;
    private object?[]? _unresolvedColumnTypes;
    private static readonly object NotUnresolved = new();

    private Type? UnresolvedColumnType(int i)
    {
        if (_unresolvedColumnDialect is null)
        {
            return null;
        }

        var cache = _unresolvedColumnTypes ??= new object?[_reader.FieldCount];
        var entry = cache[i];
        if (entry is null)
        {
            entry = (ProviderFieldTypeIsUnknown(i)
                ? _unresolvedColumnDialect.GetUnresolvedColumnType(_reader.GetDataTypeName(i))
                : null) ?? NotUnresolved;
            cache[i] = entry;
        }

        return entry as Type;
    }

    // SqlClient returns null for a CLR type whose assembly isn't loaded; Npgsql throws for a type it
    // has no handler for (PostGIS without NetTopologySuite).
    private bool ProviderFieldTypeIsUnknown(int i)
    {
        try
        {
            return _reader.GetFieldType(i) is null;
        }
        catch (InvalidCastException)
        {
            return true;
        }
    }

    private DataMappingException? UnreadableValue(int i, Exception exception) =>
        _isUnreadableStoredValue?.Invoke(exception) == true
            ? new DataMappingException(
                $"Could not read column '{_reader.GetName(i)}': the stored value has no .NET representation ({exception.Message})",
                SupportedDatabase.Unknown, exception)
            : null;

    private OverflowException OutOfRangeDecimal(int i) =>
        new($"Column '{_reader.GetName(i)}' holds a value outside System.Decimal's range; the provider returned no value for it.");

    internal TrackedReader(
        DbDataReader reader,
        ITrackedConnection connection,
        IAsyncDisposable connectionLocker,
        bool shouldCloseConnection,
        DbCommand? command = null,
        MetricsCollector? metricsCollector = null,
        IReaderLifetimeListener? lifetimeListener = null,
        IAsyncDisposable? contextLocker = null,
        IAsyncDisposable? singleConnectionTransactionGate = null,
        Func<Exception, Exception?>? readFailureTranslator = null,
        bool readsInt64ThroughGetValue = false,
        bool reportsOutOfRangeDecimalAsNull = false,
        Func<Exception, bool>? isUnreadableStoredValue = null,
        SqlDialect? unresolvedColumnDialect = null,
        TypeCoercionOptions? coercionOptions = null)
    {
        _coercionOptions = coercionOptions ?? TypeCoercionOptions.Default;
        _unresolvedColumnDialect = unresolvedColumnDialect;
        _isUnreadableStoredValue = isUnreadableStoredValue;
        _reportsOutOfRangeDecimalAsNull = reportsOutOfRangeDecimalAsNull;
        _readFailureTranslator = readFailureTranslator;
        _readsInt64ThroughGetValue = readsInt64ThroughGetValue;
        _reader = reader;
        _connection = connection;
        _connectionLocker = connectionLocker;
        _contextLocker = contextLocker;
        _singleConnectionTransactionGate = singleConnectionTransactionGate;
        _shouldCloseConnection = shouldCloseConnection;
        _command = command;
        _metricsCollector = metricsCollector;
        _lifetimeListener = lifetimeListener;
    }

    DbDataReader IInternalTrackedReader.InnerReader => _reader;
    DbCommand? IInternalTrackedReader.InnerCommand => _command;
    Type? IInternalTrackedReader.GetUnresolvedColumnType(int ordinal) => UnresolvedColumnType(ordinal);

    object IInternalTrackedReader.ReadUnresolvedColumn(IDataRecord record, int ordinal, Type type) =>
        record.IsDBNull(ordinal) ? DBNull.Value
        : _unresolvedColumnDialect is { } dialect ? dialect.ReadUnresolvedColumn(record, ordinal, type)
        : UnresolvedColumnReader.Read(record, ordinal, type);
    TypeCoercionOptions IInternalTrackedReader.CoercionOptions => _coercionOptions;

    protected override void DisposeManaged()
    {
        // BP-110 (3.0 CORE-021): every phase below owns a distinct resource (reader, command,
        // connection — which releases the governor slot, three lock layers, lifetime-listener
        // notification). An exception from an early phase must not skip the later ones — that
        // would leak whatever came after the throw. Each phase is attempted regardless of prior
        // failures; the first exception encountered is preserved and rethrown once everything
        // has been attempted, matching SafeAsyncDisposableBase's own continue-on-failure principle.
        RecordMetricsOnce();

        Exception? first = null;

        try
        {
            _reader.Dispose();
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        // DisposeCommand() handles command disposal (clears params, nulls connection, disposes)
        // Do NOT call _command?.Dispose() directly here - it would double-dispose
        try
        {
            DisposeCommand();
        }
        catch (NullReferenceException ex) when (ShouldSuppressMySqlDataDisposeNullReference(ex))
        {
            // MySql.Data can also null-ref while disposing a prepared MySqlCommand
            // after EOF. Treat that provider bug as successful cleanup.
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        if (_shouldCloseConnection)
        {
            try
            {
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                first ??= ex;
            }
        }

        try
        {
            DisposeLockerSynchronously(_connectionLocker);
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        try
        {
            DisposeLockerSynchronously(_contextLocker);
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        try
        {
            DisposeLockerSynchronously(_singleConnectionTransactionGate);
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        try
        {
            _lifetimeListener?.OnReaderDisposed();
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        if (first != null)
        {
            ExceptionDispatchInfo.Capture(first).Throw();
        }
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
        bool hasRow;
        try
        {
            hasRow = _reader.Read();
        }
        catch (Exception ex) when (TranslateReadFailure(ex) is { } translated)
        {
            throw translated;
        }

        if (hasRow)
        {
            Interlocked.CompareExchange(ref _firstRowTimestamp, Stopwatch.GetTimestamp(), 0);
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
        if (_reportsOutOfRangeDecimalAsNull && _reader.GetValue(i) is null)
        {
            throw OutOfRangeDecimal(i);
        }

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
        if (_readsInt64ThroughGetValue && _reader.GetValue(i) is long value)
        {
            return value;
        }

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
        try
        {
            return _reader.GetString(i);
        }
        catch (Exception ex) when (UnreadableValue(i, ex) is { } unreadable)
        {
            throw unreadable;
        }
    }

    public object GetValue(int i)
    {
        if (UnresolvedColumnType(i) is { } unresolvedType)
        {
            return _reader.IsDBNull(i)
                ? DBNull.Value
                : _unresolvedColumnDialect!.ReadUnresolvedColumn(_reader, i, unresolvedType);
        }

        try
        {
            var value = _reader.GetValue(i);
            if (_reportsOutOfRangeDecimalAsNull && value is null)
            {
                throw OutOfRangeDecimal(i);
            }

            return value!;
        }
        catch (InvalidCastException) when (ProviderValueFieldReader.TryReadNullableElementArray(_reader, i, out var array))
        {
            // Npgsql refuses some arrays as non-nullable elements (Spanner, TYPE-002).
            return array;
        }
        catch (OverflowException) when (WideIntegerFieldReader.TryReadText(_reader, i, out var wide))
        {
            // Snowflake.Data reports a scale-0 NUMBER as Int64 and overflows beyond it.
            return wide;
        }
        catch (OverflowException)
        {
            throw;
        }
        catch (Exception ex)
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

            if (UnreadableValue(i, ex) is { } unreadable)
            {
                throw unreadable;
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
        try
        {
            return _reader.IsDBNull(i);
        }
        catch (Exception ex) when (UnreadableValue(i, ex) is { } unreadable)
        {
            throw unreadable;
        }
    }

    public int FieldCount => _reader.FieldCount;
    public object this[int i] => _reader[i];
    public object this[string name] => _reader[name];

    /// <summary>
    /// Releases this reader's full ownership — command, connection (if owned), locks, governor
    /// slot, and metrics — identically to <see cref="Dispose"/>. IDataReader.Close() carries the
    /// same "I'm done with this reader" contract as Dispose() for callers that don't use
    /// using/await using; leaving anything held here would silently leak past the governed pool.
    /// </summary>
    public void Close()
    {
        Dispose();
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
        // BP-110 (3.0 CORE-021): same continue-on-failure structure as DisposeManaged() above —
        // an exception from an early phase must not skip the later ones.
        RecordMetricsOnce();

        Exception? first = null;

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
        catch (Exception ex)
        {
            first ??= ex;
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
        catch (Exception ex)
        {
            first ??= ex;
        }

        if (_shouldCloseConnection)
        {
            try
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
            catch (Exception ex)
            {
                first ??= ex;
            }
        }

        try
        {
            await _connectionLocker.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        try
        {
            if (_contextLocker != null)
            {
                await _contextLocker.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        try
        {
            if (_singleConnectionTransactionGate != null)
            {
                await _singleConnectionTransactionGate.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        try
        {
            _lifetimeListener?.OnReaderDisposed();
        }
        catch (Exception ex)
        {
            first ??= ex;
        }

        if (first != null)
        {
            ExceptionDispatchInfo.Capture(first).Throw();
        }
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
        bool hasRow;
        try
        {
            hasRow = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (TranslateReadFailure(ex) is { } translated)
        {
            throw translated;
        }

        if (hasRow)
        {
            Interlocked.CompareExchange(ref _firstRowTimestamp, Stopwatch.GetTimestamp(), 0);
            Interlocked.Increment(ref _rowsRead);
            return true;
        }

        await DisposeAsync().ConfigureAwait(false); // Auto-dispose when done reading
        return false;
    }

    private Exception? TranslateReadFailure(Exception exception)
    {
        // TYPE-008: some providers (FirebirdClient, AdoNetCore.AseClient) decode the whole row inside
        // Read, so a stored value its .NET type can't hold (e.g. a NUMERIC above decimal.MaxValue)
        // fails here rather than in a getter. Report it like a getter failure.
        if (exception is OverflowException or InvalidCastException or FormatException)
        {
            return new DataMappingException(
                $"Could not read the next row: a stored value can't be converted to its .NET type ({exception.Message})",
                SupportedDatabase.Unknown, exception);
        }

        if (_readFailureTranslator == null || exception is OperationCanceledException)
        {
            return null;
        }

        return _readFailureTranslator(exception);
    }

    public DateTime GetDateTime(int i)
    {
        try
        {
            return _reader.GetDateTime(i);
        }
        catch (Exception ex)
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

            if (UnreadableValue(i, ex) is { } unreadable)
            {
                throw unreadable;
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

            // SqlClient returns null for a CLR type whose assembly isn't loaded (hierarchyid without
            // Microsoft.SqlServer.Types); the value is still read, or reported, through GetValue.
            return type ?? UnresolvedColumnType(i) ?? typeof(object);
        }
        catch (InvalidCastException) when (UnresolvedColumnType(i) is { } unresolvedType)
        {
            return unresolvedType;
        }
        catch (Exception ex)
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

            // A type the provider has no handler for (an extension type Npgsql can't read) is reported
            // as object; reading it then fails as a DataMappingException naming the column.
            if (ex is InvalidCastException)
            {
                return typeof(object);
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

        _metricsCollector.RecordReaderDurations(_leaseStartTimestamp, Volatile.Read(ref _firstRowTimestamp));

        var affected = _reader.RecordsAffected;
        if (affected > 0)
        {
            _metricsCollector.RecordRowsAffected(affected);
        }
    }

    private static void DisposeLockerSynchronously(IAsyncDisposable? locker)
    {
        if (locker == null)
        {
            return;
        }

        if (locker is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }

        Task.Run(async () => { await locker.DisposeAsync().ConfigureAwait(false); }).GetAwaiter()
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