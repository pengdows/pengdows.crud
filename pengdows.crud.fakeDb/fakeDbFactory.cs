#region

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

#endregion

namespace pengdows.crud.fakeDb;

public sealed partial class fakeDbFactory : DbProviderFactory, IFakeDbFactory
{
    public static readonly fakeDbFactory Instance = new();
    private readonly SupportedDatabase _pretendToBe;
    private ConnectionFailureMode _failureMode;
    private Exception? _customException;
    private int? _failAfterCount;
    private int _sharedOpenCount;
    private bool _skipFirstOpen;
    private bool _hasOpenedOnce;
    private readonly List<fakeDbConnection> _connections = new();
    private readonly List<fakeDbConnection> _createdConnections = new();
    private readonly List<FakeDbDataSource> _createdDataSources = new();
    private Exception? _globalPersistentScalarException;
    private Exception? _globalTransactionCommitException;
    private Exception? _globalTransactionRollbackException;
    public bool EnableDataPersistence { get; set; } = false;

    /// <summary>
    /// When set, propagated to every <see cref="fakeDbConnection.CommandFactory"/> this factory
    /// creates (or hands back from the pre-enqueued queue) — see that property's remarks.
    /// </summary>
    public Func<fakeDbConnection, fakeDbCommand>? CommandFactory { get; set; }

    /// <summary>
    /// When true, <see cref="CreateDataSource"/> returns a <see cref="FakeDbDataSource"/> wrapping
    /// this factory (a provider-native data source), so tests can exercise DatabaseContext's
    /// provider-native-DataSource path without hand-rolling a DbProviderFactory/DbDataSource pair.
    /// When false (the default), it returns .NET's default data source, as a provider without its
    /// own does and as 2.0.5 did; DatabaseContext then uses its GenericDbDataSource.
    /// </summary>
    public bool SupportsNativeDataSource { get; set; } = false;

    /// <summary>
    /// When set, every <see cref="FakeDbDataSource"/> this factory creates via
    /// <see cref="CreateDataSource"/> has its <see cref="FakeDbDataSource.ThrowOnDispose"/> set to
    /// this exception — lets a test make an INTERNALLY-created data source (one the test never
    /// gets a direct handle to before construction fails) throw during cleanup, to prove the
    /// original construction exception still propagates rather than being replaced by the
    /// cleanup failure.
    /// </summary>
    public Exception? ThrowOnDataSourceDispose { get; set; }

    /// <summary>
    /// When set, <see cref="CreateConnection"/> throws this exception immediately instead of
    /// returning a connection — lets a test exercise a caller's handling of a factory-level
    /// failure (e.g. a misconfigured provider registration) without a bespoke DbProviderFactory
    /// subclass.
    /// </summary>
    public Exception? ThrowOnCreateConnection { get; set; }

    /// <summary>
    /// Server version every connection this factory creates reports, through
    /// <see cref="DbConnection.ServerVersion"/> and the product's version query (for example
    /// <c>SELECT VERSION()</c>). Null keeps fakeDb's canned per-product version.
    /// </summary>
    public string? ServerVersion { get; set; }

    /// <summary>
    /// When true, <see cref="CreateConnection"/> returns null instead of a connection —
    /// DbProviderFactory.CreateConnection is documented nullable, and some callers (e.g. a
    /// best-effort pool-reset hook probing for a sample connection) must tolerate a provider that
    /// genuinely can't produce one.
    /// </summary>
    public bool ReturnNullConnection { get; set; }

    /// <summary>
    /// When false, connections from this factory never raise <see cref="DbConnection.StateChange"/>,
    /// like drivers that don't implement it (Snowflake.Data's SnowflakeDbConnection). Default true.
    /// </summary>
    public bool RaiseConnectionStateChangeEvents { get; set; } = true;

    /// <summary>
    /// When true, connections from this factory throw <see cref="ObjectDisposedException"/> when a
    /// <see cref="DbConnection.StateChange"/> handler is added or removed after they are disposed, as
    /// AdoNetCore.AseClient's AseConnection does. Default false.
    /// </summary>
    public bool ThrowOnStateChangeAccessAfterDispose { get; set; }

    internal ConnectionStringBuilderBehavior ConnectionStringBuilderBehavior { get; set; } =
        ConnectionStringBuilderBehavior.None;

    /// <summary>
    /// Keywords the emulated provider's typed builder knows, for
    /// <see cref="ConnectionStringBuilderBehavior.ReportKnownKeywordsAsPresent"/>.
    /// </summary>
    internal IReadOnlyCollection<string> KnownConnectionStringKeywords { get; set; } = Array.Empty<string>();

