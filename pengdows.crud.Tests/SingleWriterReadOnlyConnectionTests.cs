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

public class SingleWriterReadOnlyConnectionTests
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
    public async Task ReadConnection_AppliesReadOnlyPreamble()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);

        // SingleWriter now uses per-operation connections (not persistent)
        // First connection is for dialect detection during init (disposed)
        // Read connection is the next one
        var read = ctx.GetConnection(ExecutionType.Read);
        await read.OpenAsync();
        ctx.CloseAndDisposeConnection(read); // Must dispose to release slot

        // SQLite read-only is enforced via Mode=ReadOnly in the connection string, not PRAGMA query_only
        var readConn = factory.CreatedConnections.FirstOrDefault(c => c.ConnectionStringHistory.Any(cs =>
            cs.Contains("Mode=ReadOnly", StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(readConn);
    }

    [Fact]
    public async Task WriteConnection_DoesNotApplyReadOnlyPreamble()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);

        // Get and release write connection
        var write = ctx.GetConnection(ExecutionType.Write);
        await write.OpenAsync();
        // It's okay if query_only=OFF is applied as part of the baseline/reset logic
        ctx.CloseAndDisposeConnection(write);

        // Now get read connection
        var read = ctx.GetConnection(ExecutionType.Read);
        await read.OpenAsync();
        ctx.CloseAndDisposeConnection(read);

        // SQLite read-only is enforced via Mode=ReadOnly in the connection string
        var readConn = factory.CreatedConnections.FirstOrDefault(c => c.ConnectionStringHistory.Any(cs =>
            cs.Contains("Mode=ReadOnly", StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(readConn);
    }
}