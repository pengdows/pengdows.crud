using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A database that the engine has deactivated refuses a connection that arrives while it is being activated
/// (Db2 LUW: SQL1035N, SQLSTATE 57019, "cannot be connected to in the mode requested"). The refusal is a race, so
/// constructing a context retries the initial open a few times before it reports the failure.
/// </summary>
public sealed class DatabaseContextOpenRetryTests
{
    private const string Db2Refusal =
        "[IBM][DB2] SQL1035N  The operation failed because the specified database cannot be connected to in the mode requested.  SQLSTATE=57019";

    // A connection whose first open is refused (as above) and whose later opens succeed.
    private static fakeDbConnection RefusedOnceThenOpens(int refusals)
    {
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Db2 };
        var refused = 0;
        connection.SetCustomOpenBehavior(() =>
        {
            if (refused < refusals)
            {
                refused++;
                throw new InvalidOperationException(Db2Refusal);
            }
        });
        return connection;
    }

    private static (fakeDbFactory Factory, DatabaseContextConfiguration Config) Db2Setup(int refusalsPerConnection)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Db2);
        for (var i = 0; i < 12; i++)
        {
            factory.Connections.Add(RefusedOnceThenOpens(refusalsPerConnection));
        }

        return (factory, new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=db2;Database=testdb;EmulatedProduct=Db2",
            DbMode = DbMode.Standard
        });
    }

    [Fact]
    public void Construction_RetriesAOpenRefusedWhileTheDatabaseIsActivating_AndSucceeds()
    {
        var (factory, config) = Db2Setup(refusalsPerConnection: 1);

        using var context = new DatabaseContext(config, factory);

        Assert.Equal(SupportedDatabase.Db2, context.Product);
    }

    [Fact]
    public void Construction_OtherOpenFailures_ReportConnectionFailedAtOnce()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Db2);
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Db2 };
        var opens = 0;
        connection.SetCustomOpenBehavior(() =>
        {
            opens++;
            throw new InvalidOperationException("the server rejected the password");
        });
        factory.Connections.Add(connection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=db2;Database=testdb;EmulatedProduct=Db2",
            DbMode = DbMode.Standard
        };

        Assert.Throws<ConnectionFailedException>(() => new DatabaseContext(config, factory));
        Assert.Equal(1, opens);
    }

    [Fact]
    public void Construction_RefusalThatPersistsPastTheRetryLimit_ReportsConnectionFailed()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Db2);
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Db2 };
        var opens = 0;
        connection.SetCustomOpenBehavior(() =>
        {
            opens++;
            throw new InvalidOperationException(Db2Refusal);
        });
        factory.Connections.Add(connection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=db2;Database=testdb;EmulatedProduct=Db2",
            DbMode = DbMode.Standard
        };

        Assert.Throws<ConnectionFailedException>(() => new DatabaseContext(config, factory));
        Assert.True(opens > 1 && opens <= 5, $"expected a bounded number of attempts, got {opens}");
    }
}
