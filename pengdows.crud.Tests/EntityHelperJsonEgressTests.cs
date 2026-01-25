using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperJsonEgressTests
{
    [Fact]
    public void BuildCreate_PostgresJsonColumn_AppendsJsonCast()
    {
        var registry = new TypeMapRegistry();
        registry.Register<JsonEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql",
            new fakeDbFactory(SupportedDatabase.PostgreSql), registry);
        var helper = new EntityHelper<JsonEntity, int>(ctx, new StubAuditValueResolver("tester"));
        var entity = new JsonEntity { Payload = new SamplePayload { Message = "hi" } };

        var container = helper.BuildCreate(entity, ctx);
        var sql = container.Query.ToString();

        Assert.Contains("::jsonb", sql);

        var expectedJson = JsonSerializer.Serialize(entity.Payload);
        var parameter = GetJsonParameter(container, expectedJson);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    public void BuildCreate_SqlServerJsonColumn_DoesNotAppendCast()
    {
        var registry = new TypeMapRegistry();
        registry.Register<JsonEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer), registry);
        var helper = new EntityHelper<JsonEntity, int>(ctx, new StubAuditValueResolver("tester"));
        var entity = new JsonEntity { Payload = new SamplePayload { Message = "hi" } };

        var container = helper.BuildCreate(entity, ctx);
        var sql = container.Query.ToString();

        Assert.DoesNotContain("::jsonb", sql);

        var expectedJson = JsonSerializer.Serialize(entity.Payload);
        var parameter = GetJsonParameter(container, expectedJson);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    private static DbParameter GetJsonParameter(ISqlContainer container, string expectedJson)
    {
        var sqlContainer = Assert.IsType<SqlContainer>(container);
        var field = typeof(SqlContainer).GetField("_parameters", BindingFlags.Instance | BindingFlags.NonPublic);
        var dictionary = (IDictionary<string, DbParameter>)field!.GetValue(sqlContainer)!;

        return Assert.Single(dictionary.Values, p => p.Value is string text && text == expectedJson);
    }

    [Table("JsonEntities")]
    private class JsonEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

        [Json]
        [Column("Payload", DbType.String)]
        public SamplePayload Payload { get; set; } = new();
    }

    private class SamplePayload
    {
        public string Message { get; set; } = string.Empty;
    }
}