using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class InternalConnectionStringAccessTests
{
    [Fact]
    public void GetRawConnectionString_TransactionContext_ReturnsUnredactedConnectionString()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString =
                "Data Source=test.db;Password=super-secret;Token=abc123;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
        using var transaction = context.BeginTransaction();

        Assert.Contains("super-secret", InternalConnectionStringAccess.GetRawConnectionString(transaction),
            StringComparison.Ordinal);
        Assert.DoesNotContain("REDACTED", InternalConnectionStringAccess.GetRawConnectionString(transaction),
            StringComparison.OrdinalIgnoreCase);
    }
}
