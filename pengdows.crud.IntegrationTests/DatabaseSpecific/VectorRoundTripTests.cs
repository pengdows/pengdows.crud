using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// TYPE-015: a float[] property round-trips through SQL Server 2025 VECTOR and Oracle 23ai VECTOR
/// with no provider types: written as exact text, read back as text (SqlClient 6.0) or float[]
/// (ODP.NET), and usable as a VECTOR_DISTANCE argument. Values here have at most 8 significant
/// digits, which is all SqlClient 6.0's text form carries (6.1+ reads SqlVector exactly).
/// </summary>
[Collection("IntegrationTests")]
public sealed class VectorRoundTripTests : DatabaseTestBase
{
    private const string TableName = "vector_rows";

    private static readonly float[][] Embeddings =
    {
        new[] { 1.5f, 2f, -3f, 0.25f },
        new[] { 0.1f, -0.5f, 1e-10f, 12345.678f },
        new[] { 3.4028235e38f, -3.4028235e38f, 1f, 0f },
    };

    public VectorRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        [SupportedDatabase.SqlServer, SupportedDatabase.Oracle];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var vectorType = provider == SupportedDatabase.Oracle ? "VECTOR(4, FLOAT32)" : "VECTOR(4)";
        var intType = provider == SupportedDatabase.Oracle ? "NUMBER(10)" : "INT";
        await using var table = context.CreateSqlContainer(
            $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, TableName)} (" +
            $"{context.WrapObjectName("id")} {intType} NOT NULL PRIMARY KEY, " +
            $"{context.WrapObjectName("embedding")} {vectorType} NULL)" +
            // The Oracle harness connects as SYSTEM, whose tablespace can't hold VECTOR (ORA-43853).
            (provider == SupportedDatabase.Oracle ? " TABLESPACE USERS" : ""));
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task FloatArray_RoundTripsAndFiltersThroughVector()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<VectorRow, int>(context);
            var rows = Embeddings.Select((e, i) => new VectorRow { Id = i + 1, Embedding = e }).ToList();
            rows.Add(new VectorRow { Id = 4, Embedding = null });

            // Oracle takes the array-bound batch path (BP-309); SQL Server a multi-row insert.
            Assert.Equal(rows.Count, await gateway.BatchCreateAsync(rows, context));

            foreach (var expected in rows)
            {
                var actual = await gateway.RetrieveOneAsync(expected.Id, context);
                Assert.Equal(expected.Embedding, actual!.Embedding);
            }

            // A float[] parameter is a VECTOR_DISTANCE argument.
            await using var nearest = gateway.BuildBaseRetrieve("v", context);
            var p = nearest.AddParameterWithValue("probe", DbType.Object, Embeddings[1]);
            var embedding = nearest.WrapObjectName("v.embedding");
            nearest.Query.Append(" WHERE ").Append(embedding).Append(" IS NOT NULL ORDER BY ").Append(
                provider == SupportedDatabase.Oracle
                    ? $"VECTOR_DISTANCE({embedding}, TO_VECTOR({nearest.MakeParameterName(p)}), COSINE)"
                    : $"VECTOR_DISTANCE('cosine', {embedding}, CAST({nearest.MakeParameterName(p)} AS VECTOR(4)))");
            var ordered = await gateway.LoadListAsync(nearest);
            Assert.Equal(2, ordered[0].Id);
        });
    }

    [Table(TableName)]
    internal sealed class VectorRow
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("embedding", DbType.Object)] public float[]? Embedding { get; set; }
    }
}