    // Shared data store across all connections from this factory
    private readonly FakeDataStore _sharedDataStore = new();

    private readonly Dictionary<string, Exception> _sharedCommandFailures =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Exception> _failOnOpenByConnectionString =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Makes only connections whose exact <see cref="fakeDbConnection.ConnectionString"/>
    /// equals <paramref name="connectionString"/> fail on <c>Open()</c>/<c>OpenAsync()</c> — unlike
    /// the factory-wide <see cref="ConnectionFailureMode.FailOnOpen"/>, this lets a test fail one
    /// specific connection-string role (e.g. <c>DatabaseContext</c>'s distinct read-only validation
    /// connection string) without also breaking earlier connections built from a different
    /// connection string (the writer connection string, or a dialect-detection probe).
    /// </summary>
    public void SetFailOnOpenForConnectionString(string connectionString, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(exception);
        _failOnOpenByConnectionString[connectionString] = exception;
    }

    internal bool TryGetFailOnOpenForConnectionString(string connectionString, [NotNullWhen(true)] out Exception? exception)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            exception = null;
            return false;
        }

        return _failOnOpenByConnectionString.TryGetValue(connectionString, out exception);
    }

    private readonly Dictionary<string, System.Threading.Tasks.TaskCompletionSource<bool>> _openGateByConnectionString =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Makes only connections whose exact <see cref="fakeDbConnection.ConnectionString"/> equals
    /// <paramref name="connectionString"/> await a test-controlled gate before completing
    /// <c>OpenAsync</c>, instead of completing immediately — the connection-string-scoped analog of
    /// <see cref="fakeDbConnection.SetOpenGate"/>. Lets a test cancel the token passed to
    /// <c>OpenAsync</c> for one specific connection-string role (e.g. a distinct read-only
    /// validation connection string) while earlier connections built from a different connection
    /// string (the writer string, or a dialect-detection probe) open normally and unblocked.
    /// </summary>
    public System.Threading.Tasks.TaskCompletionSource<bool> SetOpenGateForConnectionString(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        _openGateByConnectionString[connectionString] = tcs;
        return tcs;
    }

    internal bool TryGetOpenGateForConnectionString(
        string connectionString,
        [NotNullWhen(true)] out System.Threading.Tasks.TaskCompletionSource<bool>? gate)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            gate = null;
            return false;
        }

        return _openGateByConnectionString.TryGetValue(connectionString, out gate);
    }

    private fakeDbFactory()
    {
        _pretendToBe = SupportedDatabase.Unknown;
    }

    public fakeDbFactory(string pretendToBe)
    {
        _pretendToBe = Enum.Parse<SupportedDatabase>(pretendToBe);
    }

    public fakeDbFactory(SupportedDatabase pretendToBe)
    {
        _pretendToBe = pretendToBe;
        _failureMode = ConnectionFailureMode.None;
    }

    public fakeDbFactory(SupportedDatabase pretendToBe, ConnectionFailureMode failureMode,
        Exception? customException = null, int? failAfterCount = null)
    {
        _pretendToBe = pretendToBe;
        _failureMode = failureMode;
        _customException = customException;
        _failAfterCount = failAfterCount;
        _skipFirstOpen = false; // Default to not skipping
    }

    private fakeDbFactory(SupportedDatabase pretendToBe, ConnectionFailureMode failureMode, Exception? customException,
        int? failAfterCount, bool skipFirstOpen)
    {
        _pretendToBe = pretendToBe;
        _failureMode = failureMode;
        _customException = customException;
        _failAfterCount = failAfterCount;
        _skipFirstOpen = skipFirstOpen;
    }

    public SupportedDatabase PretendToBe => _pretendToBe;

    public override DbCommand CreateCommand()
    {
        return new fakeDbCommand();
    }

    public override DbConnection CreateConnection()
    {
        if (ThrowOnCreateConnection != null)
        {
            throw ThrowOnCreateConnection;
        }

        if (ReturnNullConnection)
        {
            // This override is declared non-nullable, matching every real provider's factory
            // — but ReturnNullConnection exists specifically to test a
            // caller's defensive handling of a provider that misbehaves at runtime despite the
            // contract, so the null-forgiving operator here is a deliberate, narrow lie.
            return null!;
        }

        if (_connections.Count > 0)
        {
            var pre = _connections[0];
            _connections.RemoveAt(0);
            if (pre.EmulatedProduct == SupportedDatabase.Unknown)
            {
                pre.EmulatedProduct = _pretendToBe;
            }

            // Apply data persistence setting from factory
            pre.EnableDataPersistence = EnableDataPersistence;
            pre.RaiseStateChangeEvents = RaiseConnectionStateChangeEvents;
            pre.ThrowOnStateChangeAccessAfterDispose |= ThrowOnStateChangeAccessAfterDispose;
            pre.CommandFactory ??= CommandFactory;
            pre.SetFactoryReference(this);
            if (ServerVersion != null)
            {
                pre.SetServerVersion(ServerVersion);
            }

            _createdConnections.Add(pre);
            return pre;
        }

        var c = new fakeDbConnection(_sharedDataStore);
        c.EmulatedProduct = _pretendToBe;
        c.RaiseStateChangeEvents = RaiseConnectionStateChangeEvents;
        c.ThrowOnStateChangeAccessAfterDispose = ThrowOnStateChangeAccessAfterDispose;

        // Configure failure modes based on factory settings
        if (_customException != null)
        {
            c.SetCustomFailureException(_customException);
        }

        switch (_failureMode)
        {
            case ConnectionFailureMode.FailOnOpen:
                c.SetFailOnOpen();
                c.SetFactoryReference(this);
                break;
            case ConnectionFailureMode.FailOnCommand:
                c.SetFailOnCommand();
                break;
            case ConnectionFailureMode.FailOnTransaction:
                c.SetFailOnBeginTransaction();
                break;
            case ConnectionFailureMode.FailAfterCount when _failAfterCount.HasValue:
                c.SetSharedFailAfterOpenCount(this, _failAfterCount.Value);
                break;
            case ConnectionFailureMode.Broken:
                c.SetFactoryReference(this);
                c.BreakConnection(); // Don't skip, factory will decide
                break;
        }

        // Apply any factory-level exception configuration to new connections
        if (_globalPersistentScalarException != null)
        {
            c.SetPersistentScalarException(_globalPersistentScalarException);
        }

        if (_globalTransactionCommitException != null)
        {
            c.SetTransactionCommitException(_globalTransactionCommitException);
        }

        if (_globalTransactionRollbackException != null)
        {
            c.SetTransactionRollbackException(_globalTransactionRollbackException);
        }

        // Apply data persistence setting from factory
        c.EnableDataPersistence = EnableDataPersistence;
        c.CommandFactory = CommandFactory;

        c.SetFactoryReference(this);
        if (ServerVersion != null)
        {
            c.SetServerVersion(ServerVersion);
        }

        _createdConnections.Add(c);
        return c;
    }

    IFakeDbConnection IFakeDbFactory.CreateConnection()
    {
        return (fakeDbConnection)CreateConnection();
    }

    public void SetGlobalPersistentScalarException(Exception? exception)
    {
        _globalPersistentScalarException = exception;
    }

    /// <summary>Sets an exception to throw when any connection's transaction Commit() is called.</summary>
    public void SetGlobalTransactionCommitException(Exception exception)
    {
        _globalTransactionCommitException = exception;
    }

    /// <summary>Sets an exception to throw when any connection's transaction Rollback() is called.</summary>
    public void SetGlobalTransactionRollbackException(Exception exception)
    {
        _globalTransactionRollbackException = exception;
    }

    public void SetCommandFailure(string commandText, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(commandText);
        ArgumentNullException.ThrowIfNull(exception);
        _sharedCommandFailures[commandText] = exception;
    }

    internal bool TryGetCommandFailure(string commandText, [NotNullWhen(true)] out Exception? exception)
    {
        return _sharedCommandFailures.TryGetValue(commandText, out exception);
    }

    public void EnqueueReaderResult(IEnumerable<Dictionary<string, object>> rows)
    {
        var conn = (fakeDbConnection)CreateConnection();
        // Staged, not handed out: it is recorded when a caller's CreateConnection takes it.
        _createdConnections.Remove(conn);
        conn.EnqueueReaderResult(rows.Select(static row =>
            row.ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value)));
        _connections.Insert(0, conn);
    }

    public override DbParameter CreateParameter()
    {
        if (EmulatesOracleParameterMetadata)
        {
            return new fakeDbOracleParameter();
        }

        if (EmulatesInterBaseParameterMetadata)
        {
            return new fakeDbInterBaseParameter();
        }

        if (EmulatesInformixParameterMetadata)
        {
            return new fakeDbInformixParameter();
        }

        return EmulatesNpgsqlParameterMetadata ? new fakeDbNpgsqlParameter() : new fakeDbParameter();
    }

    /// <summary>
    /// When true, parameters carry Informix.Net.Core's <c>IfxType</c> property (see
    /// <see cref="fakeDbInformixParameter"/>).
    /// </summary>
    public bool EmulatesInformixParameterMetadata { get; set; }

    /// <summary>
    /// When true, parameters carry ODP.NET's <c>OracleDbType</c> property, so tests can see what a
    /// dialect stamps on them for ODP.NET.
    /// </summary>
    public bool EmulatesOracleParameterMetadata { get; set; }

    /// <summary>
    /// When true, parameters behave like InterBase's <c>IBParameter</c> for arrays (see
    /// <see cref="fakeDbInterBaseParameter"/>).
    /// </summary>
    public bool EmulatesInterBaseParameterMetadata { get; set; }

    /// <summary>
    /// When true, parameters carry Npgsql's provider metadata properties (<c>NpgsqlDbType</c> and
    /// <c>DataTypeName</c>), so tests can see what a dialect stamps on them for Npgsql.
    /// </summary>
    public bool EmulatesNpgsqlParameterMetadata { get; set; }

    public override DbDataSource CreateDataSource(string connectionString)
    {
        if (!SupportsNativeDataSource)
        {
            // DEC-009: what 2.0.5 (no override) and a real provider without its own data source
            // return. DatabaseContext recognizes .NET's default data source as non-native and uses
            // its generic wrapper, exactly as for such a provider.
            return base.CreateDataSource(connectionString);
        }

        var dataSource = new FakeDbDataSource(connectionString, this);
        if (ThrowOnDataSourceDispose != null)
        {
            dataSource.ThrowOnDispose = ThrowOnDataSourceDispose;
        }

        _createdDataSources.Add(dataSource);
        return dataSource;
    }

    /// <summary>
    /// Every FakeDbDataSource this factory has created via CreateDataSource, in creation order.
    /// Lets tests verify disposal of internally-created data sources they never received a
    /// direct handle to (e.g. ones DatabaseContext creates itself during construction).
    /// </summary>
    public IReadOnlyList<FakeDbDataSource> CreatedDataSources => _createdDataSources;

    /// <summary>
    /// Increments the shared open count and returns the new value, optionally skipping the first open
    /// </summary>
    internal int IncrementSharedOpenCount()
    {
        if (_skipFirstOpen)
        {
            _skipFirstOpen = false;
            return 0; // Don't count the first open (context initialization)
        }

        return Interlocked.Increment(ref _sharedOpenCount);
    }

    /// <summary>
    /// Checks if this is the first open across all connections from this factory
    /// </summary>
    internal bool ShouldSkipThisOpen()
    {
        if (_skipFirstOpen && !_hasOpenedOnce)
        {
            _hasOpenedOnce = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a factory whose connections fail according to <paramref name="failureMode"/>
    /// </summary>
    public static fakeDbFactory CreateFailingFactory(SupportedDatabase pretendToBe, ConnectionFailureMode failureMode,
        Exception? customException = null, int? failAfterCount = null)
    {
        return new fakeDbFactory(pretendToBe, failureMode, customException, failAfterCount);
    }

    /// <summary>
    /// Creates a factory for helper methods that skip the first open (for DatabaseContext initialization)
    /// </summary>
    internal static fakeDbFactory CreateFailingFactoryWithSkip(SupportedDatabase pretendToBe,
        ConnectionFailureMode failureMode, Exception? customException = null, int? failAfterCount = null)
    {
        var skipFirst = failureMode == ConnectionFailureMode.FailOnOpen ||
                        failureMode == ConnectionFailureMode.Broken ||
                        failureMode == ConnectionFailureMode.FailAfterCount;
        return new fakeDbFactory(pretendToBe, failureMode, customException, failAfterCount, skipFirst);
    }

    /// <summary>
    /// Pre-enqueue connections to be returned by CreateConnection
    /// </summary>
    public List<fakeDbConnection> Connections => _connections;

    /// <summary>
    /// All connections created by this factory (for test assertions)
    /// </summary>
    public IReadOnlyList<fakeDbConnection> CreatedConnections => _createdConnections;

    public override DbConnectionStringBuilder? CreateConnectionStringBuilder()
    {
        // Return a plain (DbConnectionStringBuilder-based) builder, honoring any configured
        // ConnectionStringBuilderBehavior test hooks
        if (ConnectionStringBuilderBehavior.HasFlag(ConnectionStringBuilderBehavior.ReturnNull))
        {
            return null;
        }

        return new fakeDbConnectionStringBuilder(_pretendToBe, ConnectionStringBuilderBehavior,
            KnownConnectionStringKeywords);
    }
}

public enum ConnectionFailureMode
{
    None,
    FailOnOpen,
    FailOnCommand,
    FailOnTransaction,
    FailAfterCount,
    Broken
}
