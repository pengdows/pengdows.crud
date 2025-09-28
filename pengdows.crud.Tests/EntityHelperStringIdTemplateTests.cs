using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperStringIdTemplateTests
{
    private readonly TypeMapRegistry _typeMap;

    [Table("StringEntities")]
    private class StringIdEntity
    {
        [Id(false)]
        [Column("Id", DbType.String)]
        public string Id { get; set; } = string.Empty;

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    public EntityHelperStringIdTemplateTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<StringIdEntity>();
    }

    [Fact]
    public async Task RetrieveAsync_Sqlite_StringId_UsesCachedTemplate()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = "alpha", ["Name"] = "first" }
        });
        factory.Connections.Add(execConn);

        using var ctx = new DatabaseContext("Data Source=file:stringids?mode=memory&cache=shared;EmulatedProduct=Sqlite", factory, _typeMap);
        var helper = new EntityHelper<StringIdEntity, string>(ctx);

        var result = await helper.RetrieveAsync(new[] { "alpha" });

        Assert.Single(result);
        Assert.Equal("first", result[0].Name);
    }

    [Fact]
    public async Task RetrieveAsync_Postgres_StringId_UsesCachedTemplate()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = "bravo", ["Name"] = "second" }
        });
        factory.Connections.Add(execConn);

        using var ctx = new DatabaseContext("Data Source=pg;EmulatedProduct=PostgreSql", factory, _typeMap);
        var helper = new EntityHelper<StringIdEntity, string>(ctx);

        var result = await helper.RetrieveAsync(new[] { "bravo" });

        Assert.Single(result);
        Assert.Equal("second", result[0].Name);
    }
}
