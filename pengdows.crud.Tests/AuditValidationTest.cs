using System;
using System.Threading.Tasks;
using Xunit;

namespace pengdows.crud.Tests;

public class AuditValidationTest : RealSqliteContextTestBase
{
    [Fact]
    public async Task VerifyAuditFieldsArePopulated()
    {
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(Context, AuditValueResolver);

        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = $@"CREATE TABLE IF NOT EXISTS {qp}Test{qs} ({qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
                   {qp}Name{qs} TEXT UNIQUE NOT NULL,
                   {qp}CreatedBy{qs} TEXT NOT NULL,
                   {qp}CreatedOn{qs} TIMESTAMP NOT NULL,
                   {qp}LastUpdatedBy{qs} TEXT NOT NULL,
                   {qp}LastUpdatedOn{qs} TIMESTAMP NULL,
                   {qp}Version{qs} INTEGER NOT NULL DEFAULT 0)";
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();

        var entity = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);

        // Verify the entity object was populated with audit fields
        Assert.Equal("test-user", entity.CreatedBy);
        Assert.Equal("test-user", entity.LastUpdatedBy);
        Assert.True(entity.CreatedOn > DateTime.MinValue);
        Assert.True(entity.LastUpdatedOn > DateTime.MinValue);

        // Also verify by loading from database
        var loaded = await helper.RetrieveOneAsync(entity.Id);
        Assert.Equal("test-user", loaded?.CreatedBy);
        Assert.Equal("test-user", loaded?.LastUpdatedBy);
        Assert.True(loaded?.CreatedOn > DateTime.MinValue);
        Assert.True(loaded?.LastUpdatedOn > DateTime.MinValue);
    }
}