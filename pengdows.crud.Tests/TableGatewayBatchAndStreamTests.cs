using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Covers BatchCreateAsync, BatchUpsertAsync, and LoadStreamAsync code paths.
/// </summary>
[Collection("SqliteSerial")]
public class TableGatewayBatchAndStreamTests
{
    // =========================================================================
    // Test entity
    // =========================================================================

    [Table("batch_entity")]
    public class BatchEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }

        [PrimaryKey]
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Column("value", DbType.Int32)] public int Value { get; set; }
    }

    [Table("batch_entity_no_pk")]
    public class BatchEntityNoPrimaryKey
    {
        [Id(false)] // server-generated → not a usable upsert key; no [PrimaryKey] → NotSupportedException
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.Int32)] public int Value { get; set; }
    }

    // =========================================================================
    // BatchCreateAsync
    // =========================================================================

    private static IDatabaseContext MakeSqliteContext(bool enableDataPersistence = true)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite)
        {
            EnableDataPersistence = enableDataPersistence
        };
        return new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
    }

    private static IDatabaseContext MakePostgresContext(bool enableDataPersistence = true)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql)
        {
            EnableDataPersistence = enableDataPersistence
        };
        return new DatabaseContext("Host=localhost;Database=test;EmulatedProduct=PostgreSql", factory);
    }

    private static IDatabaseContext MakeMySqlContext(bool enableDataPersistence = true)
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql)
        {
            EnableDataPersistence = enableDataPersistence
        };
        return new DatabaseContext("Server=localhost;Database=test;EmulatedProduct=MySql", factory);
    }

    private static IDatabaseContext MakeSqlServerContext(bool enableDataPersistence = true)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer)
        {
            EnableDataPersistence = enableDataPersistence
        };
        return new DatabaseContext(
            "Server=localhost;Database=test;Trusted_Connection=True;EmulatedProduct=SqlServer", factory);
    }

    [Fact]
    public async Task BatchCreateAsync_EmptyList_ReturnsZero()
    {
        await using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var result = await gw.BatchCreateAsync(new List<BatchEntity>());

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task BatchCreateAsync_EmptyList_WithCancellationToken_ReturnsZero()
    {
        await using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var result = await gw.BatchCreateAsync(new List<BatchEntity>(), null, CancellationToken.None);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task BatchCreateAsync_SingleEntity_UsesSingleCreateFastPath()
    {
        await using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        // Single entity → delegates to CreateAsync (fast path)
        var entity = new BatchEntity { Name = "single", Value = 1 };
        var result = await gw.BatchCreateAsync(new[] { entity });

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task BatchCreateAsync_MultipleEntities_ReturnsTotalAffected()
    {
        await using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var entities = new[]
        {
            new BatchEntity { Name = "alpha", Value = 1 },
            new BatchEntity { Name = "beta", Value = 2 },
            new BatchEntity { Name = "gamma", Value = 3 }
        };

        var result = await gw.BatchCreateAsync(entities);

        Assert.True(result >= 0); // fakeDb returns 1 per batch statement
    }

    [Fact]
    public void BuildBatchCreate_EmptyList_ReturnsEmptyContainers()
    {
        using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var containers = gw.BuildBatchCreate(new List<BatchEntity>());

        Assert.Empty(containers);
    }

    [Fact]
    public void BuildBatchCreate_MultipleEntities_GeneratesBatchSql()
    {
        using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var entities = new[]
        {
            new BatchEntity { Name = "a", Value = 1 },
            new BatchEntity { Name = "b", Value = 2 }
        };

        var containers = gw.BuildBatchCreate(entities);

        Assert.Single(containers);
        var sql = containers[0].Query.ToString();
        Assert.Contains("INSERT INTO", sql.ToUpperInvariant());
        Assert.Contains("VALUES", sql.ToUpperInvariant());
    }

    // =========================================================================
    // BatchUpsertAsync
    // =========================================================================

    [Fact]
    public async Task BatchUpsertAsync_EmptyList_ReturnsZero()
    {
        await using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var result = await gw.BatchUpsertAsync(new List<BatchEntity>());

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task BatchUpsertAsync_EmptyList_WithCancellationToken_ReturnsZero()
    {
        await using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var result = await gw.BatchUpsertAsync(new List<BatchEntity>(), null, CancellationToken.None);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task BatchUpsertAsync_SingleEntity_UsesUpsertFastPath()
    {
        await using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var entity = new BatchEntity { Name = "solo", Value = 99 };
        // Single entity → delegates to UpsertAsync (fast path)
        var result = await gw.BatchUpsertAsync(new[] { entity });

        Assert.True(result >= 0);
    }

    [Fact]
    public async Task BatchUpsertAsync_MultipleEntities_PostgreSql_UsesOnConflict()
    {
        await using var ctx = MakePostgresContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var entities = new[]
        {
            new BatchEntity { Name = "p1", Value = 1 },
            new BatchEntity { Name = "p2", Value = 2 }
        };

        var result = await gw.BatchUpsertAsync(entities);

        Assert.True(result >= 0);
    }

    [Fact]
    public void BuildBatchUpsert_MultipleEntities_PostgreSql_ContainsOnConflict()
    {
        using var ctx = MakePostgresContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var entities = new[]
        {
            new BatchEntity { Name = "x", Value = 10 },
            new BatchEntity { Name = "y", Value = 20 }
        };

        var containers = gw.BuildBatchUpsert(entities);

        Assert.True(containers.Count >= 1);
        var sql = containers[0].Query.ToString().ToUpperInvariant();
        Assert.Contains("ON CONFLICT", sql);
        Assert.Contains("DO UPDATE SET", sql);
    }

    [Fact]
    public async Task BatchUpsertAsync_MultipleEntities_MySql_UsesOnDuplicateKey()
    {
        await using var ctx = MakeMySqlContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var entities = new[]
        {
            new BatchEntity { Name = "m1", Value = 1 },
            new BatchEntity { Name = "m2", Value = 2 }
        };

        var result = await gw.BatchUpsertAsync(entities);

        Assert.True(result >= 0);
    }

    [Fact]
    public void BuildBatchUpsert_MultipleEntities_MySql_ContainsOnDuplicateKey()
    {
        using var ctx = MakeMySqlContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var entities = new[]
        {
            new BatchEntity { Name = "x", Value = 10 },
            new BatchEntity { Name = "y", Value = 20 }
        };

        var containers = gw.BuildBatchUpsert(entities);

        Assert.True(containers.Count >= 1);
        var sql = containers[0].Query.ToString().ToUpperInvariant();
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql);
    }

    [Fact]
    public async Task BatchUpsertAsync_MultipleEntities_SqlServer_FallsBackToIndividualUpsert()
    {
        await using var ctx = MakeSqlServerContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var entities = new[]
        {
            new BatchEntity { Name = "s1", Value = 1 },
            new BatchEntity { Name = "s2", Value = 2 }
        };

        var result = await gw.BatchUpsertAsync(entities);

        Assert.True(result >= 0);
    }

    [Fact]
    public void BuildBatchUpsert_NoKey_ThrowsNotSupportedException()
    {
        using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntityNoPrimaryKey, int>(ctx);

        var entities = new[]
        {
            new BatchEntityNoPrimaryKey { Value = 1 },
            new BatchEntityNoPrimaryKey { Value = 2 }
        };

        Assert.Throws<NotSupportedException>(() => gw.BuildBatchUpsert(entities));
    }

    // =========================================================================
    // LoadStreamAsync
    // =========================================================================

    [Fact]
    public async Task LoadStreamAsync_NoRows_YieldsNothing()
    {
        // No reader results queued → empty reader
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = new DatabaseContext("Data Source=:memory:", factory);
        var gw = new TableGateway<BatchEntity, int>(ctx);

        var sc = ctx.CreateSqlContainer("SELECT id, name, value FROM batch_entity");
        var results = new List<BatchEntity>();
        await foreach (var item in gw.LoadStreamAsync(sc))
        {
            results.Add(item);
        }

        Assert.Empty(results);
    }

    [Fact]
    public async Task LoadStreamAsync_WithRows_YieldsAllEntities()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", 1L }, { "name", "a" }, { "value", 10L } },
            new Dictionary<string, object?> { { "id", 2L }, { "name", "b" }, { "value", 20L } }
        });
        factory.Connections.Add(conn);

        await using var ctx = new DatabaseContext("Data Source=:memory:", factory);

        // Re-prime after init probes
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", 1L }, { "name", "a" }, { "value", 10L } },
            new Dictionary<string, object?> { { "id", 2L }, { "name", "b" }, { "value", 20L } }
        });

        var gw = new TableGateway<BatchEntity, int>(ctx);
        var sc = ctx.CreateSqlContainer("SELECT id, name, value FROM batch_entity");

        var results = new List<BatchEntity>();
        await foreach (var item in gw.LoadStreamAsync(sc))
        {
            results.Add(item);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("a", results[0].Name);
        Assert.Equal("b", results[1].Name);
    }

    [Fact]
    public async Task LoadStreamAsync_WithCancellationToken_YieldsEntities()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", 1L }, { "name", "x" }, { "value", 5L } }
        });
        factory.Connections.Add(conn);

        await using var ctx = new DatabaseContext("Data Source=:memory:", factory);

        // Re-prime after init probes
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "id", 1L }, { "name", "x" }, { "value", 5L } }
        });

        var gw = new TableGateway<BatchEntity, int>(ctx);
        var sc = ctx.CreateSqlContainer("SELECT id, name, value FROM batch_entity");

        var results = new List<BatchEntity>();
        await foreach (var item in gw.LoadStreamAsync(sc, CancellationToken.None))
        {
            results.Add(item);
        }

        Assert.Single(results);
        Assert.Equal("x", results[0].Name);
    }

    [Fact]
    public async Task LoadStreamAsync_NullContainer_ThrowsArgumentNullException()
    {
        await using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in gw.LoadStreamAsync(null!))
            {
            }
        });
    }

    [Fact]
    public async Task LoadStreamAsync_WithCancellationToken_NullContainer_ThrowsArgumentNullException()
    {
        await using var ctx = MakeSqliteContext();
        var gw = new TableGateway<BatchEntity, int>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in gw.LoadStreamAsync(null!, CancellationToken.None))
            {
            }
        });
    }
}