using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class AuditFieldNumericUserIdTests : SqlLiteContextTestBase
{
    #region Test Entities

    [Table("IntUserAudit")]
    private class IntUserAuditEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedBy]
        [Column("CreatedBy", DbType.Int32)]
        public int CreatedBy { get; set; }

        [LastUpdatedBy]
        [Column("LastUpdatedBy", DbType.Int32)]
        public int LastUpdatedBy { get; set; }

        [CreatedOn]
        [Column("CreatedOn", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    [Table("LongUserAudit")]
    private class LongUserAuditEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedBy]
        [Column("CreatedBy", DbType.Int64)]
        public long CreatedBy { get; set; }

        [LastUpdatedBy]
        [Column("LastUpdatedBy", DbType.Int64)]
        public long LastUpdatedBy { get; set; }

        [CreatedOn]
        [Column("CreatedOn", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    #endregion

    [Fact]
    public void Register_IntUserAuditEntity_DoesNotThrow()
    {
        // Validate that registration accepts int CreatedBy/LastUpdatedBy
        TypeMap.Register<IntUserAuditEntity>();
    }

    [Fact]
    public void Register_LongUserAuditEntity_DoesNotThrow()
    {
        // Validate that registration accepts long CreatedBy/LastUpdatedBy
        TypeMap.Register<LongUserAuditEntity>();
    }

    [Fact]
    public async Task IntUserAudit_Create_SetsNumericUserId()
    {
        // Arrange
        TypeMap.Register<IntUserAuditEntity>();
        var resolver = new StubAuditValueResolver(42);
        var helper = new TableGateway<IntUserAuditEntity, int>(Context, resolver);

        await CreateIntUserAuditTable();

        // Act
        var entity = new IntUserAuditEntity { Name = Guid.NewGuid().ToString() };
        var success = await helper.CreateAsync(entity, Context);

        // Assert
        Assert.True(success);
        Assert.Equal(42, entity.CreatedBy);
        Assert.Equal(42, entity.LastUpdatedBy);
    }

    [Fact]
    public async Task LongUserAudit_Create_SetsNumericUserId()
    {
        // Arrange
        TypeMap.Register<LongUserAuditEntity>();
        var resolver = new StubAuditValueResolver(99L);
        var helper = new TableGateway<LongUserAuditEntity, int>(Context, resolver);

        await CreateLongUserAuditTable();

        // Act
        var entity = new LongUserAuditEntity { Name = Guid.NewGuid().ToString() };
        var success = await helper.CreateAsync(entity, Context);

        // Assert
        Assert.True(success);
        Assert.Equal(99L, entity.CreatedBy);
        Assert.Equal(99L, entity.LastUpdatedBy);
    }

    [Fact]
    public async Task IntUserAudit_Create_PreservesExistingNonZeroCreatedBy()
    {
        // Arrange
        TypeMap.Register<IntUserAuditEntity>();
        var resolver = new StubAuditValueResolver(42);
        var helper = new TableGateway<IntUserAuditEntity, int>(Context, resolver);

        await CreateIntUserAuditTable();

        // Act — CreatedBy already set to non-zero, should be preserved
        var entity = new IntUserAuditEntity
        {
            Name = Guid.NewGuid().ToString(),
            CreatedBy = 7
        };
        await helper.CreateAsync(entity, Context);

        // Assert — CreatedBy preserved, LastUpdatedBy set by resolver
        Assert.Equal(7, entity.CreatedBy);
        Assert.Equal(42, entity.LastUpdatedBy);
    }

    [Fact]
    public async Task IntUserAudit_Create_OverwritesZeroCreatedBy()
    {
        // Arrange
        TypeMap.Register<IntUserAuditEntity>();
        var resolver = new StubAuditValueResolver(42);
        var helper = new TableGateway<IntUserAuditEntity, int>(Context, resolver);

        await CreateIntUserAuditTable();

        // Act — CreatedBy is default(int) == 0, should be overwritten
        var entity = new IntUserAuditEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);

        // Assert
        Assert.Equal(42, entity.CreatedBy);
    }

    [Fact]
    public async Task IntUserAudit_Update_SetsLastUpdatedBy()
    {
        // Arrange
        TypeMap.Register<IntUserAuditEntity>();
        var resolver = new StubAuditValueResolver(42);
        var helper = new TableGateway<IntUserAuditEntity, int>(Context, resolver);

        await CreateIntUserAuditTable();

        var entity = new IntUserAuditEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);

        // Act — update should set LastUpdatedBy
        var resolver2 = new StubAuditValueResolver(99);
        var helper2 = new TableGateway<IntUserAuditEntity, int>(Context, resolver2);
        entity.Name = Guid.NewGuid().ToString();
        await helper2.UpdateAsync(entity, Context);

        // Assert — CreatedBy unchanged, LastUpdatedBy updated
        Assert.Equal(42, entity.CreatedBy);
        Assert.Equal(99, entity.LastUpdatedBy);
    }

    #region Table Creation Helpers

    private async Task CreateIntUserAuditTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}IntUserAudit{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedBy{1} INTEGER NOT NULL DEFAULT 0,
                {0}LastUpdatedBy{1} INTEGER NOT NULL DEFAULT 0,
                {0}CreatedOn{1} TIMESTAMP NOT NULL,
                {0}LastUpdatedOn{1} TIMESTAMP NOT NULL
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private async Task CreateLongUserAuditTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}LongUserAudit{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedBy{1} INTEGER NOT NULL DEFAULT 0,
                {0}LastUpdatedBy{1} INTEGER NOT NULL DEFAULT 0,
                {0}CreatedOn{1} TIMESTAMP NOT NULL,
                {0}LastUpdatedOn{1} TIMESTAMP NOT NULL
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    #endregion
}