#region

using System;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

#endregion

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// Helper class for configuring connection failure scenarios in tests
/// </summary>
public static class ConnectionFailureHelper
{
    /// <summary>
    /// Creates a DatabaseContext with a connection that fails on open
    /// </summary>
    public static IDatabaseContext CreateFailOnOpenContext(SupportedDatabase database = SupportedDatabase.Sqlite, Exception? customException = null)
    {
        var factory = fakeDbFactory.CreateFailingFactoryWithSkip(database, ConnectionFailureMode.FailOnOpen, customException);
        return new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);
    }

    /// <summary>
    /// Creates a DatabaseContext with a connection that fails on command creation
    /// </summary>
    public static IDatabaseContext CreateFailOnCommandContext(SupportedDatabase database = SupportedDatabase.Sqlite, Exception? customException = null)
    {
        var factory = fakeDbFactory.CreateFailingFactoryWithSkip(database, ConnectionFailureMode.FailOnCommand, customException);
        return new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);
    }

    /// <summary>
    /// Creates a DatabaseContext with a connection that fails on transaction begin
    /// </summary>
    public static IDatabaseContext CreateFailOnTransactionContext(SupportedDatabase database = SupportedDatabase.Sqlite, Exception? customException = null)
    {
        var factory = fakeDbFactory.CreateFailingFactoryWithSkip(database, ConnectionFailureMode.FailOnTransaction, customException);
        return new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);
    }

    /// <summary>
    /// Creates a DatabaseContext with a connection that fails after N open operations
    /// </summary>
    public static IDatabaseContext CreateFailAfterCountContext(int failAfterCount, SupportedDatabase database = SupportedDatabase.Sqlite, Exception? customException = null)
    {
        var factory = fakeDbFactory.CreateFailingFactoryWithSkip(database, ConnectionFailureMode.FailAfterCount, customException, failAfterCount);
        return new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);
    }

    /// <summary>
    /// Creates a DatabaseContext with a pre-broken connection
    /// </summary>
    public static IDatabaseContext CreateBrokenConnectionContext(SupportedDatabase database = SupportedDatabase.Sqlite)
    {
        var factory = fakeDbFactory.CreateFailingFactoryWithSkip(database, ConnectionFailureMode.Broken);
        return new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);
    }

    /// <summary>
    /// Configures an existing fakeDbConnection with failure modes
    /// </summary>
    public static void ConfigureConnectionFailure(fakeDbConnection connection, ConnectionFailureMode mode, Exception? customException = null, int? failAfterCount = null)
    {
        if (customException != null)
        {
            connection.SetCustomFailureException(customException);
        }

        switch (mode)
        {
            case ConnectionFailureMode.FailOnOpen:
                connection.SetFailOnOpen();
                break;
            case ConnectionFailureMode.FailOnCommand:
                connection.SetFailOnCommand();
                break;
            case ConnectionFailureMode.FailOnTransaction:
                connection.SetFailOnBeginTransaction();
                break;
            case ConnectionFailureMode.FailAfterCount when failAfterCount.HasValue:
                connection.SetFailAfterOpenCount(failAfterCount.Value);
                break;
            case ConnectionFailureMode.Broken:
                connection.BreakConnection();
                break;
        }
    }

    /// <summary>
    /// Creates common database exceptions for testing
    /// </summary>
    public static class CommonExceptions
    {
        public static Exception Timeout => new TimeoutException("Connection timeout");
        public static Exception NetworkError => new InvalidOperationException("Network error");
        public static Exception AuthenticationError => new UnauthorizedAccessException("Authentication failed");
        public static Exception DatabaseUnavailable => new InvalidOperationException("Database unavailable");
        public static Exception InvalidConnectionString => new ArgumentException("Invalid connection string");

        public static DbException CreateDbException(string message)
        {
            // Since DbException is abstract and has protected constructors,
            // we'll create a custom implementation for testing
            return new TestDbException(message);
        }
    }

    /// <summary>
    /// Custom DbException implementation for testing
    /// </summary>
    private class TestDbException : DbException
    {
        public TestDbException(string message) : base(message) { }

        public override int ErrorCode => -1;
    }
}
