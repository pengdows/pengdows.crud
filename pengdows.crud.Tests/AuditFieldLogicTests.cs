using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class AuditFieldLogicTests : SqlLiteContextTestBase
{
    [Table("TimeOnlyAudit")]
    private class TimeOnlyAuditEntity
    {
        [Id(writable: false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [Column("CreatedOn", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    [Table("UserAudit")]
    private class UserAuditEntity
    {
        [Id(writable: false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedBy]
        [Column("CreatedBy", DbType.String)]
        public string CreatedBy { get; set; } = string.Empty;

        [LastUpdatedBy]
        [Column("LastUpdatedBy", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;
    }

    [Fact]
    public async Task TimeOnlyAuditFields_NoResolver_ShouldWork()
    {
        // Arrange
        TypeMap.Register<TimeOnlyAuditEntity>();
        var helper = new EntityHelper<TimeOnlyAuditEntity, int>(Context); // No audit resolver
        
        await CreateTimeOnlyAuditTable();
        
        // Act & Assert - Should not throw
        var entity = new TimeOnlyAuditEntity { Name = Guid.NewGuid().ToString() };
        var success = await helper.CreateAsync(entity, Context);
        
        Assert.True(success);
        Assert.True(entity.CreatedOn > DateTime.MinValue);
        Assert.True(entity.LastUpdatedOn > DateTime.MinValue);
    }

    [Fact]
    public async Task UserAuditFields_NoResolver_ShouldThrow()
    {
        // Arrange
        TypeMap.Register<UserAuditEntity>();
        var helper = new EntityHelper<UserAuditEntity, int>(Context); // No audit resolver
        
        await CreateUserAuditTable();
        
        // Act & Assert
        var entity = new UserAuditEntity { Name = Guid.NewGuid().ToString() };
        
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await helper.CreateAsync(entity, Context));

        Assert.Contains("AuditValues", exception.Message);
        Assert.Contains("resolver", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserAuditFields_WithResolver_ShouldWork()
    {
        // Arrange
        TypeMap.Register<UserAuditEntity>();
        var helper = new EntityHelper<UserAuditEntity, int>(Context, AuditValueResolver);
        
        await CreateUserAuditTable();
        
        // Act & Assert - Should not throw
        var entity = new UserAuditEntity { Name = Guid.NewGuid().ToString() };
        var success = await helper.CreateAsync(entity, Context);
        
        Assert.True(success);
        Assert.Equal("test-user", entity.CreatedBy);
        Assert.Equal("test-user", entity.LastUpdatedBy);
    }

    [Fact]
    public async Task MixedAuditFields_NoResolver_ShouldThrow()
    {
        // Arrange - TestEntity has both time and user fields
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(Context); // No audit resolver
        
        await CreateTestTable();
        
        // Act & Assert
        var entity = new TestEntity { Name = Guid.NewGuid().ToString() };
        
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await helper.CreateAsync(entity, Context));

        Assert.Contains("AuditValues", exception.Message);
        Assert.Contains("resolver", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateOperation_UserFields_NoResolver_ShouldThrow()
    {
        // Arrange
        TypeMap.Register<UserAuditEntity>();
        var helperWithResolver = new EntityHelper<UserAuditEntity, int>(Context, AuditValueResolver);
        var helperWithoutResolver = new EntityHelper<UserAuditEntity, int>(Context); // No resolver
        
        await CreateUserAuditTable();
        
        // Create entity with resolver first
        var entity = new UserAuditEntity { Name = Guid.NewGuid().ToString() };
        await helperWithResolver.CreateAsync(entity, Context);
        
        // Act & Assert - Update without resolver should throw
        entity.Name = "Updated Name";
        
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await helperWithoutResolver.UpdateAsync(entity, Context));

        Assert.Contains("AuditValues", exception.Message);
        Assert.Contains("resolver", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CreateTimeOnlyAuditTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}TimeOnlyAudit{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedOn{1} TIMESTAMP NOT NULL,
                {0}LastUpdatedOn{1} TIMESTAMP NOT NULL
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private async Task CreateUserAuditTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}UserAudit{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedBy{1} TEXT NOT NULL,
                {0}LastUpdatedBy{1} TEXT NOT NULL
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private async Task CreateTestTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}Test{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedBy{1} TEXT NOT NULL,
                {0}CreatedOn{1} TIMESTAMP NOT NULL,
                {0}LastUpdatedBy{1} TEXT NOT NULL,
                {0}LastUpdatedOn{1} TIMESTAMP NOT NULL,
                {0}Version{1} INTEGER NOT NULL DEFAULT 0
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }
}
