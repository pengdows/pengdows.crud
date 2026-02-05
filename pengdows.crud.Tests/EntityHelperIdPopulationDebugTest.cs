#region

using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayIdPopulationDebugTest
{
    private readonly TypeMapRegistry _typeMap;

    public TableGatewayIdPopulationDebugTest()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<TestEntityWithAutoId>();
    }

    [Fact]
    public async Task TableGateway_Should_Recognize_AutoId_Entity_Configuration()
    {
        // Test that TableGateway recognizes the entity as having an auto-generated ID
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(42, 1);

        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new TableGateway<TestEntityWithAutoId, int>(context);

        // Check if TableGateway recognizes the ID column configuration
        var tableInfo = _typeMap.GetTableInfo<TestEntityWithAutoId>();
        Assert.NotNull(tableInfo);
        Assert.NotNull(tableInfo.Id);

        // This is the key check - if this fails, ID population won't happen
        Assert.False(tableInfo.Id.IsIdIsWritable); // Should be false for auto-generated IDs

        // Check the SQL dialect configuration
        var dialect = context.Dialect;
        Assert.NotNull(dialect);

        // For SQL Server, SupportsInsertReturning should be true (uses OUTPUT INSERTED)
        Assert.True(dialect.SupportsInsertReturning);

        var entity = new TestEntityWithAutoId { Name = "Test" };
        Assert.Equal(0, entity.Id); // Should start at 0

        // This is where the ID population should happen
        var result = await helper.CreateAsync(entity);

        // Debug info if it fails
        if (entity.Id == 0)
        {
            throw new Exception(
                $"ID population failed. tableInfo.Id.IsIdIsWritable={tableInfo.Id.IsIdIsWritable}, dialect.SupportsInsertReturning={dialect.SupportsInsertReturning}");
        }

        Assert.True(result);
        Assert.Equal(42, entity.Id);
    }

    [Fact]
    public async Task TableGateway_Should_Work_With_Dialect_SupportsInsertReturning_True()
    {
        // Test the SQLite path (SupportsInsertReturning = true)
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetIdPopulationResult(99, 1);

        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new TableGateway<TestEntityWithAutoId, int>(context);

        var entity = new TestEntityWithAutoId { Name = "Test SQLite" };
        var result = await helper.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(99, entity.Id); // Should work via INSERT RETURNING path
    }

    [Fact]
    public async Task TableGateway_Should_Work_With_Dialect_SupportsInsertReturning_False()
    {
        // Test the SQL Server path (SupportsInsertReturning = true, uses OUTPUT INSERTED)  
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(77, 1);

        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new TableGateway<TestEntityWithAutoId, int>(context);

        var entity = new TestEntityWithAutoId { Name = "Test SQL Server" };

        var result = await helper.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(77, entity.Id); // Should work via SQL Server OUTPUT INSERTED path
    }
}