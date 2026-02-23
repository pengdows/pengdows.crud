using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Targeted tests for PopulateGeneratedIdAsync paths and read-only SQL dialect behaviour.
/// </summary>
public class CoverageGapTests_IdGenerationAndDialect
{
    // =========================================================================
    // Entities
    // =========================================================================

    [Table("gen_int")]
    public class IntIdEntity
    {
        [Id(false)] // database generates (triggers PopulateGeneratedIdAsync)
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    [Table("gen_guid")]
    public class GuidIdEntity
    {
        [Id(false)] // database generates (triggers PopulateGeneratedIdAsync)
        [Column("id", DbType.Guid)]
        public Guid Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    // =========================================================================
    // PopulateGeneratedIdAsync — integer ID via last-insert-rowid fallback
    // =========================================================================

    /// <summary>
    /// When INSERT RETURNING returns null (empty reader), CreateAsync falls back to
    /// PopulateGeneratedIdAsync which runs the last-insert-id query.
    /// </summary>
    [Fact]
    public async Task CreateAsync_FallsBackToPopulateGeneratedId_WhenReturningReturnsNull()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();

        // First reader result: empty → RETURNING clause returns null → fallback triggered
        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        // Second reader result: for the last_insert_rowid() query → returns 42
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", 42L } }
        });

        factory.Connections.Add(conn);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection
        };
        await using var ctx = new DatabaseContext(config, factory);

        // Re-prime with the two reader results needed by the actual operation
        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", 42L } }
        });

        var gw = new TableGateway<IntIdEntity, int>(ctx);
        var entity = new IntIdEntity { Name = "fallback-test" };

        var created = await gw.CreateAsync(entity);

        Assert.True(created);
        Assert.Equal(42, entity.Id);
    }

    /// <summary>
    /// CreateAsync with no ID returned and no fallback query result just skips setting ID.
    /// </summary>
    [Fact]
    public async Task CreateAsync_FallsBackToPopulateGeneratedId_WithNoLastInsertResult()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();

        // First: RETURNING returns empty
        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        // Second: last_insert_rowid() also returns empty → generatedId is null → ID stays 0
        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());

        factory.Connections.Add(conn);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection
        };
        await using var ctx = new DatabaseContext(config, factory);

        // Re-prime
        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());

        var gw = new TableGateway<IntIdEntity, int>(ctx);
        var entity = new IntIdEntity { Name = "no-id" };

        // Should complete without exception even if no ID could be populated
        var created = await gw.CreateAsync(entity);
        Assert.True(created);
    }

    /// <summary>
    /// PopulateGeneratedIdAsync with a Guid ID column where last-insert returns a parseable Guid string.
    /// </summary>
    [Fact]
    public async Task CreateAsync_GuidEntity_PopulatesGuidFromString()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();
        var expectedGuid = Guid.NewGuid();

        // RETURNING: empty (SQLite returns a long for last_insert_rowid, not a Guid,
        // so we force the fallback by returning empty)
        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        // last-insert-id query returns the Guid as a string
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", expectedGuid.ToString() } }
        });

        factory.Connections.Add(conn);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection
        };
        await using var ctx = new DatabaseContext(config, factory);

        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", expectedGuid.ToString() } }
        });

        var gw = new TableGateway<GuidIdEntity, Guid>(ctx);
        var entity = new GuidIdEntity { Name = "guid-test" };

        var created = await gw.CreateAsync(entity);

        Assert.True(created);
        Assert.Equal(expectedGuid, entity.Id);
    }

    /// <summary>
    /// PopulateGeneratedIdAsync with a Guid ID column where last-insert returns an actual Guid object.
    /// </summary>
    [Fact]
    public async Task CreateAsync_GuidEntity_PopulatesGuidDirectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();
        var expectedGuid = Guid.NewGuid();

        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", (object?)expectedGuid } }
        });

        factory.Connections.Add(conn);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection
        };
        await using var ctx = new DatabaseContext(config, factory);

        conn.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", (object?)expectedGuid } }
        });

        var gw = new TableGateway<GuidIdEntity, Guid>(ctx);
        var entity = new GuidIdEntity { Name = "guid-direct" };

        var created = await gw.CreateAsync(entity);

        Assert.True(created);
        Assert.Equal(expectedGuid, entity.Id);
    }

    /// <summary>
    /// PopulateGeneratedIdAsync throws InvalidOperationException when the returned
    /// value cannot be converted to Guid.
    /// Uses MySQL (no RETURNING support) so CreateAsync always calls PopulateGeneratedIdAsync
    /// directly without the RETURNING clause path.
    /// </summary>
    [Fact]
    public async Task CreateAsync_GuidEntity_ThrowsWhenLastInsertValueIsNotGuid()
    {
        // MySQL has no INSERT RETURNING support → always falls through to PopulateGeneratedIdAsync
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var conn = new fakeDbConnection();
        factory.Connections.Add(conn);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=localhost;Database=test",
            DbMode = DbMode.SingleConnection
        };
        await using var ctx = new DatabaseContext(config, factory);

        // Enqueue AFTER init: ExecuteNonQueryAsync (the INSERT) doesn't consume readers,
        // so PopulateGeneratedIdAsync will consume this value → Guid.TryParse fails → throws.
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", "not-a-guid" } }
        });

        var gw = new TableGateway<GuidIdEntity, Guid>(ctx);
        var entity = new GuidIdEntity { Name = "bad-guid" };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await gw.CreateAsync(entity));
    }

    // =========================================================================
    // TryExecuteReadOnlySqlAsync — via read-only transaction sessions
    // =========================================================================

    [Fact]
    public async Task ReadOnlyTransaction_ExecutesSessionSettings_OnSqlite()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:"
        };
        await using var ctx = new DatabaseContext(config, factory);

        // BeginTransaction with readOnly=true triggers TryExecuteReadOnlySqlAsync
        await using var tx = ctx.BeginTransaction(readOnly: true);
        Assert.NotNull(tx);
        tx.Commit();
    }

    [Fact]
    public async Task ReadOnlyTransaction_ExecutesSessionSettings_OnPostgreSQL()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test"
        };
        await using var ctx = new DatabaseContext(config, factory);

        // PostgreSQL read-only transaction triggers TryExecuteReadOnlySqlAsync
        await using var tx = ctx.BeginTransaction(readOnly: true);
        Assert.NotNull(tx);
        tx.Commit();
    }

    [Fact]
    public async Task ReadOnlyTransaction_Async_Works()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:"
        };
        await using var ctx = new DatabaseContext(config, factory);

        await using var tx = await ctx.BeginTransactionAsync(readOnly: true);
        Assert.NotNull(tx);
        tx.Commit();
    }

    // =========================================================================
    // TrackedConnection dispose async core path
    // =========================================================================

    [Fact]
    public async Task DatabaseContext_GetAndCloseConnection_CoversDisposeAsyncCore()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:"
        };
        await using var ctx = new DatabaseContext(config, factory);

        // GetConnection then close triggers DisposeConnectionAsyncCore
        var conn = ctx.GetConnection(ExecutionType.Write, isShared: false);
        await ctx.CloseAndDisposeConnectionAsync(conn);
    }

    [Fact]
    public async Task DatabaseContext_CloseNullConnection_IsNoOp()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:"
        };
        await using var ctx = new DatabaseContext(config, factory);

        // Should not throw
        await ctx.CloseAndDisposeConnectionAsync(null);
        ctx.CloseAndDisposeConnection(null);
    }
}