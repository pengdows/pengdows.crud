#region

using System.Data.Common;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.FakeDb;

public sealed class FakeDbFactory : DbProviderFactory
{
    public static readonly FakeDbFactory Instance = new();
    private readonly SupportedDatabase _pretendToBe;
    private readonly ConnectionFailureMode _failureMode;
    private readonly Exception? _customException;
    private readonly int? _failAfterCount;

    private FakeDbFactory()
    {
        _pretendToBe = SupportedDatabase.Unknown;
    }

    public FakeDbFactory(string pretendToBe)
    {
        _pretendToBe = Enum.Parse<SupportedDatabase>(pretendToBe);
    }

    public FakeDbFactory(SupportedDatabase pretendToBe)
    {
        _pretendToBe = pretendToBe;
        _failureMode = ConnectionFailureMode.None;
    }

    public FakeDbFactory(SupportedDatabase pretendToBe, ConnectionFailureMode failureMode, Exception? customException = null, int? failAfterCount = null)
    {
        _pretendToBe = pretendToBe;
        _failureMode = failureMode;
        _customException = customException;
        _failAfterCount = failAfterCount;
    }

    public override DbCommand CreateCommand()
    {
        return new FakeDbCommand();
    }

    public override DbConnection CreateConnection()
    {
        var c = new FakeDbConnection();
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
                break;
            case ConnectionFailureMode.FailOnCommand:
                c.SetFailOnCommand();
                break;
            case ConnectionFailureMode.FailOnTransaction:
                c.SetFailOnBeginTransaction();
                break;
            case ConnectionFailureMode.FailAfterCount when _failAfterCount.HasValue:
                c.SetFailAfterOpenCount(_failAfterCount.Value);
                break;
            case ConnectionFailureMode.Broken:
                c.BreakConnection();
                break;
        }
        
        return c;
    }

    public override DbParameter CreateParameter()
    {
        return new FakeDbParameter();
    }
    
    /// <summary>
    /// Creates a factory that produces connections that fail on open
    /// </summary>
    public static FakeDbFactory CreateFailingFactory(SupportedDatabase pretendToBe, ConnectionFailureMode failureMode, Exception? customException = null, int? failAfterCount = null)
    {
        return new FakeDbFactory(pretendToBe, failureMode, customException, failAfterCount);
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