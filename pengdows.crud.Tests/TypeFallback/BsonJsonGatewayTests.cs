using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

[Table("bson_docs")]
public class BsonDocumentEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("doc", DbType.Object)]
    public JsonDocument? Doc { get; set; }
}

/// <summary>
/// A SingleStore BSON column read through a gateway: the mapper reads the stored BSON document as its JSON, the way the JSON
/// reader does for a JSON column.
/// </summary>
public class BsonJsonGatewayTests
{
    [Fact]
    public async Task ABsonColumnReadsIntoAJsonDocumentProperty()
    {
        using var context = new DatabaseContext("Data Source=bson;EmulatedProduct=SingleStore",
            new fakeDbFactory(SupportedDatabase.SingleStore) { EnableDataPersistence = true });
        var bson = new byte[] { 0x0C, 0, 0, 0, 0x10, (byte)'b', 0, 2, 0, 0, 0, 0 };
        await using (var create = context.CreateSqlContainer("CREATE TABLE bson_docs (id INTEGER, doc BLOB)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using (var insert = context.CreateSqlContainer("INSERT INTO bson_docs (id, doc) VALUES (1, "))
        {
            insert.AddParameterWithValue("doc", DbType.Binary, bson);
            insert.Query.Append(insert.MakeParameterName("doc"));
            insert.Query.Append(")");
            await insert.ExecuteNonQueryAsync();
        }

        var gateway = new TableGateway<BsonDocumentEntity, int>(context);
        var row = await gateway.RetrieveOneAsync(1);

        Assert.NotNull(row);
        Assert.Equal(2, row!.Doc!.RootElement.GetProperty("b").GetInt32());
    }
}
