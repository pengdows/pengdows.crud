using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ReadOnlySingleWriterConnectionTests
{
    private static DatabaseContext CreateReadOnlySingleWriterContext(fakeDbFactory factory)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        return new DatabaseContext(config, factory);
    }

    [Fact]
    public async Task ReadOnlySingleWriter_ReadConnection_ShouldHaveReadOnlySettings()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateReadOnlySingleWriterContext(factory);

        // ReadOnly context: all connections are read connections
        var conn = ctx.GetConnection(ExecutionType.Read);
        await conn.OpenAsync();
        ctx.CloseAndDisposeConnection(conn);

        Assert.True(factory.CreatedConnections.Count >= 1);
        var operationalConnection = factory.CreatedConnections.Last();

        // SQLite read-only is enforced via Mode=ReadOnly in the connection string
        Assert.Contains(operationalConnection.ConnectionStringHistory,
            cs => cs.Contains("Mode=ReadOnly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadOnlySingleWriter_ReadConnections_ShouldBeReadOnly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateReadOnlySingleWriterContext(factory);

        // ReadOnly context: only read connections are permitted
        var readConn1 = ctx.GetConnection(ExecutionType.Read);
        await readConn1.OpenAsync();
        ctx.CloseAndDisposeConnection(readConn1);

        var readConn2 = ctx.GetConnection(ExecutionType.Read);
        await readConn2.OpenAsync();
        ctx.CloseAndDisposeConnection(readConn2);

        // Operational connections (those that received session settings) should be read-only.
        // Init connections used for dialect detection may have empty command lists because
        // session settings detection was not yet complete when they opened.
        var operationalConnections = factory.CreatedConnections
            .Where(c => c.ExecutedNonQueryTexts.Count > 0)
            .ToList();
        Assert.NotEmpty(operationalConnections);
        foreach (var recorded in operationalConnections)
        {
            Assert.Contains(recorded.ConnectionStringHistory,
                cs => cs.Contains("Mode=ReadOnly", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task ReadOnlySingleWriter_WriteTransaction_ShouldThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateReadOnlySingleWriterContext(factory);

        // Attempting to create a write transaction on a read-only context should throw
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);
            await tx.DisposeAsync();
        });
    }

    [Fact]
    public void ReadOnlySingleWriter_GetWriteConnection_ThrowsPoolForbiddenException()
    {
        // ReadOnly context: write pool is forbidden — GetConnection(Write) throws immediately
        // rather than returning a connection that later fails at SQL execution time.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = CreateReadOnlySingleWriterContext(factory);

        Assert.Throws<PoolForbiddenException>(() => ctx.GetConnection(ExecutionType.Write));
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void ReadOnlySingleConnection_IsNotSupported(SupportedDatabase database)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={database}",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        Assert.Throws<InvalidOperationException>(() => new DatabaseContext(config, new fakeDbFactory(database)));
    }

    [Theory]
    [InlineData(DbMode.SingleWriter)]
    [InlineData(DbMode.SingleConnection)]
    public async Task ReadOnlySingleMode_AssertIsWriteConnection_ShouldFail(DbMode mode)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = mode == DbMode.SingleWriter
                ? "Data Source=file.db;EmulatedProduct=Sqlite"
                : "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = mode,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        if (mode == DbMode.SingleConnection)
        {
            Assert.Throws<InvalidOperationException>(() => new DatabaseContext(config, factory));
            return;
        }

        await using var ctx = new DatabaseContext(config, factory);

        // Should fail because the context is read-only
        Assert.Throws<InvalidOperationException>(() => ctx.AssertIsWriteConnection());
    }
}