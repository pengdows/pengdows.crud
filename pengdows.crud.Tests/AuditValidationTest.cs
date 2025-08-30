using System;
using System.Threading.Tasks;
using Xunit;

namespace pengdows.crud.Tests;

public class AuditValidationTest : SqlLiteContextTestBase
{
    [Fact]
    public async Task VerifyAuditFieldsArePopulated()
    {
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(Context, AuditValueResolver);
        
        var sql = @"CREATE TABLE IF NOT EXISTS ""Test"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                   ""Name"" TEXT UNIQUE NOT NULL,
                   ""CreatedBy"" TEXT NOT NULL,
                   ""CreatedOn"" TIMESTAMP NOT NULL,
                   ""LastUpdatedBy"" TEXT NOT NULL,
                   ""LastUpdatedOn"" TIMESTAMP NULL,
                   ""Version"" INTEGER NOT NULL DEFAULT 0)";
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
