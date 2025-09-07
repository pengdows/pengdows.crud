#region

using System.Data.Common;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

public sealed class fakeDbFactory : DbProviderFactory
{
    public static readonly fakeDbFactory Instance = new();
    private readonly SupportedDatabase _pretendToBe;
    private readonly ConnectionFailureMode _failureMode;
    private readonly Exception? _customException;
    private readonly int? _failAfterCount;
    private int _sharedOpenCount;
    private bool _skipFirstOpen;
    private bool _hasOpenedOnce;
    private readonly List<fakeDbConnection> _connections = new();

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
            return pre;
        }

        var c = new fakeDbConnection();
        c.EmulatedProduct = _pretendToBe;

        // Configure failure modes based on factory settings
        if (_customException != null)
        {
            c.SetCustomFailureException(_customException);
        }

        switch (_failureMode)
        {
            case ConnectionFailureMode.FailOnOpen:
                c.SetFailOnOpen(true);
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
                c.BreakConnection(false); // Don't skip, factory will decide
                break;
        }

        return c;
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
        return System.Threading.Interlocked.Increment(ref _sharedOpenCount);
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
