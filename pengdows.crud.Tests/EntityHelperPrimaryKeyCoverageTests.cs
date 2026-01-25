#region

using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperPrimaryKeyCoverageTests : SqlLiteContextTestBase
{
    public EntityHelperPrimaryKeyCoverageTests()
    {
        TypeMap.Register<PrimaryKeyEntity>();
        CreatePrimaryKeyTable();
    }

    [Fact]
    public async Task Upsert_UsesPrimaryKeyWhenIdNotWritable()
    {
        var helper = new EntityHelper<PrimaryKeyEntity, long>(Context);
        var entity = new PrimaryKeyEntity
        {
            Code = "PK-1",
            Payload = "first"
        };

        Assert.Equal(1, await helper.UpsertAsync(entity));

        entity.Payload = "updated";
        Assert.Equal(1, await helper.UpsertAsync(entity));

        var retrieved = await helper.RetrieveOneAsync(entity);
        Assert.NotNull(retrieved);
        Assert.Equal(entity.Code, retrieved!.Code);
    }

    private void CreatePrimaryKeyTable()
    {
        using var container = Context.CreateSqlContainer();
        container.Query.Append("""
                               CREATE TABLE primary_key_entities (
                                   id INTEGER PRIMARY KEY AUTOINCREMENT,
                                   code TEXT NOT NULL UNIQUE,
                                   payload TEXT
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