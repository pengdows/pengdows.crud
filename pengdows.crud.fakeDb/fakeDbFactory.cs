#region

using System.Data.Common;
using pengdows.crud.enums;

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
    private Exception? _globalPersistentScalarException;
    public bool EnableDataPersistence { get; set; } = false;

    // Shared data store across all connections from this factory
    private readonly FakeDataStore _sharedDataStore = new();

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

    public fakeDbFactory(SupportedDatabase pretendToBe, ConnectionFailureMode failureMode, Exception? customException = null, int? failAfterCount = null)
    {
        _pretendToBe = pretendToBe;
        _failureMode = failureMode;
        _customException = customException;
        _failAfterCount = failAfterCount;
        _skipFirstOpen = false; // Default to not skipping
    }

    private fakeDbFactory(SupportedDatabase pretendToBe, ConnectionFailureMode failureMode, Exception? customException, int? failAfterCount, bool skipFirstOpen)
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

        // Apply data persistence setting from factory
        c.EnableDataPersistence = EnableDataPersistence;

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

    public override DbParameter CreateParameter()
    {
        return new fakeDbParameter();
    }

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
    public static fakeDbFactory CreateFailingFactory(SupportedDatabase pretendToBe, ConnectionFailureMode failureMode, Exception? customException = null, int? failAfterCount = null)
    {
        return new fakeDbFactory(pretendToBe, failureMode, customException, failAfterCount);
    }

    /// <summary>
    /// Creates a factory for helper methods that skip the first open (for DatabaseContext initialization)
    /// </summary>
    internal static fakeDbFactory CreateFailingFactoryWithSkip(SupportedDatabase pretendToBe, ConnectionFailureMode failureMode, Exception? customException = null, int? failAfterCount = null)
    {
        bool skipFirst = failureMode == ConnectionFailureMode.FailOnOpen ||
                        failureMode == ConnectionFailureMode.Broken ||
                        failureMode == ConnectionFailureMode.FailAfterCount;
        return new fakeDbFactory(pretendToBe, failureMode, customException, failAfterCount, skipFirst);
    }

    // Expose created connections for tests
    public List<fakeDbConnection> Connections => _connections;

    public override DbConnectionStringBuilder? CreateConnectionStringBuilder()
    {
        // Return a connection string builder that supports provider-specific keys
        // based on which database we're emulating
        return new fakeDbConnectionStringBuilder(_pretendToBe);
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
