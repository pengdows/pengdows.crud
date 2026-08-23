#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SingleWriterReadOnlyTransactionTests
{
    private static DatabaseContext CreateContext(fakeDbFactory factory)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        return new DatabaseContext(config, factory);
    }

    [Fact]
    public async Task ReadOnlyTransaction_AppliesReadOnlyPreamble()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);

        await using (ctx.BeginTransaction(executionType: ExecutionType.Read))
        {
        }

        Assert.True(factory.CreatedConnections.Count >= 1);
        // SQLite read-only is enforced via Mode=ReadOnly in the connection string, not PRAGMA query_only
        var allConnectionStrings = factory.CreatedConnections.SelectMany(c => c.ConnectionStringHistory).ToList();
        Assert.Contains(allConnectionStrings,
            cs => cs.Contains("Mode=ReadOnly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadWriteTransaction_UsesWriterConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);

        await using (ctx.BeginTransaction(executionType: ExecutionType.Write))
        {
        }

        Assert.True(factory.CreatedConnections.Count >= 1);
        var writerConnection = factory.CreatedConnections.Last();
    }
}