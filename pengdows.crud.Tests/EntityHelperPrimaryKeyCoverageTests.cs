#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayPrimaryKeyCoverageTests : RealSqliteContextTestBase
{
    public TableGatewayPrimaryKeyCoverageTests()
    {
        TypeMap.Register<PrimaryKeyEntity>();
        CreatePrimaryKeyTable();
    }

    [Fact]
    public async Task Upsert_UsesPrimaryKeyWhenIdNotWritable()
    {
        var helper = new TableGateway<PrimaryKeyEntity, long>(Context);
        var entity = new PrimaryKeyEntity
        {
            Code = $"PK-{Guid.NewGuid():N}",
            Payload = "first"
        };

        Assert.Equal(1, await helper.UpsertAsync(entity));

        entity.Payload = "updated";
        Assert.Equal(1, await helper.UpsertAsync(entity));

        await using var verify = Context.CreateSqlContainer("SELECT payload FROM primary_key_entities WHERE code = @code");
        verify.AddParameterWithValue("@code", DbType.String, entity.Code);
        var payload = await verify.ExecuteScalarRequiredAsync<string>();
        Assert.Equal("updated", payload);
    }

    private void CreatePrimaryKeyTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        using var container = Context.CreateSqlContainer();
        container.Query.Append($"""
                                CREATE TABLE IF NOT EXISTS {qp}primary_key_entities{qs} (
                                    {qp}id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
                                    {qp}code{qs} TEXT NOT NULL UNIQUE,
                                    {qp}payload{qs} TEXT
                                );
                                """);
        container.ExecuteNonQueryAsync().GetAwaiter().GetResult();
    }

    [Table("primary_key_entities")]
    private sealed class PrimaryKeyEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [PrimaryKey(1)]
        [Column("code", DbType.String)]
        public string Code { get; set; } = string.Empty;

        [Column("payload", DbType.String)] public string Payload { get; set; } = string.Empty;
    }
}
