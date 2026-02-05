using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayCriticalPathTests
{
    [Table("TestEntity")]
    private class TestEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String, 255)] public string? Name { get; set; }

        [Version]
        [Column("row_version", DbType.Binary)]
        public byte[]? RowVersion { get; set; }
    }

    [Table("EntityWithoutId")]
    private class EntityWithoutId
    {
        [Column("name", DbType.String, 255)] public string? Name { get; set; }
    }

    private static DatabaseContext CreateContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite)
        {
            EnableDataPersistence = true
        };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.Standard
        };
        var typeMap = new TypeMapRegistry();

        return new DatabaseContext(config, factory, NullLoggerFactory.Instance, typeMap);
    }

    [Fact]
    public void Constructor_WithoutIdColumn_ThrowsInvalidOperation()
    {
        using var context = CreateContext();

        Assert.Throws<InvalidOperationException>(() =>
            new TableGateway<EntityWithoutId, int>(context));
    }

    [Fact]
    public void Constructor_WithUnsupportedRowIdType_ThrowsNotSupported()
    {
        using var context = CreateContext();

        var ex = Assert.Throws<TypeInitializationException>(() =>
            new TableGateway<TestEntity, DateTime>(context));

        Assert.IsType<NotSupportedException>(ex.InnerException);
        Assert.Contains("TRowID type 'System.DateTime' is not supported", ex.InnerException!.Message);
    }

    [Fact]
    public async Task CreateAsync_WhenCommandCreationFails_PropagatesException()
    {
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.Sqlite,
            ConnectionFailureMode.FailOnCommand);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=create-failure.db",
            DbMode = DbMode.Standard
        };

        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance, typeMap);
        var helper = new TableGateway<TestEntity, int>(context);
        var entity = new TestEntity
        {
            Name = "widget",
            RowVersion = new byte[] { 1, 2, 3, 4 } // Provide proper byte array for version column
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await helper.CreateAsync(entity, context));
    }

    [Fact]
    public async Task BuildUpdateAsync_IncludesBinaryVersionInWhere()
    {
        using var context = CreateContext();
        var helper = new TableGateway<TestEntity, int>(context);
        var entity = new TestEntity
        {
            Id = 1,
            Name = "updated",
            RowVersion = new byte[] { 1, 2, 3, 4 }
        };

        // Use the overload that doesn't load the original record
        var container = await helper.BuildUpdateAsync(entity, false);

        var sql = container.Query.ToString();

        // For byte[] version columns, the column should appear in WHERE clause for optimistic concurrency
        Assert.Contains("row_version", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql);

        // But it should NOT appear in the SET clause (database manages it automatically)
        var setPart = sql.Split("WHERE")[0];
        Assert.DoesNotContain("row_version", setPart, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildUpsert_ForSqliteFallsBackToInsert()
    {
        using var context = CreateContext();
        var helper = new TableGateway<TestEntity, int>(context);
        var entity = new TestEntity { Id = 1, Name = "widget" };

        var container = helper.BuildUpsert(entity);
        var sql = container.Query.ToString().ToUpperInvariant();

        Assert.Contains("INSERT", sql);
        Assert.DoesNotContain("MERGE", sql);
    }

    [Fact]
    public async Task RetrieveAsync_WithEmptyIds_ThrowsArgumentException()
    {
        using var context = CreateContext();
        var helper = new TableGateway<TestEntity, int>(context);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await helper.RetrieveAsync(Array.Empty<int>()));
    }

    [Fact]
    public async Task RetrieveAsync_WithNullIds_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var helper = new TableGateway<TestEntity, int>(context);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await helper.RetrieveAsync((IEnumerable<int>)null!));
    }
}