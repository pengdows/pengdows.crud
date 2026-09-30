using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// GEN-001 / CORE-016: a session-scoped last-id function (Sybase ASE @@IDENTITY, Informix DBINFO,
/// SAP HANA CURRENT_IDENTITY_VALUE(), Access @@IDENTITY) reports the id only on the connection that
/// ran the INSERT. The gateway ran the id query on a new connection lease, so it read another
/// session's value (or 0). Outside a transaction the INSERT and the id query now share one pinned
/// connection, released exactly once, including when the INSERT fails.
/// </summary>
public sealed class GeneratedKeySameConnectionTests
{
    private static (DatabaseContext Context, fakeDbFactory Factory, fakeDbConnection Exec) CreateInformix()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix };
        exec.ScalarResolver = sql => sql.Contains("DBINFO", StringComparison.Ordinal) ? 42L : null;
        exec.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 42L } });
        factory.Connections.Add(exec);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Database=test;Server=ifx;EmulatedProduct=Informix",
            DbMode = DbMode.Standard
        }, factory);
        return (context, factory, exec);
    }

    private static bool RanIdQuery(fakeDbConnection connection) =>
        connection.ExecutedScalarTexts.Concat(connection.ExecutedReaderTexts)
            .Any(sql => sql.Contains("DBINFO('bigserial')", StringComparison.Ordinal));

    [Fact]
    public async Task CreateAsync_SessionScopedFunction_RunsInsertAndIdQueryOnOneConnection()
    {
        var (context, factory, exec) = CreateInformix();
        await using var _ = context;
        Assert.Equal(GeneratedKeyPlan.SessionScopedFunction, context.Dialect.GetGeneratedKeyPlan());
        var gateway = new TableGateway<Widget, long>(context);

        var widget = new Widget { Name = "w" };
        Assert.True(await gateway.CreateAsync(widget));

        Assert.Equal(42L, widget.Id);
        Assert.Contains(exec.ExecutedNonQueryTexts, sql => sql.Contains("INSERT INTO", StringComparison.Ordinal));
        Assert.True(RanIdQuery(exec), "the id query ran on a different connection than the INSERT");
        Assert.DoesNotContain(factory.CreatedConnections.Where(c => !ReferenceEquals(c, exec)), RanIdQuery);
        Assert.Equal(1, exec.DisposeCount);
    }

    [Fact]
    public async Task CreateAsync_SessionScopedFunction_InsertFails_ReleasesTheConnectionOnce()
    {
        var (context, _, exec) = CreateInformix();
        await using var _ = context;
        exec.SetNonQueryExecuteException(new InvalidOperationException("insert failed"));
        var gateway = new TableGateway<Widget, long>(context);

        await Assert.ThrowsAnyAsync<Exception>(async () => await gateway.CreateAsync(new Widget { Name = "w" }));

        Assert.False(RanIdQuery(exec));
        Assert.Equal(1, exec.DisposeCount);
    }

    [Fact]
    public async Task CreateAsync_SessionScopedFunction_InsideATransaction_UsesTheTransactionConnection()
    {
        var (context, factory, exec) = CreateInformix();
        await using var _ = context;
        var gateway = new TableGateway<Widget, long>(context);

        await using (var tx = context.BeginTransaction())
        {
            var widget = new Widget { Name = "w" };
            Assert.True(await gateway.CreateAsync(widget, tx));
            Assert.Equal(42L, widget.Id);
            Assert.True(RanIdQuery(exec));
            Assert.Equal(0, exec.DisposeCount); // still pinned by the transaction
            tx.Commit();
        }

        Assert.DoesNotContain(factory.CreatedConnections.Where(c => !ReferenceEquals(c, exec)), RanIdQuery);
        Assert.Equal(1, exec.DisposeCount);
    }

    [Fact]
    public async Task CreateAsync_SessionScopedFunction_SingleConnectionMode_UsesTheSharedConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        var shared = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix };
        shared.ScalarResolver = sql => sql.Contains("DBINFO", StringComparison.Ordinal) ? 42L : null;
        shared.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 42L } });
        factory.Connections.Add(shared);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Database=test;Server=ifx;EmulatedProduct=Informix",
            DbMode = DbMode.SingleConnection
        }, factory);
        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        var gateway = new TableGateway<Widget, long>(context);

        var widget = new Widget { Name = "w" };
        Assert.True(await gateway.CreateAsync(widget));

        Assert.Equal(42L, widget.Id);
        Assert.True(RanIdQuery(shared));
        Assert.Equal(0, shared.DisposeCount); // the shared connection stays open
    }

    [Theory]
    [InlineData(SupportedDatabase.Informix, "DBINFO")]
    [InlineData(SupportedDatabase.SapHana, "CURRENT_IDENTITY_VALUE()")]
    [InlineData(SupportedDatabase.Access, "@@IDENTITY")]
    public void SameConnectionIdDialects_UseSessionScopedFunction(SupportedDatabase database, string idQueryFragment)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

        Assert.Equal(GeneratedKeyPlan.SessionScopedFunction, dialect.GetGeneratedKeyPlan());
        Assert.Contains(idQueryFragment, dialect.GetLastInsertedIdQuery(), StringComparison.Ordinal);
    }

    [Table("widgets")]
    private sealed class Widget
    {
        [Id(false)] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}
