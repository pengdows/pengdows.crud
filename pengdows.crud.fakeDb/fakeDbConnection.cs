#region

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbConnection : DbConnection, IFakeDbConnection
{
    private string? _connectionString;
    private SupportedDatabase? _emulatedProduct;
    private DataTable? _schemaTable;
    private ConnectionState _state = ConnectionState.Closed;
    private string _serverVersion = "1.0";

    /// <summary>
    /// Overrides the table GetSchema()/GetSchema(string) return, bypassing the embedded
    /// per-SupportedDatabase XML resource lookup entirely — lets a test fabricate an arbitrary
    /// schema (e.g. a specific DataSourceProductName/Version pair) that doesn't correspond to any
    /// real emulated product.
    /// </summary>
    public DataTable? SchemaTable
    {
        get => _schemaTable;
        set => _schemaTable = value;
    }
    private int? _maxParameterLimit;
    private bool _shouldFailOnOpen;
    private bool _shouldFailOnCommand;
    private bool _shouldFailOnBeginTransaction;
    private Exception? _transactionCommitException;
    private Exception? _transactionRollbackException;
    private Exception? _closeFailureException;
    private Exception? _customFailureException;
    private int _openCallCount;
    private int? _failAfterOpenCount;
    private fakeDbFactory? _sharedFactory;
    private int? _sharedFailAfterOpenCount;
    private bool _isBroken;
    private bool _skipFirstFailOnOpen;
    private fakeDbFactory? _factoryRef;
    private string? _emulatedTypeName;
    private Action? _customOpenBehavior;
    private Action? _customCommandBehavior;
    public override string DataSource => "FakeSource";
    public override string ServerVersion => GetEmulatedServerVersion();

    internal readonly Queue<fakeDbDataReader> ReaderResults = new();
    public readonly Queue<object?> ScalarResults = new();
    public readonly Queue<int> NonQueryResults = new();
    internal readonly Dictionary<string, object?> ScalarResultsByCommand = new();
    private readonly Queue<Exception> _nonQueryExecuteExceptions = new();
    internal Exception? ScalarExecuteException { get; private set; }
    internal Exception? PersistentScalarException { get; private set; }
    internal object? DefaultScalarResultOnce { get; private set; }
    internal readonly Dictionary<string, Exception> CommandFailuresByText = new();
    public readonly List<string> ExecutedNonQueryTexts = new();
    public readonly List<string> ExecutedReaderTexts = new();

    /// <summary>
    /// Command text paired with the exact parameter names/values bound at execution time,
    /// captured before the command is disposed. EF Core (and other callers) dispose the
    /// DbCommand — clearing its Parameters collection — before an await on the executing
    /// call returns, so this is the only place a test can observe the real bound value rather
    /// than just the parameter's name token in the captured SQL text.
    /// </summary>
    public readonly List<CapturedCommand> ExecutedNonQueryCommands = new();

    /// <summary>See <see cref="ExecutedNonQueryCommands"/> — the reader-execution equivalent.</summary>
    public readonly List<CapturedCommand> ExecutedReaderCommands = new();
    public readonly List<fakeDbCommand> CreatedCommands = new();
    public fakeDbCommand? LastCreatedCommand { get; private set; }

    /// <summary>
    /// When set, every command created by this connection will have its
    /// <see cref="fakeDbCommand.LastInsertedId"/> pre-populated with this value.
    /// Used to simulate the MySqlConnector OK-packet LastInsertedId for the ReaderInsertedId plan.
    /// </summary>
    public object? NextCommandLastInsertedId { get; set; }

    /// <summary>
    /// When set, every command created by this connection has its
    /// <see cref="fakeDbCommand.BlockSynchronousExecution"/> pre-set to this value — lets a test
    /// prove production code used the async execution path without knowing in advance which
    /// command instance will be created.
    /// </summary>
    public bool BlockSynchronousCommandExecution { get; set; }

    /// <summary>
    /// When set, every ExecuteScalar/ExecuteScalarAsync call on this connection's commands is
    /// answered exclusively by invoking this resolver with the command text — bypassing every
    /// other canned/default scalar response below. Letting the resolver throw for an unexpected
    /// command text is intentional: it catches production code probing something a test didn't
    /// anticipate, which a fixed dictionary of responses can't express.
    /// </summary>
    public Func<string, object?>? ScalarResolver { get; set; }

    private readonly Dictionary<string, Exception> _asyncOnlyScalarFailures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Configures ExecuteScalarAsync (and only ExecuteScalarAsync — the synchronous ExecuteScalar
    /// for the same command text is unaffected) to throw <paramref name="exception"/> when a
    /// command's text exactly matches <paramref name="commandText"/>. Lets a test prove a sync
    /// entry point never accidentally routes through the async overload for a specific probe,
    /// while every other command (sync or async) keeps working normally.
    /// </summary>
    public void SetAsyncOnlyScalarFailure(string commandText, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(commandText);
        ArgumentNullException.ThrowIfNull(exception);
        _asyncOnlyScalarFailures[commandText] = exception;
    }

    internal bool TryGetAsyncOnlyScalarFailure(string commandText, [NotNullWhen(true)] out Exception? exception)
    {
        if (string.IsNullOrEmpty(commandText))
        {
            exception = null;
            return false;
        }

        return _asyncOnlyScalarFailures.TryGetValue(commandText, out exception);
    }

    /// <summary>
    /// Queue of output parameter values to apply after command execution.
    /// Each dictionary maps parameter name to its output value.
    /// </summary>
    internal readonly Queue<Dictionary<string, object?>> OutputParameterResults = new();

    // Enhanced data persistence
    internal readonly FakeDataStore DataStore;

    /// <summary>
    /// Controls whether the connection should persist DML results in-memory for subsequent queries.
    /// Tests opt-in explicitly to avoid surprising behavior changes in existing suites.
    /// </summary>
    public bool EnableDataPersistence { get; set; } = false;

    /// <summary>
    /// Creates a fakeDbConnection with an optional shared data store.
    /// If no shared store is provided, creates a new instance-level store.
    /// </summary>
    public fakeDbConnection(FakeDataStore? sharedDataStore = null)
    {
        DataStore = sharedDataStore ?? new FakeDataStore();
    }

    /// <summary>
    /// Enqueues a reader whose <see cref="fakeDbDataReader.RecordsAffected"/> reports
    /// <paramref name="recordsAffected"/> instead of the default 0 — needed for providers whose
    /// modification-command-batch implementation reads that property directly rather than a
    /// row/column value (see <see cref="fakeDbDataReader.RecordsAffectedOverride"/>).
    /// </summary>
    public void EnqueueReaderResult(IEnumerable<Dictionary<string, object?>> rows, int recordsAffected)
    {
        var reader = new fakeDbDataReader(ConvertRows(rows)) { RecordsAffectedOverride = recordsAffected };
        ReaderResults.Enqueue(reader);
    }

    public void EnqueueReaderResult(IEnumerable<Dictionary<string, object?>> rows)
    {
        ReaderResults.Enqueue(new fakeDbDataReader(ConvertRows(rows)));
    }

    /// <summary>
    /// Enqueues a reader that returns <paramref name="rows"/> successfully up to
    /// <paramref name="failAfterRowCount"/> rows, then throws <paramref name="exception"/> on the
    /// next read attempt — simulating a stream that fails partway through enumeration.
    /// </summary>
    public void EnqueueReaderResult(
        IEnumerable<Dictionary<string, object?>> rows,
        int failAfterRowCount,
        Exception exception)
    {
        var reader = new fakeDbDataReader(ConvertRows(rows))
        {
            FailAfterReadCount = failAfterRowCount,
            FailException = exception
        };
        ReaderResults.Enqueue(reader);
    }

    /// <summary>
    /// Enqueues a reader that returns <paramref name="rows"/> successfully up to
    /// <paramref name="cancelAfterRowCount"/> rows, then cancels <paramref name="cancellationTokenSource"/>
    /// on the next read attempt and honors that cancellation — simulating a caller cancelling the
    /// real, ambient <see cref="CancellationToken"/> mid-stream rather than a canned failure.
    /// </summary>
    public void EnqueueReaderResult(
        IEnumerable<Dictionary<string, object?>> rows,
        int cancelAfterRowCount,
        CancellationTokenSource cancellationTokenSource)
    {
        var reader = new fakeDbDataReader(ConvertRows(rows))
        {
            CancelAfterReadCount = cancelAfterRowCount,
            CancelSource = cancellationTokenSource
        };
        ReaderResults.Enqueue(reader);
    }

    /// <summary>
    /// Enqueues a reader with multiple result sets, allowing <see cref="fakeDbDataReader.NextResult"/>
    /// to advance to subsequent sets. Used to test compound batch queries such as
    /// INSERT followed by SELECT LAST_INSERT_ID().
    /// </summary>
    public void EnqueueMultiResultReader(IEnumerable<IEnumerable<Dictionary<string, object?>>> resultSets)
    {
        var converted = resultSets
            .Select(rs => ConvertRows(rs).ToList())
            .ToList<IEnumerable<Dictionary<string, object>>>();
        ReaderResults.Enqueue(new fakeDbDataReader(converted));
    }

    private static IEnumerable<Dictionary<string, object>> ConvertRows(
        IEnumerable<Dictionary<string, object?>> rows)
    {
        foreach (var row in rows)
        {
            var map = new Dictionary<string, object>(row.Count);
            foreach (var kvp in row)
                map[kvp.Key] = kvp.Value!;
            yield return map;
        }
    }

    public void EnqueueScalarResult(object? value)
    {
        ScalarResults.Enqueue(value);
    }

    public void EnqueueNonQueryResult(int value)
    {
        NonQueryResults.Enqueue(value);
    }

    /// <summary>
    /// Enqueues output parameter values to be applied after the next command execution.
    /// The dictionary maps parameter names to their output values.
    /// </summary>
    public void EnqueueOutputParameterResult(Dictionary<string, object?> outputValues)
    {
        OutputParameterResults.Enqueue(outputValues);
    }

    public void SetScalarResultForCommand(string commandText, object? value)
    {
        ScalarResultsByCommand[commandText] = value;
    }

    public void SetNonQueryExecuteException(Exception? exception)
    {
        _nonQueryExecuteExceptions.Clear();
        if (exception != null)
        {
            _nonQueryExecuteExceptions.Enqueue(exception);
        }
    }

    /// <summary>
    /// Queues exceptions to throw on the next N ExecuteNonQuery(Async) calls, one per call and in
    /// order, after which execution proceeds normally. Lets a test prove a real EF Core execution
    /// strategy (e.g. a custom retrying <c>ExecutionStrategy</c>) actually retries a transient
    /// failure through StormGate-gated fakeDb connections, rather than surfacing on the first
    /// attempt — unlike <see cref="SetNonQueryExecuteException"/>, which only primes a single call.
    /// </summary>
    public void EnqueueTransientNonQueryFailures(params Exception[] exceptions)
    {
        foreach (var exception in exceptions)
        {
            _nonQueryExecuteExceptions.Enqueue(exception);
        }
    }

    internal bool TryDequeueNonQueryExecuteException([NotNullWhen(true)] out Exception? exception)
    {
        if (_nonQueryExecuteExceptions.Count > 0)
        {
            exception = _nonQueryExecuteExceptions.Dequeue();
            return true;
        }

        exception = null;
        return false;
    }

    public void SetScalarExecuteException(Exception? exception)
    {
        ScalarExecuteException = exception;
    }

    public void SetPersistentScalarException(Exception? exception)
    {
        PersistentScalarException = exception;
    }

    public void SetDefaultScalarOnce(object? value)
    {
        DefaultScalarResultOnce = value;
    }

    internal object? ConsumeDefaultScalarOnce()
    {
        var v = DefaultScalarResultOnce;
        DefaultScalarResultOnce = null;
        return v;
    }

    public void SetCommandFailure(string commandText, Exception exception)
    {
        CommandFailuresByText[commandText] = exception;
    }

    public void SetServerVersion(string version)
    {
        _serverVersion = version;
    }

    public void SetMaxParameterLimit(int limit)
    {
        _maxParameterLimit = limit;
    }

    public int? GetMaxParameterLimit()
    {
        return _maxParameterLimit;
    }

    /// <summary>
    /// Sets the emulated type name for the connection (e.g., "Npgsql.NpgsqlConnection")
    /// This affects GetType().FullName behavior to simulate different connection types
    /// </summary>
    public void SetEmulatedTypeName(string typeName)
    {
        _emulatedTypeName = typeName;
    }

    /// <summary>
    /// Gets the emulated type name if set, otherwise returns the actual type name
    /// </summary>
    public string GetEmulatedTypeName()
    {
        return _emulatedTypeName ?? GetType().FullName ?? "fakeDbConnection";
    }

    /// <summary>
    /// Override for testing purposes - checks if the type name starts with the specified prefix
    /// This is used by dialects to check connection types (e.g., "Npgsql.")
    /// </summary>
    public bool TypeNameStartsWith(string prefix)
    {
        var typeName = _emulatedTypeName ?? GetType().FullName ?? "";
        return typeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets the connection to fail on the next Open() or OpenAsync() call
    /// </summary>
    public void SetFailOnOpen(bool shouldFail = true, bool skipFirstOpen = false)
    {
        _shouldFailOnOpen = shouldFail;
        _skipFirstFailOnOpen = skipFirstOpen;
    }

    /// <summary>
    /// Sets the connection to fail when creating commands
    /// </summary>
    public void SetFailOnCommand(bool shouldFail = true)
    {
        _shouldFailOnCommand = shouldFail;
    }

    /// <summary>
    /// Sets the connection to fail when beginning transactions
    /// </summary>
    public void SetFailOnBeginTransaction(bool shouldFail = true)
    {
        _shouldFailOnBeginTransaction = shouldFail;
    }

    /// <summary>
    /// Sets an exception to be thrown when the transaction's Commit() is called.
    /// </summary>
    public void SetTransactionCommitException(Exception exception)
    {
        _transactionCommitException = exception;
    }

    /// <summary>
    /// Sets an exception to be thrown when the transaction's Rollback() is called.
    /// </summary>
    public void SetTransactionRollbackException(Exception exception)
    {
        _transactionRollbackException = exception;
    }

    /// <summary>
    /// Sets a custom exception to throw instead of the default InvalidOperationException
    /// </summary>
    public void SetCustomFailureException(Exception exception)
    {
        _customFailureException = exception;
    }

    /// <summary>
    /// Sets the connection to fail after N successful open operations
    /// </summary>
    public void SetFailAfterOpenCount(int openCount)
    {
        _failAfterOpenCount = openCount;
        _openCallCount = 0;
    }

    /// <summary>
    /// Marks the connection as permanently broken - all operations will fail
    /// </summary>
    public void SetBroken()
    {
        SetFailOnOpen(true);
        SetFailOnCommand(true);
        SetFailOnBeginTransaction(true);
    }

    /// <summary>
    /// Sets custom behavior for Open() calls
    /// </summary>
    public void SetCustomOpenBehavior(Action customBehavior)
    {
        _customOpenBehavior = customBehavior;
    }

    /// <summary>
    /// Sets custom behavior for CreateCommand() calls
    /// </summary>
    public void SetCustomCommandBehavior(Action customBehavior)
    {
        _customCommandBehavior = customBehavior;
    }

    /// <summary>
    /// Sets the connection to fail after N successful open operations across the entire factory
    /// </summary>
    internal void SetSharedFailAfterOpenCount(fakeDbFactory factory, int openCount)
    {
        _sharedFactory = factory;
        _sharedFailAfterOpenCount = openCount;
    }

    /// <summary>
    /// Sets a reference to the factory for factory-level failure coordination
    /// </summary>
    internal void SetFactoryReference(fakeDbFactory factory)
    {
        _factoryRef = factory;
    }

    /// <summary>
    /// Simulates a broken connection by setting state to Broken
    /// </summary>
    public void BreakConnection(bool skipFirst = false)
    {
        if (!skipFirst)
        {
            var original = _state;
            _state = ConnectionState.Broken;
            _isBroken = true;
            RaiseStateChangedEvent(original);
        }
        else
        {
            // Mark as broken but don't change state yet - factory will control when it breaks
            _isBroken = true;
        }
    }

    /// <summary>
    /// Resets all failure conditions
    /// </summary>
    public void ResetFailureConditions()
    {
        _shouldFailOnOpen = false;
        _shouldFailOnCommand = false;
        _shouldFailOnBeginTransaction = false;
        _customFailureException = null;
        _failAfterOpenCount = null;
        _sharedFactory = null;
        _sharedFailAfterOpenCount = null;
        _openCallCount = 0;
        _isBroken = false;
        _skipFirstFailOnOpen = false;
        _factoryRef = null;
    }

    internal bool TryGetCommandFailure(string commandText, [NotNullWhen(true)] out Exception? exception)
    {
        if (CommandFailuresByText.TryGetValue(commandText, out exception))
        {
            return true;
        }

        if (_factoryRef?.TryGetCommandFailure(commandText, out exception) == true)
        {
            return true;
        }

        exception = null;
        return false;
    }

    IReadOnlyCollection<IEnumerable<Dictionary<string, object>>> IFakeDbConnection.RemainingReaderResults
    {
        get
        {
            if (ReaderResults.Count == 0)
            {
                return Array.Empty<IEnumerable<Dictionary<string, object>>>();
            }

            var copies = new List<IEnumerable<Dictionary<string, object>>>(ReaderResults.Count);
            foreach (var reader in ReaderResults)
            {
                var clonedRows = new List<Dictionary<string, object>>();
                foreach (var row in reader.FirstResultSet)
                {
                    var clone = new Dictionary<string, object>(row.Count);
                    foreach (var kvp in row)
                        clone[kvp.Key] = kvp.Value;
                    clonedRows.Add(clone);
                }

                copies.Add(clonedRows);
            }

            return copies;
        }
    }

    IReadOnlyCollection<object?> IFakeDbConnection.RemainingScalarResults => ScalarResults.ToArray();

    IReadOnlyCollection<int> IFakeDbConnection.RemainingNonQueryResults => NonQueryResults.ToArray();

    IReadOnlyCollection<string> IFakeDbConnection.ExecutedNonQueryTexts => ExecutedNonQueryTexts.ToArray();
    IReadOnlyCollection<fakeDbCommand> IFakeDbConnection.CreatedCommands => CreatedCommands.ToArray();

    private static Dictionary<string, object> CloneRow(Dictionary<string, object?> row)
    {
        var clone = new Dictionary<string, object>(row.Count);
        foreach (var kvp in row)
        {
            clone[kvp.Key] = kvp.Value!;
        }

        return clone;
    }

    void IFakeDbConnection.EnqueueMultiResultReader(IEnumerable<IEnumerable<Dictionary<string, object>>> resultSets)
    {
        var converted = resultSets.Select(rs => rs.Select(row =>
        {
            var newRow = new Dictionary<string, object?>(row.Count);
            foreach (var kvp in row)
                newRow[kvp.Key] = kvp.Value;
            return newRow;
        }));
        EnqueueMultiResultReader(converted);
    }

    void IFakeDbConnection.EnqueueReaderResult(IEnumerable<Dictionary<string, object>> rows)
    {
        var converted =
            new List<Dictionary<string, object?>>(rows is ICollection<Dictionary<string, object>> collection
                ? collection.Count
                : 0);
        foreach (var row in rows)
        {
            var newRow = new Dictionary<string, object?>(row.Count);
            foreach (var kvp in row)
            {
                newRow[kvp.Key] = kvp.Value;
            }

            converted.Add(newRow);
        }

        EnqueueReaderResult(converted);
    }

    private string GetEmulatedServerVersion()
    {
        if (!string.IsNullOrEmpty(_serverVersion) && _serverVersion != "1.0")
        {
            return _serverVersion;
        }

        return EmulatedProduct switch
        {
            SupportedDatabase.SqlServer => "Microsoft SQL Server 2019",
            SupportedDatabase.PostgreSql => "PostgreSQL 15.0",
            SupportedDatabase.MySql => "8.0.33",
            SupportedDatabase.MariaDb => "10.11.0",
            SupportedDatabase.Sqlite => "3.42.0",
            SupportedDatabase.Oracle => "Oracle Database 19c",
            SupportedDatabase.Firebird => "4.0.0",
            SupportedDatabase.CockroachDb => "v23.1.0",
            SupportedDatabase.DuckDB => "DuckDB 0.9.2",
            SupportedDatabase.Db2 => "DB2 11.05.0800",
            _ => "1.0"
        };
    }

    public SupportedDatabase EmulatedProduct
    {
        get
        {
            _emulatedProduct ??= SupportedDatabase.Unknown;
            return _emulatedProduct.Value;
        }
        set
        {
            if (_emulatedProduct == null || _emulatedProduct == SupportedDatabase.Unknown)
            {
                _emulatedProduct = value;
            }
        }
    }

    /// <summary>
    /// Every value ever assigned to <see cref="ConnectionString"/>, in order — lets a test verify
    /// what connection string(s) production code actually built and assigned (e.g. a read-only
    /// variant with extra parameters) without needing its own recording DbConnection subclass.
    /// </summary>
    public List<string> ConnectionStringHistory { get; } = new();

    [AllowNull]
    public override string ConnectionString
    {
        get => _connectionString ?? string.Empty;
        set
        {
            var normalized = value ?? string.Empty;
            ConnectionStringHistory.Add(normalized);
            _connectionString = normalized;
        }
    }

    public override int ConnectionTimeout => 0;
    public override string Database => _emulatedProduct?.ToString() ?? string.Empty;

    public override ConnectionState State => _state;

    private void ThrowConfiguredException(string defaultMessage)
    {
        throw _customFailureException ?? new InvalidOperationException(defaultMessage);
    }

    public int OpenCount { get; private set; }

    public int OpenAsyncCount { get; private set; }

    public int CloseCount { get; private set; }

    public int DisposeCount { get; private set; }

    public override void Open()
    {
        if (_state == ConnectionState.Open)
        {
            return; // Already open, don't change state again
        }

        if (_isBroken)
        {
            throw new InvalidOperationException("Connection is broken");
        }

        _openCallCount++;

        // Check if we should use shared factory counter
        if (_sharedFactory != null && _sharedFailAfterOpenCount.HasValue)
        {
            var sharedCount = _sharedFactory.IncrementSharedOpenCount();
            if (sharedCount > _sharedFailAfterOpenCount.Value)
            {
                var original = _state;
                _state = ConnectionState.Broken;
                _isBroken = true;
                RaiseStateChangedEvent(original);
                ThrowConfiguredException("Connection failed after " + _sharedFailAfterOpenCount.Value + " opens");
            }
        }
        // Check if we should fail after a specific number of opens (per connection)
        else if (_failAfterOpenCount.HasValue && _openCallCount > _failAfterOpenCount.Value)
        {
            var original = _state;
            _state = ConnectionState.Broken;
            _isBroken = true;
            RaiseStateChangedEvent(original);
            ThrowConfiguredException("Connection failed after " + _failAfterOpenCount.Value + " opens");
        }

        // Check if we should fail on open
        if (_shouldFailOnOpen)
        {
            if (_factoryRef?.ShouldSkipThisOpen() == true)
            {
                // Skip this open (factory-level first open)
            }
            else
            {
                ThrowConfiguredException("Simulated connection open failure");
            }
        }

        // Check if connection should be broken (factory decides)
        if (_isBroken)
        {
            if (_factoryRef?.ShouldSkipThisOpen() == true)
            {
                // Allow this open, but mark as broken for future opens
                _isBroken = false; // Temporarily allow this open
                var original = _state;
                _state = ConnectionState.Open;
                RaiseStateChangedEvent(original);
                return; // Exit early, don't do normal open logic
            }

            throw new InvalidOperationException("Connection is broken");
        }

        // Invoke custom open behavior if set
        _customOpenBehavior?.Invoke();

        OpenCount++;
        ParseEmulatedProduct(ConnectionString);
        var originalState = _state;

        _state = ConnectionState.Open;
        RaiseStateChangedEvent(originalState);
    }

    public override void Close()
    {
        if (_closeFailureException != null)
        {
            throw _closeFailureException;
        }

        CloseCount++;
        var original = _state;
        _state = ConnectionState.Closed;
        RaiseStateChangedEvent(original);
    }

    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCount++;
        }

        try
        {
            Close();
        }
        catch
        {
            // Dispose should not throw, even if Close is configured to fail.
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        DisposeCount++;
        await CloseAsync();
        await base.DisposeAsync();
    }

    public override Task CloseAsync()
    {
        Close();
        return Task.CompletedTask;
    }

    private TaskCompletionSource<bool>? _openGate;

    /// <summary>
    /// Makes OpenAsync await a test-controlled gate before performing the physical open, instead
    /// of completing immediately. Lets a test hold a connection's open "in flight" indefinitely
    /// and release it deterministically by completing the returned TaskCompletionSource — used to
    /// prove genuine concurrent-open behavior (e.g. an admission-control semaphore actually
    /// permitting N simultaneous opens, not serializing them) against real overlapping async
    /// calls, without relying on a fixed delay.
    /// </summary>
    public TaskCompletionSource<bool> SetOpenGate()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _openGate = tcs;
        return tcs;
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        OpenAsyncCount++;

        var gate = _openGate;
        if (gate == null)
        {
            try
            {
                Open();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        return OpenAsyncWithGate(gate, cancellationToken);
    }

    private async Task OpenAsyncWithGate(TaskCompletionSource<bool> gate, CancellationToken cancellationToken)
    {
        await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        Open();
    }

    private SupportedDatabase ParseEmulatedProduct(string? connStr)
    {
        if (EmulatedProduct == SupportedDatabase.Unknown)
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connStr ?? string.Empty };
            if (!builder.TryGetValue("EmulatedProduct", out var raw))
            {
                EmulatedProduct = SupportedDatabase.Unknown;
            }
            else
            {
                // If parsing fails, default to Unknown rather than throwing
                var rawText = raw?.ToString();
                EmulatedProduct = Enum.TryParse<SupportedDatabase>(rawText, true, out var result)
                    ? result
                    : SupportedDatabase.Unknown;
            }
        }

        return EmulatedProduct;
    }

    private void RaiseStateChangedEvent(ConnectionState originalState)
    {
        if (_state != originalState)
        {
            OnStateChange(new StateChangeEventArgs(originalState, _state));
        }
    }

    /// <summary>
    /// Configure the connection to throw an exception on Close/Dispose.
    /// </summary>
    public void SetFailOnClose(Exception? exception)
    {
        _closeFailureException = exception;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (_shouldFailOnBeginTransaction)
        {
            ThrowConfiguredException("Simulated transaction begin failure");
        }

        if (_isBroken)
        {
            throw new InvalidOperationException("Cannot begin transaction on broken connection");
        }

        if (_state != ConnectionState.Open)
        {
            throw new InvalidOperationException("Connection must be open to begin transaction");
        }

        var tx = new fakeDbTransaction(this, isolationLevel);
        if (_transactionCommitException != null)
        {
            tx.CommitException = _transactionCommitException;
        }

        if (_transactionRollbackException != null)
        {
            tx.RollbackException = _transactionRollbackException;
        }

        return tx;
    }

    protected override DbCommand CreateDbCommand()
    {
        if (_shouldFailOnCommand)
        {
            ThrowConfiguredException("Simulated command creation failure");
        }

        if (_isBroken)
        {
            throw new InvalidOperationException("Cannot create command on broken connection");
        }

        // Invoke custom command behavior if set
        _customCommandBehavior?.Invoke();

        var command = new fakeDbCommand(this);
        if (NextCommandLastInsertedId != null)
        {
            command.LastInsertedId = NextCommandLastInsertedId;
        }

        command.BlockSynchronousExecution = BlockSynchronousCommandExecution;

        CreatedCommands.Add(command);
        LastCreatedCommand = command;
        return command;
    }

    public override DataTable GetSchema()
    {
        if (_schemaTable != null)
        {
            return _schemaTable;
        }

        if (_emulatedProduct is null)
        {
            throw new InvalidOperationException("EmulatedProduct must be configured via connection string.");
        }

        if (_emulatedProduct == SupportedDatabase.Unknown)
        {
            _schemaTable = new DataTable();
            _schemaTable.Columns.Add("DataSourceProductName", typeof(string));
            _schemaTable.Columns.Add("DataSourceProductVersion", typeof(string));
            _schemaTable.Columns.Add("ParameterMarkerPattern", typeof(string));
            _schemaTable.Columns.Add("ParameterMarkerFormat", typeof(string));
            _schemaTable.Columns.Add("ParameterNameMaxLength", typeof(int));
            _schemaTable.Columns.Add("ParameterNamePattern", typeof(string));
            _schemaTable.Columns.Add("ParameterNamePatternRegex", typeof(string));
            _schemaTable.Columns.Add("SupportsNamedParameters", typeof(bool));
            _schemaTable.Rows.Add("UnknownDb", "1", "@p[0-9]+", "@{0}", 64, "@\\w+", "@\\w+", true);
            return _schemaTable;
        }

        var resourceName = $"pengdows.crud.fakeDb.xml.{_emulatedProduct}.schema.xml";

        using var stream = typeof(fakeDbConnection).Assembly
                               .GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException($"Embedded schema not found: {resourceName}");

        var table = new DataTable();
        table.ReadXml(stream);
        _schemaTable = table;
        return _schemaTable;
    }

    public override DataTable GetSchema(string meta)
    {
        if (_schemaTable != null)
        {
            return _schemaTable;
        }

        if (_emulatedProduct is null)
        {
            throw new InvalidOperationException("EmulatedProduct must be configured via connection string.");
        }

        if (_emulatedProduct == SupportedDatabase.Unknown)
        {
            _schemaTable = new DataTable();
            _schemaTable.Columns.Add("DataSourceProductName", typeof(string));
            _schemaTable.Columns.Add("DataSourceProductVersion", typeof(string));
            _schemaTable.Columns.Add("ParameterMarkerPattern", typeof(string));
            _schemaTable.Columns.Add("ParameterMarkerFormat", typeof(string));
            _schemaTable.Columns.Add("ParameterNameMaxLength", typeof(int));
            _schemaTable.Columns.Add("ParameterNamePattern", typeof(string));
            _schemaTable.Columns.Add("ParameterNamePatternRegex", typeof(string));
            _schemaTable.Columns.Add("SupportsNamedParameters", typeof(bool));
            _schemaTable.Rows.Add("UnknownDb", "1", "@p[0-9]+", "@{0}", 64, "@\\w+", "@\\w+", true);
            return _schemaTable;
        }

        var resourceName = $"pengdows.crud.fakeDb.xml.{_emulatedProduct}.schema.xml";

        using var stream = typeof(fakeDbConnection).Assembly
                               .GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException($"Embedded schema not found: {resourceName}");

        var table = new DataTable();
        table.ReadXml(stream);
        _schemaTable = table;
        return _schemaTable;
    }
}