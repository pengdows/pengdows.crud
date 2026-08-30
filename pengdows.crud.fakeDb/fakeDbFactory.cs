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
    /// When true, <see cref="CreateDataSource"/> returns a <see cref="FakeDbDataSource"/> wrapping
    /// this factory instead of throwing <see cref="NotSupportedException"/> — opt-in so tests that
    /// specifically need to exercise DatabaseContext's provider-native-DataSource path don't have
    /// to hand-roll their own DbProviderFactory/DbDataSource pair. Defaults to false so every
    /// existing caller keeps falling back to GenericDbDataSource, unchanged.
    /// </summary>
    public bool SupportsNativeDataSource { get; set; } = false;

    /// <summary>
    /// TEST-017: when set, every <see cref="FakeDbDataSource"/> this factory creates via
    /// <see cref="CreateDataSource"/> has its <see cref="FakeDbDataSource.ThrowOnDispose"/> set to
    /// this exception — lets a test make an INTERNALLY-created data source (one the test never
    /// gets a direct handle to before construction fails) throw during cleanup, to prove the
    /// original construction exception still propagates rather than being replaced by the
    /// cleanup failure.
    /// </summary>
    public Exception? ThrowOnDataSourceDispose { get; set; }

    internal ConnectionStringBuilderBehavior ConnectionStringBuilderBehavior { get; set; } =
        ConnectionStringBuilderBehavior.None;

    // Shared data store across all connections from this factory
    private readonly FakeDataStore _sharedDataStore = new();

    private readonly Dictionary<string, Exception> _sharedCommandFailures =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Exception> _failOnOpenByConnectionString =
        new(StringComparer.Ordinal);

    /// <summary>
    /// TEST-017: makes only connections whose exact <see cref="fakeDbConnection.ConnectionString"/>
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
            pre.SetFactoryReference(this);
            _createdConnections.Add(pre);
            return pre;
        }

        var c = new fakeDbConnection(_sharedDataStore);
        c.EmulatedProduct = _pretendToBe;

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

        c.SetFactoryReference(this);
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
        conn.EnqueueReaderResult(rows.Select(static row =>
            row.ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value)));
        _connections.Insert(0, conn);
    }

    public override DbParameter CreateParameter()
    {
        return new fakeDbParameter();
    }

    public override DbDataSource CreateDataSource(string connectionString)
    {
        if (!SupportsNativeDataSource)
        {
            // DbProviderFactory's own base implementation does NOT throw (it returns a
            // DefaultDataSource) — but DatabaseContext's reflection-based provider-native probe
            // (TryCreateProviderDataSource) treats a caught NotSupportedException as "provider
            // explicitly opts out" and falls back to GenericDbDataSource. Throwing here, rather
            // than delegating to base, is what actually preserves the pre-existing fallback
            // behavior for every caller that hasn't opted into SupportsNativeDataSource.
            throw new NotSupportedException(
                $"{nameof(fakeDbFactory)} does not support {nameof(CreateDataSource)} unless {nameof(SupportsNativeDataSource)} is set to true.");
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
    /// Creates a factory that produces connections that fail on open
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
        // Return a connection string builder that supports provider-specific keys
        // based on which database we're emulating
        if (ConnectionStringBuilderBehavior.HasFlag(ConnectionStringBuilderBehavior.ReturnNull))
        {
            return null;
        }

        return new fakeDbConnectionStringBuilder(_pretendToBe, ConnectionStringBuilderBehavior);
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
