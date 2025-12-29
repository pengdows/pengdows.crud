#region

using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperRetrieveAsyncSetValuedTests
{
    private readonly TypeMapRegistry _typeMap;

    [Table("Ret")] private class RetEntity
    {
        [Id(false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [PrimaryKey(1)] [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    public EntityHelperRetrieveAsyncSetValuedTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<RetEntity>();
    }

    [Fact]
    public async Task RetrieveAsync_Postgres_UsesArrayBinding_PathAndReturnsRows()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        // Seed one init connection (unused) and one configured execution connection
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        // Queue rows for the select execution
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "a" },
            new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "b" }
        });
        factory.Connections.Add(execConn);

        using var ctx = new DatabaseContext("Data Source=pg;EmulatedProduct=PostgreSql", factory, _typeMap);
        var helper = new EntityHelper<RetEntity, int>(ctx);

        var result = await helper.RetrieveAsync(new[] { 1, 2, 3 });
        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0].Name);
        Assert.Equal("b", result[1].Name);
    }

    [Fact]
    public async Task RetrieveAsync_Sqlite_UsesExpandedParameters_PathAndReturnsRows()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        // Seed init connection and configured execution connection
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "x" },
            new Dictionary<string, object?> { ["Id"] = 3, ["Name"] = "z" }
        });
        factory.Connections.Add(execConn);

        using var ctx = new DatabaseContext("Data Source=sqlite;EmulatedProduct=Sqlite", factory, _typeMap);
        var helper = new EntityHelper<RetEntity, int>(ctx);

        var result = await helper.RetrieveAsync(new[] { 1, 2, 3 });
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(3, result[1].Id);
    }
}
