using System.Data;
using Npgsql;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed.PostgreSQL;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// TYPE-015: pgvector (its own pgvector/pgvector container; the shared PostgreSQL image has no
/// extension). Npgsql binds a float[] as real[], which pgvector's assignment cast stores; Npgsql
/// can't read the vector type without the Pgvector.Npgsql plugin, so the portable read is a
/// ::real[] cast. Runs when PostgreSQL is enabled.
/// </summary>
[Collection("IntegrationTests")]
public sealed class PgvectorRoundTripTests : DatabaseTestBase
{
    public PgvectorRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() => [SupportedDatabase.PostgreSql];

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    [SkippableFact]
    public async Task FloatArray_WritesToVectorAndReadsThroughARealArrayCast()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async _ =>
        {
            var container = new PostgreSqlTestContainer("pgvector/pgvector:pg17");
            await container.StartAsync();
            try
            {
                await using var context = new DatabaseContext(container.ConnectionString, NpgsqlFactory.Instance);
                await using (var ddl = context.CreateSqlContainer(
                                 "CREATE EXTENSION IF NOT EXISTS vector; " +
                                 "CREATE TABLE \"pgvector_rows\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"embedding\" vector(3) NULL)"))
                {
                    await ddl.ExecuteNonQueryAsync();
                }

                var gateway = new TableGateway<PgvectorRow, int>(context);
                var rows = new[]
                {
                    new PgvectorRow { Id = 1, Embedding = new[] { 1.5f, 2f, -3f } },
                    new PgvectorRow { Id = 2, Embedding = new[] { 0.1f, 3.4028235e38f, 1e-10f } },
                    new PgvectorRow { Id = 3, Embedding = null },
                };
                foreach (var row in rows)
                {
                    Assert.True(await gateway.CreateAsync(row, context));
                }

                await using var read = context.CreateSqlContainer(
                    "SELECT \"id\", \"embedding\"::real[] AS \"embedding\" FROM \"pgvector_rows\" ORDER BY \"id\"");
                var back = await gateway.LoadListAsync(read);
                Assert.Equal(rows.Select(r => r.Embedding), back.Select(r => r.Embedding));

                // TYPE-002: without the plugin Npgsql can't read vector, but its binary value comes
                // through GetBytes and the dialect decodes it, so the column reads directly too.
                foreach (var row in rows)
                {
                    Assert.Equal(row.Embedding, (await gateway.RetrieveOneAsync(row.Id, context))!.Embedding);
                }

                await using (var plain = context.CreateSqlContainer(
                                 "SELECT \"id\" AS \"Id\", \"embedding\" AS \"Embedding\" FROM \"pgvector_rows\" WHERE \"id\" = 1"))
                await using (var reader = await plain.ExecuteReaderAsync())
                {
                    var mapped = Assert.Single(await DataReaderMapper.LoadAsync<PgvectorRow>(reader, MapperOptions.Default));
                    Assert.Equal(rows[0].Embedding, mapped.Embedding);
                }
            }
            finally
            {
                await container.DisposeAsync();
            }
        });
    }

    [Table("pgvector_rows")]
    internal sealed class PgvectorRow
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("embedding", DbType.Object)] public float[]? Embedding { get; set; }
    }
}
