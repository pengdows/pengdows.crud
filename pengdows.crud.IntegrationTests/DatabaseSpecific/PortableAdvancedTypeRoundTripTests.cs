using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

[Collection("IntegrationTests")]
public sealed class PortableAdvancedTypeRoundTripTests : DatabaseTestBase
{
    public PortableAdvancedTypeRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        [SupportedDatabase.SqlServer, SupportedDatabase.PostgreSql, SupportedDatabase.MySql,
         SupportedDatabase.MariaDb, SupportedDatabase.Sqlite, SupportedDatabase.DuckDB,
         SupportedDatabase.Firebird, SupportedDatabase.CockroachDb, SupportedDatabase.YugabyteDb,
         SupportedDatabase.TiDb, SupportedDatabase.Db2, SupportedDatabase.Oracle];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var sql = provider switch
        {
            SupportedDatabase.SqlServer => "CREATE TABLE [dbo].[portable_advanced_types] ([id] INT NOT NULL PRIMARY KEY, [payload] NVARCHAR(MAX) NOT NULL, [bytes] VARBINARY(MAX) NOT NULL, [content] VARBINARY(MAX) NOT NULL, [notes] NVARCHAR(MAX) NOT NULL)",
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb => "CREATE TABLE portable_advanced_types (id INT PRIMARY KEY, payload JSONB NOT NULL, bytes BYTEA NOT NULL, content BYTEA NOT NULL, notes TEXT NOT NULL)",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => "CREATE TABLE portable_advanced_types (id INT NOT NULL PRIMARY KEY, payload JSON NOT NULL, bytes LONGBLOB NOT NULL, content LONGBLOB NOT NULL, notes LONGTEXT NOT NULL)",
            SupportedDatabase.TiDb => "CREATE TABLE portable_advanced_types (id INT NOT NULL PRIMARY KEY, payload LONGTEXT NOT NULL, bytes LONGBLOB NOT NULL, content LONGBLOB NOT NULL, notes LONGTEXT NOT NULL)",
            SupportedDatabase.DuckDB => "CREATE TABLE portable_advanced_types (id INTEGER PRIMARY KEY, payload JSON NOT NULL, bytes BLOB NOT NULL, content BLOB NOT NULL, notes VARCHAR NOT NULL)",
            SupportedDatabase.Firebird => "CREATE TABLE \"portable_advanced_types\" (\"id\" INTEGER PRIMARY KEY, \"payload\" BLOB SUB_TYPE TEXT NOT NULL, \"bytes\" BLOB SUB_TYPE BINARY NOT NULL, \"content\" BLOB SUB_TYPE BINARY NOT NULL, \"notes\" BLOB SUB_TYPE TEXT NOT NULL)",
            SupportedDatabase.Db2 => "CREATE TABLE \"portable_advanced_types\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"payload\" CLOB NOT NULL, \"bytes\" BLOB(1M) NOT NULL, \"content\" BLOB(1M) NOT NULL, \"notes\" CLOB NOT NULL)",
            SupportedDatabase.Oracle => "CREATE TABLE \"portable_advanced_types\" (\"id\" NUMBER(10) PRIMARY KEY, \"payload\" CLOB NOT NULL, \"bytes\" BLOB NOT NULL, \"content\" BLOB NOT NULL, \"notes\" CLOB NOT NULL)",
            _ => "CREATE TABLE portable_advanced_types (id INTEGER PRIMARY KEY, payload TEXT NOT NULL, bytes BLOB NOT NULL, content BLOB NOT NULL, notes TEXT NOT NULL)"
        };
        await using var table = context.CreateSqlContainer(sql);
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task JsonDocumentAndBinary_RoundTripThroughCrudMapper()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider == SupportedDatabase.TiDb)
            {
                await RoundTripTiDbThroughPortableJsonColumn(context);
                return;
            }

            using var document = JsonDocument.Parse("{\"provider\":\"" + provider + "\",\"enabled\":true}");
            byte[] expectedContent = [9, 8, 7, 6];
            var expected = new PortableAdvancedTypeEntity
            {
                Id = 1,
                Payload = document,
                Bytes = [0, 1, 2, 127, 255],
                Content = new MemoryStream(expectedContent),
                Notes = new StringReader($"streamed notes for {provider}")
            };

            var gateway = new TableGateway<PortableAdvancedTypeEntity, int>(context);
            await gateway.CreateAsync(expected, context);

            var rawTable = provider is SupportedDatabase.Firebird or SupportedDatabase.Oracle or SupportedDatabase.Db2
                ? "\"portable_advanced_types\""
                : "portable_advanced_types";
            var rawId = provider is SupportedDatabase.Firebird or SupportedDatabase.Oracle or SupportedDatabase.Db2 ? "\"id\"" : "id";
            var rawPayload = provider is SupportedDatabase.Firebird or SupportedDatabase.Oracle or SupportedDatabase.Db2 ? "\"payload\"" : "payload";
            await using (var raw = context.CreateSqlContainer(
                             $"SELECT {rawPayload} FROM {rawTable} WHERE {rawId} = 1"))
            await using (var rawReader = await raw.ExecuteReaderAsync())
            {
                Assert.True(await rawReader.ReadAsync());
                Assert.False(rawReader.IsDBNull(0));
                Output.WriteLine($"{provider} stored payload: {rawReader.GetValue(0)} ({rawReader.GetFieldType(0)})");
            }

            var actual = await gateway.RetrieveOneAsync(expected.Id, context);

            Assert.NotNull(actual);
            Assert.NotNull(actual!.Payload);
            Assert.True(JsonNode.DeepEquals(
                JsonNode.Parse(expected.Payload.RootElement.GetRawText()),
                JsonNode.Parse(actual.Payload.RootElement.GetRawText())),
                $"Actual JSON: {actual.Payload.RootElement.GetRawText()}");
            Assert.Equal(expected.Bytes, actual.Bytes);
            Assert.NotNull(actual.Content);
            await using (actual.Content)
            {
                using var content = new MemoryStream();
                await actual.Content.CopyToAsync(content);
                Assert.Equal(expectedContent, content.ToArray());
            }

            Assert.NotNull(actual.Notes);
            using (actual.Notes)
            {
                Assert.Equal($"streamed notes for {provider}", await actual.Notes.ReadToEndAsync());
            }
        });
    }

    private static async Task RoundTripTiDbThroughPortableJsonColumn(IDatabaseContext context)
    {
        var expected = new TiDbPortableTypeEntity
        {
            Id = 1,
            Payload = "{\"provider\":\"TiDb\",\"enabled\":true}",
            Bytes = [0, 1, 2, 127, 255],
            Content = new MemoryStream([9, 8, 7, 6]),
            Notes = new StringReader("streamed notes for TiDb")
        };

        var gateway = new TableGateway<TiDbPortableTypeEntity, int>(context);
        await gateway.CreateAsync(expected, context);

        var actual = await gateway.RetrieveOneAsync(expected.Id, context);

        Assert.NotNull(actual);
        Assert.Equal(expected.Payload, actual!.Payload);
        Assert.Equal(expected.Bytes, actual.Bytes);
        await using (actual.Content)
        {
            using var content = new MemoryStream();
            await actual.Content.CopyToAsync(content);
            Assert.Equal(new byte[] { 9, 8, 7, 6 }, content.ToArray());
        }

        using (actual.Notes)
        {
            Assert.Equal("streamed notes for TiDb", await actual.Notes.ReadToEndAsync());
        }
    }
}

[Table("portable_advanced_types")]
internal sealed class PortableAdvancedTypeEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    // String is the portable JSON transport type; providers with native JSON
    // support still receive their JSON metadata through the dialect.
    [Column("payload", DbType.String)]
    public JsonDocument Payload { get; set; } = null!;

    [Column("bytes", DbType.Binary)]
    public byte[] Bytes { get; set; } = [];

    [Column("content", DbType.Object)]
    public Stream Content { get; set; } = null!;

    [Column("notes", DbType.Object)]
    public TextReader Notes { get; set; } = null!;
}

[Table("portable_advanced_types")]
internal sealed class TiDbPortableTypeEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("payload", DbType.String)]
    public string Payload { get; set; } = string.Empty;

    [Column("bytes", DbType.Binary)]
    public byte[] Bytes { get; set; } = [];

    [Column("content", DbType.Object)]
    public Stream Content { get; set; } = null!;

    [Column("notes", DbType.Object)]
    public TextReader Notes { get; set; } = null!;
}
