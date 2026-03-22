#region

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class UpsertAsyncTests : RealSqliteContextTestBase, IAsyncLifetime
{
    private readonly TableGateway<TestEntity, int> helper;

    public UpsertAsyncTests()
    {
        TypeMap.Register<TestEntity>();
        helper = new TableGateway<TestEntity, int>(Context, AuditValueResolver);
    }

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await BuildTestTable();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
    }

    [Fact]
    public async Task UpsertAsync_Inserts_WhenIdDefault()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        var affected = await helper.UpsertAsync(e);
        Assert.Equal(1, affected);
        var loaded = await helper.RetrieveOneAsync(e);
        Assert.NotNull(loaded);
        Assert.Equal(e.Name, loaded!.Name);
    }

    [Fact]
    public async Task UpsertAsync_Updates_WhenIdSet()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e, Context);
        var loaded = await helper.RetrieveOneAsync(e);
        Assert.NotNull(loaded);
        var originalUpdated = loaded!.LastUpdatedOn;

        var affected = await helper.UpsertAsync(loaded);
        Assert.Equal(1, affected);
        var reloaded = await helper.RetrieveOneAsync(loaded.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.LastUpdatedOn >= originalUpdated);
    }

    [Fact]
    public async Task UpsertAsync_SqlServer_StaleVersion_ThrowsConcurrencyConflictException()
    {
        // MERGE with version condition: when WHEN MATCHED AND version condition is false,
        // 0 rows are affected. UpsertAsync must throw ConcurrencyConflictException.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<VersionedUpsertEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var connection = new fakeDbConnection();
        connection.EnqueueNonQueryResult(0); // version mismatch → 0 rows from MERGE
        factory.Connections.Add(connection);
        await using var context = new DatabaseContext(
            new DatabaseContextConfiguration { ConnectionString = "Data Source=test;EmulatedProduct=SqlServer", DbMode = DbMode.SingleConnection },
            factory, NullLoggerFactory.Instance, typeMap);
        var gateway = new TableGateway<VersionedUpsertEntity, int>(context);
        var staleEntity = new VersionedUpsertEntity { Id = 1, Name = "new", Version = 3 };

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            await gateway.UpsertAsync(staleEntity, context));
    }

    [Fact]
    public async Task UpsertAsync_PostgreSql_StaleVersion_ThrowsConcurrencyConflictException()
    {
        // ON CONFLICT DO UPDATE WHERE version = EXCLUDED.version: mismatch → DO NOTHING → 0 rows.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<VersionedUpsertEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection();
        connection.EnqueueNonQueryResult(0); // version mismatch → 0 rows from ON CONFLICT
        factory.Connections.Add(connection);
        await using var context = new DatabaseContext(
            new DatabaseContextConfiguration { ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql", DbMode = DbMode.SingleConnection },
            factory, NullLoggerFactory.Instance, typeMap);
        var gateway = new TableGateway<VersionedUpsertEntity, int>(context);
        var staleEntity = new VersionedUpsertEntity { Id = 1, Name = "new", Version = 3 };

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            await gateway.UpsertAsync(staleEntity, context));
    }

    [Fact]
    public async Task UpsertAsync_MySql_ZeroRows_DoesNotThrowConcurrencyConflictException()
    {
        // MySQL ON DUPLICATE KEY UPDATE does not support WHERE — version conflict detection unavailable.
        // 0 rows returned must NOT throw ConcurrencyConflictException.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<VersionedUpsertEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var connection = new fakeDbConnection();
        connection.EnqueueNonQueryResult(0);
        factory.Connections.Add(connection);
        await using var context = new DatabaseContext(
            new DatabaseContextConfiguration { ConnectionString = "Data Source=test;EmulatedProduct=MySql", DbMode = DbMode.SingleConnection },
            factory, NullLoggerFactory.Instance, typeMap);
        var gateway = new TableGateway<VersionedUpsertEntity, int>(context);
        var entity = new VersionedUpsertEntity { Id = 1, Name = "v", Version = 1 };

        var affected = await gateway.UpsertAsync(entity, context);
        Assert.Equal(0, affected);
    }

    [Fact]
    public async Task UpsertAsync_Sqlite_ZeroRows_DoesNotThrowConcurrencyConflictException()
    {
        // SQLite SupportsOnConflictWhere = false — no version WHERE clause, no conflict detection.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<VersionedUpsertEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = new fakeDbConnection();
        connection.EnqueueNonQueryResult(0);
        factory.Connections.Add(connection);
        await using var context = new DatabaseContext(
            new DatabaseContextConfiguration { ConnectionString = "Data Source=test;EmulatedProduct=Sqlite", DbMode = DbMode.SingleConnection },
            factory, NullLoggerFactory.Instance, typeMap);
        var gateway = new TableGateway<VersionedUpsertEntity, int>(context);
        var entity = new VersionedUpsertEntity { Id = 1, Name = "v", Version = 1 };

        var affected = await gateway.UpsertAsync(entity, context);
        Assert.Equal(0, affected);
    }

    [Table("versioned_upsert")]
    private sealed class VersionedUpsertEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }

    private async Task BuildTestTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}Test{1} ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
{0}Name{1} TEXT UNIQUE NOT NULL,
    {0}CreatedBy{1} TEXT NOT NULL DEFAULT 'system',
    {0}CreatedOn{1} TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    {0}LastUpdatedBy{1} TEXT NOT NULL DEFAULT 'system',
    {0}LastUpdatedOn{1} TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
{0}Version{1} INTEGER NOT NULL DEFAULT 0)", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }
}