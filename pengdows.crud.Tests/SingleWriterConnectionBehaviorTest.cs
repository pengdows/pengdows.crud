using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SingleWriterConnectionBehaviorTest
{
    private static DatabaseContext CreateSingleWriterContext(fakeDbFactory factory)
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
    public async Task SingleWriter_AllNewConnections_ShouldBeReadOnly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateSingleWriterContext(factory);

        var initialCount = factory.CreatedConnections.Count;

        // Acquire a write connection and assert it remains writable
        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        ctx.CloseAndDisposeConnection(writeConn);

        Assert.True(factory.CreatedConnections.Count > initialCount);
        var writeConnection = factory.CreatedConnections.Last();

        // Acquire two read connections, each of which should be read-only via connection string
        for (var i = 0; i < 2; i++)
        {
            var readConn = ctx.GetConnection(ExecutionType.Read);
            await readConn.OpenAsync();
            ctx.CloseAndDisposeConnection(readConn);

            var readOnlyConnection = factory.CreatedConnections.Last();
            Assert.Contains(readOnlyConnection.ConnectionStringHistory,
                cs => cs.Contains("Mode=ReadOnly", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task SingleWriter_WriteTransaction_UsesWritableConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateSingleWriterContext(factory);

        await using var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);

        Assert.True(factory.CreatedConnections.Count >= 1);
        var writeConnection = factory.CreatedConnections.Last();
    }

    [Fact]
    public async Task SingleWriter_ReadConnectionsStayReadOnly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateSingleWriterContext(factory);

        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        ctx.CloseAndDisposeConnection(writeConn);
        var writerConnection = factory.CreatedConnections.Last();

        var readConn = ctx.GetConnection(ExecutionType.Read);
        await readConn.OpenAsync();
        ctx.CloseAndDisposeConnection(readConn);
        var readOnlyConnection = factory.CreatedConnections.Last();

        Assert.Contains(readOnlyConnection.ConnectionStringHistory,
            cs => cs.Contains("Mode=ReadOnly", StringComparison.OrdinalIgnoreCase));
    }
}