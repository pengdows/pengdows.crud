#region

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbCommand : DbCommand
{
    private bool _shouldFailOnExecute;
    private Exception? _customExecuteException;
    public bool WasDisposed { get; private set; }

    /// <summary>
    /// When true, the synchronous ExecuteNonQuery/ExecuteScalar/ExecuteReader entry points throw
    /// instead of running — while their ...Async counterparts still run normally, with their own
    /// call counts. Lets a test prove production code went through the genuinely async path
    /// (rather than a sync-wrapped-in-Task.FromResult one) without hand-rolling a DbConnection.
    /// </summary>
    public bool BlockSynchronousExecution { get; set; }

    public int SyncExecuteCount { get; private set; }
    public int AsyncExecuteCount { get; private set; }

    private void EnforceAsyncOnly(string operation)
    {
        if (BlockSynchronousExecution)
        {
            throw new InvalidOperationException(
                $"Synchronous {operation}() is blocked on this fakeDbCommand — use the async overload.");
        }
    }

    /// <summary>
    /// Simulates MySqlCommand.LastInsertedId populated from the MySQL OK packet.
    /// When set, MySqlDialect.GetLastInsertedIdFromCommand returns this value via reflection.
    /// Default null triggers the fallback path (SELECT LAST_INSERT_ID()).
    /// </summary>
    public object? LastInsertedId { get; set; }

    public fakeDbCommand(DbConnection connection)
    {
        Connection = connection;
        DbConnection = connection;
    }

    public fakeDbCommand()
    {
    }

    // A command a factory creates with no connection: it reproduces that factory's driver quirks.
    internal fakeDbCommand(fakeDbDriverQuirks quirks)
    {
        _detachedQuirks = quirks;
        if (quirks.CommandTypeDefaultsToZero)
        {
            CommandType = 0;
        }
    }

    private static readonly fakeDbDriverQuirks Conformant = new();
    private readonly fakeDbDriverQuirks? _detachedQuirks;

    // The connection's quirks, or the creating factory's while the command has no fakeDb connection.
    private fakeDbDriverQuirks Quirks => (_connection as fakeDbConnection)?.DriverQuirks ?? _detachedQuirks ?? Conformant;

    private string _commandText = string.Empty;
    private DbConnection? _connection;
    private fakeDbDataReader? _openReader;

    // A command whose reader is still open cannot change its text or connection, or run again (every provider refuses:
    // Microsoft.Data.Sqlite "An open reader is associated with this command. Close it before changing the CommandText
    // property.").
    [AllowNull]
    public override string CommandText
    {
        get => _commandText;
        set
        {
            if (!Quirks.CommandTextChangeableWithOpenReader)
            {
                ThrowIfReaderOpen();
            }

            _commandText = value ?? string.Empty;
        }
    }

    public override int CommandTimeout { get; set; }

    // DbCommand.CommandType: "The default is Text."
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection
    {
        get => _connection;
        set
        {
            if (!Quirks.ConnectionChangeableWithOpenReader)
            {
                ThrowIfReaderOpen();
            }

            _connection = value;
        }
    }

    private void ThrowIfReaderOpen()
    {
        if (_openReader is { IsClosed: false })
        {
            throw new InvalidOperationException(
                "An open reader is associated with this command. Close it before changing or executing the command.");
        }
    }

    // A driver's own error for a parameter value it cannot send (fakeDbDriverQuirks.UnmappableParameterValueException).
    private T WithDriverValueErrors<T>(Func<T> execute)
    {
        try
        {
            return execute();
        }
        catch (FakeConstantSelect.UnmappableValueException ex)
        {
            throw Quirks.UnmappableParameterValueException?.Invoke(ex.Value) ?? new NotSupportedException(ex.Message);
        }
    }

    // Executing and preparing need an open connection, no open reader on this command and, when a transaction is set,
    // that connection's transaction: what every provider checks before sending anything (DbCommand.ExecuteNonQuery,
    // ExecuteScalar, ExecuteReader and Prepare all document InvalidOperationException for these).
    private void ThrowIfCannotExecute(string operation)
    {
        var quirks = Quirks;
        if (_connection == null)
        {
            if (quirks.NullReferenceWithoutConnection)
            {
                throw new NullReferenceException();
            }

            throw new InvalidOperationException($"{operation}: Connection property has not been initialized.");
        }

        if (_connection.State != ConnectionState.Open)
        {
            if (quirks.ScalarNullReferenceOnClosedConnection && operation == nameof(ExecuteScalar))
            {
                throw new NullReferenceException();
            }

            throw new InvalidOperationException(
                $"{operation} requires an open and available Connection. The connection's current state is {_connection.State}.");
        }

        if (_openReader is { IsClosed: false } && !quirks.ReexecuteWithOpenReaderAllowed)
        {
            throw quirks.ReexecuteWithOpenReaderException?.Invoke() ?? new InvalidOperationException(
                "An open reader is associated with this command. Close it before changing or executing the command.");
        }

        if (!quirks.ForeignTransactionAccepted && DbTransaction?.Connection is { } transactionConnection &&
            !ReferenceEquals(transactionConnection, _connection))
        {
            throw new InvalidOperationException(
                "The transaction is either not associated with the current connection or has been completed.");
        }
    }
    // One transaction, whichever way it is set: through this property, DbCommand.Transaction or
    // IDbCommand.Transaction. It was a separate field, so a transaction set through the interface
    // (as pengdows.crud does) read back null here.
    [AllowNull] public new DbTransaction Transaction { get => DbTransaction!; set => DbTransaction = value; }

    private readonly FakeParameterCollection _parameterCollection = new();

    protected override DbParameterCollection DbParameterCollection => _parameterCollection;

    protected override DbTransaction? DbTransaction { get; set; }

    private readonly OperationLog _log = new();

    /// <summary>
    /// The provider-contract members called on this command, in order: Cancel, Prepare, PrepareAsync, ExecuteNonQuery,
    /// ExecuteNonQueryAsync, ExecuteScalar, ExecuteScalarAsync, ExecuteReader(behavior), ExecuteReaderAsync(behavior),
    /// CreateParameter, Dispose and DisposeAsync. A sync member and its async twin are different entries.
    /// </summary>
    public IReadOnlyList<string> Operations => _log.Snapshot();

    public override void Cancel()
    {
        _log.Record("Cancel");
    }

    public override void Prepare()
    {
        _log.Record("Prepare");
        if (!Quirks.PrepareWithoutOpenConnection)
        {
            ThrowIfCannotExecute(nameof(Prepare));
        }

        PrepareCount++;
    }

    /// <summary>Number of times <see cref="Prepare"/> was called on this command.</summary>
    public int PrepareCount { get; private set; }

    /// <summary>
    /// Mirrors ODP.NET's <c>OracleCommand.InitialLONGFetchSize</c>, so tests can see what a dialect sets.
    /// </summary>
    public int InitialLONGFetchSize { get; set; }

    private fakeDbConnection? FakeConnection => Connection as fakeDbConnection;

    /// <summary>
    /// Sets the command to fail on execute operations
    /// </summary>
    public void SetFailOnExecute(bool shouldFail = true, Exception? customException = null)
    {
        _shouldFailOnExecute = shouldFail;
        _customExecuteException = customException;
    }

    private void ThrowIfShouldFail(string operation)
    {
        if (_shouldFailOnExecute)
        {
            throw _customExecuteException ?? new InvalidOperationException($"Simulated {operation} failure");
        }
    }

    public override int ExecuteNonQuery()
    {
        _log.Record("ExecuteNonQuery");
        EnforceAsyncOnly(nameof(ExecuteNonQuery));
        SyncExecuteCount++;
        return ExecuteNonQueryCore();
    }

    private int ExecuteNonQueryCore()
    {
        ThrowIfCannotExecute(nameof(ExecuteNonQuery));
        ThrowIfShouldFail(nameof(ExecuteNonQuery));

        var conn = FakeConnection;

        // An exact-command-text failure (SetCommandFailure) names a specific SQL string, so it
        // must win even when that string happens to be a SET/PRAGMA session-setting command — the
        // test chose that exact text on purpose. This does NOT apply to the queued
        // NonQueryExecuteException dequeue below: that primes "the next non-query call(s)"
        // generically, and session-setting commands routinely run before the real DML the test is
        // actually targeting, so checking it here would let an incidental SET statement consume a
        // queued exception before the intended call ever runs.
        if (TryGetCommandFailure(conn, CommandText, out var exNonQuery))
        {
            throw exNonQuery!;
        }

        // Treat session-setting commands as no-op and do not consume queued NonQueryResults,
        // so tests that seed rows-affected for subsequent DML remain stable.
        if (!string.IsNullOrWhiteSpace(CommandText))
        {
            // Compared in place: upper-casing copied every command's whole text.
            var trimmed = CommandText.AsSpan().TrimStart();
            if (trimmed.StartsWith("SET ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("PRAGMA ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("ALTER SESSION SET", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("SELECT SET_CONFIG(", StringComparison.OrdinalIgnoreCase))
            {
                if (conn != null && !string.IsNullOrWhiteSpace(CommandText))
                {
                    conn.ExecutedNonQueryTexts.Add(CommandText);
                    conn.ExecutedNonQueryCommands.Add(CaptureCommand());
                }

                return 0;
            }
        }

        if (conn != null && conn.TryDequeueNonQueryExecuteException(out var exTransient))
        {
            throw exTransient;
        }

        if (conn != null && !string.IsNullOrWhiteSpace(CommandText))
        {
            conn.ExecutedNonQueryTexts.Add(CommandText);
            conn.ExecutedNonQueryCommands.Add(CaptureCommand());
        }

        // Apply output parameter values if queued
        ApplyOutputParameterValues(conn);

        // Prefer queued results when present (test control)
        if (conn != null && conn.NonQueryResults.Count > 0)
        {
            return conn.NonQueryResults.Dequeue();
        }

        var factory = conn?.FactoryReference;
        if (factory != null)
        {
            if (!string.IsNullOrEmpty(CommandText) && factory.TryGetSharedNonQueryForCommand(CommandText, out var keyed))
            {
                return keyed;
            }

            if (factory.TryDequeueSharedNonQueryResult(out var shared))
            {
                return shared;
            }
        }

        // Use data persistence if enabled
        if (conn != null && conn.EnableDataPersistence && !string.IsNullOrWhiteSpace(CommandText))
        {
            var result = WithDriverValueErrors(() => conn.DataStore.ExecuteNonQuery(CommandText, Parameters));
            return result == -1 && Quirks.NonQueryResultForSelect is { } selectResult ? selectResult : result;
        }

        // A SELECT changes no rows: ExecuteNonQuery returns -1 for it (DbCommand.ExecuteNonQuery).
        if (FakeConstantSelect.StartsWithKeyword(CommandText, "SELECT"))
        {
            return Quirks.NonQueryResultForSelect ?? -1;
        }

        return factory?.DefaultNonQueryResult ?? 1;
    }

    public override object? ExecuteScalar()
    {
        _log.Record("ExecuteScalar");
        EnforceAsyncOnly(nameof(ExecuteScalar));
        SyncExecuteCount++;
        return ExecuteScalarCore();
    }

    private object? ExecuteScalarCore()
    {
        ThrowIfCannotExecute(nameof(ExecuteScalar));
        ThrowIfShouldFail(nameof(ExecuteScalar));

        var conn = FakeConnection;
        if (conn != null)
        {
            // Check persistent scalar exception first
            if (conn.PersistentScalarException != null)
            {
                throw conn.PersistentScalarException;
            }

            if (conn.ScalarExecuteException != null)
            {
                var ex = conn.ScalarExecuteException;
                conn.SetScalarExecuteException(null);
                throw ex;
            }

            if (TryGetCommandFailure(conn, CommandText, out var exScalar))
            {
                throw exScalar!;
            }

            if (!string.IsNullOrWhiteSpace(CommandText))
            {
                conn.ExecutedScalarTexts.Add(CommandText);
            }

            // A resolver takes full, exclusive control over every scalar response by command
            // text — including throwing for a command the test didn't expect — which is exactly
            // what version/flavor-detection tests need instead of the canned defaults below.
            if (conn.ScalarResolver != null)
            {
                return conn.ScalarResolver(CommandText);
            }

            // Apply output parameter values if queued
            ApplyOutputParameterValues(conn);

            // Command-text-based result
            if (!string.IsNullOrEmpty(CommandText) &&
                conn.ScalarResultsByCommand.TryGetValue(CommandText, out var commandResult))
            {
                return commandResult;
            }

            if (!string.IsNullOrEmpty(CommandText) && conn.FactoryReference is { } keyedFactory &&
                keyedFactory.TryGetSharedScalarForCommand(CommandText, out var sharedKeyed))
            {
                return sharedKeyed;
            }

            // If this is any kind of version query and the test queued a scalar (even null/empty), prefer the queued value
            if (!string.IsNullOrEmpty(CommandText))
            {
                var upper = CommandText.TrimStart().ToUpperInvariant();
                var isGenericVersion = upper == "SELECT VERSION()" || upper == "PRAGMA VERSION" ||
                                       upper == "SELECT CURRENT_VERSION";
                var isFirebirdEngine = upper.Contains("RDB$GET_CONTEXT('SYSTEM', 'ENGINE_VERSION')");
                var isFirebirdMonitor = upper.Contains("MON$SERVER_VERSION");
                if ((isGenericVersion || isFirebirdEngine || isFirebirdMonitor) && conn.ScalarResults.Count > 0)
                {
                    return conn.ScalarResults.Dequeue();
                }
            }

            // Apply default scalar only to identity-returning paths and explicit version overrides
            if (conn.DefaultScalarResultOnce != null && !string.IsNullOrWhiteSpace(CommandText))
            {
                var trimmed = CommandText.TrimStart();
                var upper = trimmed.ToUpperInvariant();
                // Version override (tests may supply an explicit version string)
                if (upper == "SELECT VERSION()" || upper == "PRAGMA VERSION" || upper == "SELECT CURRENT_VERSION"
                    || upper.Contains("RDB$GET_CONTEXT('SYSTEM', 'ENGINE_VERSION')")
                    || upper.Contains("MON$SERVER_VERSION"))
                {
                    return conn.ConsumeDefaultScalarOnce();
                }

                // INSERT ... RETURNING should return generated IDs
                if (upper.StartsWith("INSERT") && (upper.Contains("RETURNING") || upper.Contains("OUTPUT INSERTED")))
                {
                    return conn.ConsumeDefaultScalarOnce();
                }

                // Identity retrieval SELECTs
                if (upper.Contains("SCOPE_IDENTITY") || upper.Contains("LASTVAL") ||
                    upper.Contains("LAST_INSERT_ROWID") || upper.Contains("LAST_INSERT_ID"))
                {
                    return conn.ConsumeDefaultScalarOnce();
                }
            }

            // Handle version queries automatically based on emulated product (do not consume the generic queue)
            if (!string.IsNullOrEmpty(CommandText))
            {
                if (TryGetDetectionProbeResult(CommandText, conn.EmulatedProduct, out var probeResult))
                {
                    return probeResult;
                }

                var versionResult = GetVersionQueryResult(CommandText, conn.EmulatedProduct);
                if (versionResult != null)
                {
                    // An explicitly set version answers the product's version query too.
                    return conn.HasExplicitServerVersion ? conn.ServerVersion : versionResult;
                }
            }

            // Prefer queued results when present (test control) for non-version commands
            if (conn.ScalarResults.Count > 0)
            {
                return conn.ScalarResults.Dequeue();
            }

            if (conn.FactoryReference is { } queuedFactory && queuedFactory.TryDequeueSharedScalarResult(out var sharedScalar))
            {
                return sharedScalar;
            }

            // Use data persistence if enabled
            if (conn.EnableDataPersistence && !string.IsNullOrWhiteSpace(CommandText))
            {
                var result = WithDriverValueErrors(() => conn.DataStore.ExecuteScalar(CommandText, Parameters));
                if (result != null)
                {
                    return result;
                }
            }
        }

        return 42;
    }

    // pengdows.crud's database-detection probes, answered as the real servers do: the matching
    // product returns a value and every other one returns no row, so an emulation is never detected
    // as another product by whatever its in-memory engine makes of the probe.
    private static bool TryGetDetectionProbeResult(string commandText, SupportedDatabase emulatedProduct, out object? result)
    {
        var normalized = commandText.Trim().ToUpperInvariant();
        (bool Matches, object Value)? answer = normalized switch
        {
            // Spanner's system schema: 1 on Spanner (confirmed live, Spanner Omni + PGAdapter), 0 elsewhere.
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'SPANNER_SYS'"
                => (true, emulatedProduct == SupportedDatabase.Spanner ? 1L : 0L),
            "SHOW VARIABLES LIKE 'MEMSQL_VERSION'"
                => (emulatedProduct == SupportedDatabase.SingleStore, "memsql_version"),
            "SHOW VARIABLES LIKE 'AURORA_VERSION'"
                => (emulatedProduct == SupportedDatabase.AuroraMySql, "aurora_version"),
            "SELECT PRONAME FROM PG_PROC WHERE PRONAME = 'AURORA_VERSION' LIMIT 1"
                => (emulatedProduct == SupportedDatabase.AuroraPostgreSql, "aurora_version"),
            _ => null
        };

        if (answer is not { } a)
        {
            result = null;
            return false;
        }

        result = a.Matches ? a.Value : DBNull.Value;
        return true;
    }

    private string? GetVersionQueryResult(string commandText, SupportedDatabase emulatedProduct)
    {
        var normalizedCommand = commandText.Trim().ToUpperInvariant();

        // Normalize to treat SELECT CURRENT_VERSION similar to SELECT VERSION()
        var isVersionQuery = normalizedCommand == "SELECT VERSION()" || normalizedCommand == "SELECT CURRENT_VERSION";

        // Handle recognized product/version queries with canned responses to stabilize tests
        return emulatedProduct switch
        {
            SupportedDatabase.SqlServer when normalizedCommand == "SELECT @@VERSION"
                => "Microsoft SQL Server 2019 (RTM) - 15.0.2000.5",

            SupportedDatabase.PostgreSql when isVersionQuery
                => "PostgreSQL 15.0 on x86_64-pc-linux-gnu",

            SupportedDatabase.MySql when isVersionQuery
                => "8.0.33",

            SupportedDatabase.MariaDb when isVersionQuery
                => "10.11.0-MariaDB",

            SupportedDatabase.Sqlite when normalizedCommand == "SELECT SQLITE_VERSION()"
                => "3.42.0",

            SupportedDatabase.Oracle when normalizedCommand.Contains("SELECT * FROM V$VERSION")
                => "Oracle Database 19c Enterprise Edition Release 19.0.0.0.0",

            SupportedDatabase.Firebird when normalizedCommand.Contains("RDB$GET_CONTEXT('SYSTEM', 'ENGINE_VERSION')")
                => "4.0.0",
            // Firebird monitor table fallback used by dialect tests
            SupportedDatabase.Firebird when normalizedCommand.Contains("MON$SERVER_VERSION")
                => "Firebird 4.0.2",

            SupportedDatabase.CockroachDb when isVersionQuery
                => "CockroachDB CCL v23.1.0",

            SupportedDatabase.DuckDB when isVersionQuery
                => "DuckDB 0.9.2",
            // DuckDB pragma fallback used by dialect tests
            SupportedDatabase.DuckDB when normalizedCommand == "PRAGMA VERSION"
                => "v0.9.2",

            SupportedDatabase.Db2 when normalizedCommand.Contains("SYSPROC.ENV_GET_INST_INFO")
                => "11.05.0800",

            _ => null
        };
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        _log.Record($"ExecuteReader({behavior})");
        EnforceAsyncOnly(nameof(ExecuteDbDataReader));
        SyncExecuteCount++;
        return ExecuteDbDataReaderCore(behavior);
    }

    // A reader executed with SequentialAccess enforces it (fakeDbDataReader.EnforceSequentialAccess).
    private DbDataReader ExecuteDbDataReaderCore(CommandBehavior behavior)
    {
        ThrowIfCannotExecute(nameof(ExecuteReader));
        var reader = CreateReader(behavior);
        if (reader is fakeDbDataReader fake)
        {
            if (behavior.HasFlag(CommandBehavior.SequentialAccess))
            {
                fake.EnforceSequentialAccess = true;
            }

            // CommandBehavior.CloseConnection: "the associated Connection object is closed when the associated
            // DataReader object is closed."
            if (behavior.HasFlag(CommandBehavior.CloseConnection) && !Quirks.CloseConnectionIgnored)
            {
                fake.ConnectionToClose = _connection;
            }

            _openReader = fake;
        }

        return reader;
    }

    private DbDataReader CreateReader(CommandBehavior behavior)
    {
        ThrowIfShouldFail(nameof(ExecuteDbDataReader));
        var conn = FakeConnection;
        if (TryGetCommandFailure(conn, CommandText, out var exReader))
        {
            throw exReader!;
        }

        // Propagate persistent scalar exception through the reader path as well,
        // since SqlContainer's scalar methods (ExecuteScalarCore) execute via ExecuteReaderAsync.
        if (conn?.PersistentScalarException != null)
        {
            throw conn.PersistentScalarException;
        }

        // Prefer queued results when present (test control)
        if (conn != null)
        {
            conn.ExecutedReaderTexts.Add(CommandText);
            conn.ExecutedReaderBehaviors.Add(behavior);
            conn.ExecutedReaderCommands.Add(CaptureCommand());
        }

        // Apply output parameter values if queued
        ApplyOutputParameterValues(conn);

        if (conn != null && conn.ReaderResults.Count > 0)
        {
            return conn.ReaderResults.Dequeue();
        }

        // Support for ExecuteScalarRequiredAsync which uses ExecuteReader:
        // If we have ScalarResults but no ReaderResults, convert scalar to a single-row reader.
        if (conn != null && conn.ScalarResults.Count > 0)
        {
            var scalar = conn.ScalarResults.Dequeue();
            var row = new Dictionary<string, object> { ["Value"] = scalar! };
            return new fakeDbDataReader(new[] { row });
        }

        // Use data persistence if enabled
        if (conn != null && conn.EnableDataPersistence && !string.IsNullOrWhiteSpace(CommandText))
        {
            var resultSets = WithDriverValueErrors(() => conn.DataStore.ExecuteReaderResultSets(CommandText, Parameters));
            return new fakeDbDataReader(resultSets.Select(ConvertRows));
        }

        return new fakeDbDataReader();
    }

    // **Async overrides**
    //
    // BlockSynchronousExecution == false (the default, used by every pre-existing caller):
    // delegate to the *virtual* sync method exactly as before this class gained
    // BlockSynchronousExecution/*Core split, so a subclass overriding ExecuteNonQuery()/
    // ExecuteScalar()/ExecuteDbDataReader() (e.g. to record command text) still gets invoked
    // through the async path, unchanged.
    //
    // BlockSynchronousExecution == true (opt-in): the virtual sync method throws by design, so
    // call the private *Core method directly instead — this is the path that lets a test prove
    // production code used the genuinely async overload.
    // A canceled token gives a canceled task and a failure a faulted one, as the base DbCommand async methods do; neither
    // throws from the call itself.
    public override Task<int> ExecuteNonQueryAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<int>(ct);
        }

        _log.Record("ExecuteNonQueryAsync");
        AsyncExecuteCount++;

        var gate = FakeConnection?.ExecuteGate;
        if (gate != null)
        {
            FakeConnection!.SignalExecuteGateEntered();
            return ExecuteWithGateAsync(gate, ct, () => BlockSynchronousExecution ? ExecuteNonQueryCore() : _log.Unlogged(ExecuteNonQuery));
        }

        try
        {
            return Task.FromResult(BlockSynchronousExecution ? ExecuteNonQueryCore() : _log.Unlogged(ExecuteNonQuery));
        }
        catch (Exception ex)
        {
            return Task.FromException<int>(ex);
        }
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<object?>(ct);
        }

        if (_connection == null && Quirks.AsyncNullReferenceWithoutConnection)
        {
            return Task.FromException<object?>(new NullReferenceException());
        }

        _log.Record("ExecuteScalarAsync");
        AsyncExecuteCount++;

        object? RunScalar()
        {
            if (FakeConnection != null && FakeConnection.TryGetAsyncOnlyScalarFailure(CommandText, out var asyncOnlyEx))
            {
                throw asyncOnlyEx;
            }

            return BlockSynchronousExecution ? ExecuteScalarCore() : _log.Unlogged(ExecuteScalar);
        }

        // The async-only-scalar-failure check must happen after the gate wait (not before it),
        // so a connection configured with both an execute gate and an async-only failure still
        // blocks in flight until the gate is released/cancelled, rather than the failure
        // bypassing the gate and returning immediately.
        var gate = FakeConnection?.ExecuteGate;
        if (gate != null)
        {
            FakeConnection!.SignalExecuteGateEntered();
            return ExecuteWithGateAsync(gate, ct, RunScalar);
        }

        try
        {
            return Task.FromResult(RunScalar());
        }
        catch (Exception ex)
        {
            return Task.FromException<object?>(ex);
        }
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken ct)
    {
        if (ct.IsCancellationRequested && !Quirks.CanceledExecuteReaderAsyncRuns)
        {
            return Task.FromCanceled<DbDataReader>(ct);
        }

        if (_connection == null && Quirks.AsyncNullReferenceWithoutConnection)
        {
            return Task.FromException<DbDataReader>(new NullReferenceException());
        }

        _log.Record($"ExecuteReaderAsync({behavior})");
        AsyncExecuteCount++;

        var gate = FakeConnection?.ExecuteGate;
        if (gate != null)
        {
            FakeConnection!.SignalExecuteGateEntered();
            return ExecuteWithGateAsync(gate, ct, () => BlockSynchronousExecution ? ExecuteDbDataReaderCore(behavior) : _log.Unlogged(() => ExecuteDbDataReader(behavior)));
        }

        try
        {
            return Task.FromResult(BlockSynchronousExecution ? ExecuteDbDataReaderCore(behavior) : _log.Unlogged(() => ExecuteDbDataReader(behavior)));
        }
        catch (Exception ex)
        {
            return Task.FromException<DbDataReader>(ex);
        }
    }

    /// <summary>
    /// Shared implementation behind every gated async execute path: awaits the test-controlled
    /// gate (see <see cref="fakeDbConnection.SetExecuteGate"/>), then runs <paramref name="execute"/>.
    /// </summary>
    private static async Task<T> ExecuteWithGateAsync<T>(TaskCompletionSource<bool> gate, CancellationToken ct, Func<T> execute)
    {
        await gate.Task.WaitAsync(ct).ConfigureAwait(false);
        return execute();
    }

    public override Task PrepareAsync(CancellationToken ct)
    {
        _log.Record("PrepareAsync");
        try
        {
            _log.Unlogged(Prepare);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }

        return Task.CompletedTask;
    }

    protected override DbParameter CreateDbParameter()
    {
        _log.Record("CreateParameter");
        // Like a real provider's command, the parameter is the provider's own type, so a factory's
        // parameter-metadata emulation (Npgsql, Informix, ...) applies here too.
        var parameter = (Connection as fakeDbConnection)?.FactoryReference?.CreateEmulatedParameter() ?? new fakeDbParameter();
        Quirks.ApplyTo(parameter);
        return parameter;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _log.Record("Dispose");
        }

        WasDisposed = true;
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        _log.Record("DisposeAsync");
        _log.Unlogged(Dispose);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Snapshots command text and bound parameter values right now, before disposal (or a later
    /// mutation of this same command instance) can erase them.
    /// </summary>
    private CapturedCommand CaptureCommand()
    {
        var parameters = new List<CapturedParameter>(_parameterCollection.Count);
        foreach (DbParameter parameter in _parameterCollection)
        {
            parameters.Add(new CapturedParameter(parameter.ParameterName, parameter.Value)
            {
                DbType = parameter.DbType,
                ProviderType = parameter is fakeDbInformixParameter { IfxType: not fakeIfxType.Unset } informix
                    ? informix.IfxType.ToString()
                    : null
            });
        }

        return new CapturedCommand(CommandText, parameters);
    }

    private static bool TryGetCommandFailure(fakeDbConnection? conn, string? commandText, out Exception? failure)
    {
        failure = null;
        if (string.IsNullOrEmpty(commandText))
        {
            return false;
        }

        return conn?.TryGetCommandFailure(commandText, out failure) == true;
    }

    /// <summary>
    /// Applies queued output parameter values to the command's parameters.
    /// This simulates stored procedure output parameter behavior.
    /// </summary>
    private void ApplyOutputParameterValues(fakeDbConnection? conn)
    {
        if (conn == null || conn.OutputParameterResults.Count == 0)
        {
            return;
        }

        var outputValues = conn.OutputParameterResults.Dequeue();
        foreach (var kvp in outputValues)
        {
            // Find the parameter by name (case-insensitive)
            foreach (DbParameter param in _parameterCollection)
            {
                if (string.Equals(param.ParameterName, kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    if (param.Direction == ParameterDirection.Output ||
                        param.Direction == ParameterDirection.InputOutput ||
                        param.Direction == ParameterDirection.ReturnValue)
                    {
                        param.Value = kvp.Value;
                    }

                    break;
                }
            }
        }
    }

    private static IEnumerable<Dictionary<string, object>> ConvertRows(IEnumerable<Dictionary<string, object?>> rows)
    {
        var converted = new List<Dictionary<string, object>>();

        foreach (var row in rows)
        {
            var map = new Dictionary<string, object>(row.Count);
            foreach (var kvp in row)
            {
                map[kvp.Key] = kvp.Value!;
            }

            converted.Add(map);
        }

        return converted;
    }
}