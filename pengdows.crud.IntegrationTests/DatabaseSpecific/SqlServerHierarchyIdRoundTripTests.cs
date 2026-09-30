using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// TYPE-016: a hierarchyid column round-trips through HierarchyId with no Microsoft.SqlServer.Types
/// reference: written as text (or, declared DbType.Binary, as SQL Server's encoding), read from the
/// stored encoding, usable in WHERE, and ordered as SQL Server orders it.
/// </summary>
[Collection("IntegrationTests")]
public sealed class SqlServerHierarchyIdRoundTripTests : DatabaseTestBase
{
    private const string TableName = "hierarchy_nodes";

    private static readonly string?[] Paths =
    {
        "/", "/1/", "/1/2/", "/1/2.5/-3/", "/1.1/", "/-1.5/7/", "/79.80/1103/", "/5200.1/",
        "/281479271683151/", "/-281479271682120/", "/4294972496/", "/-4294971465/", null
    };

    public SqlServerHierarchyIdRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() => [SupportedDatabase.SqlServer];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using var table = context.CreateSqlContainer(
            $"CREATE TABLE {context.WrapObjectName(TableName)} (" +
            $"{context.WrapObjectName("id")} INT NOT NULL PRIMARY KEY, " +
            $"{context.WrapObjectName("node")} hierarchyid NULL, " +
            $"{context.WrapObjectName("node_bin")} hierarchyid NULL)");
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task HierarchyId_RoundTripsFiltersAndOrders()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.SqlServer, async context =>
        {
            var gateway = new TableGateway<NodeRow, int>(context);
            var rows = Paths.Select((path, i) => new NodeRow
            {
                Id = i + 1,
                Node = path is null ? null : HierarchyId.Parse(path),
                NodeBin = path is null ? null : HierarchyId.Parse(path)
            }).ToList();
            foreach (var row in rows)
            {
                Assert.True(await gateway.CreateAsync(row, context));
            }

            // Read back through the gateway, as HierarchyId and as text.
            var textGateway = new TableGateway<NodeTextRow, int>(context);
            foreach (var expected in rows)
            {
                var actual = await gateway.RetrieveOneAsync(expected.Id, context);
                Assert.Equal(expected.Node, actual!.Node);
                Assert.Equal(expected.NodeBin, actual.NodeBin);
                Assert.Equal(expected.Node?.ToString(), (await textGateway.RetrieveOneAsync(expected.Id, context))!.Node);
            }

            // SQL Server itself sees hierarchyid values, not text.
            var table = context.WrapObjectName(TableName);
            await using (var server = context.CreateSqlContainer(
                             $"SELECT {context.WrapObjectName("node")}.ToString() FROM {table} WHERE {context.WrapObjectName("id")} = 4"))
            {
                Assert.Equal("/1/2.5/-3/", await server.ExecuteScalarRequiredAsync<string>());
            }

            // A HierarchyId parameter filters.
            await using (var filter = gateway.BuildBaseRetrieve("n", context))
            {
                filter.Query.Append(" WHERE ").Append(filter.WrapObjectName("n.node")).Append(" = ");
                var p = filter.AddParameterWithValue("node", DbType.String, HierarchyId.Parse("/79.80/1103/"));
                filter.Query.Append(filter.MakeParameterName(p));
                Assert.Equal(7, Assert.Single(await gateway.LoadListAsync(filter)).Id);
            }

            // ORDER BY hierarchyid matches HierarchyId's own ordering.
            await using (var ordered = gateway.BuildBaseRetrieve("n", context))
            {
                ordered.Query.Append(" WHERE ").Append(ordered.WrapObjectName("n.node")).Append(" IS NOT NULL ORDER BY ")
                    .Append(ordered.WrapObjectName("n.node"));
                var fromServer = (await gateway.LoadListAsync(ordered)).Select(r => r.Node!.Value).ToList();
                var sorted = rows.Where(r => r.Node is not null).Select(r => r.Node!.Value).OrderBy(n => n).ToList();
                Assert.Equal(sorted, fromServer);
            }

            // DataReaderMapper reads it too.
            await using (var sc = context.CreateSqlContainer(
                             $"SELECT {context.WrapObjectName("id")}, {context.WrapObjectName("node")}, {context.WrapObjectName("node_bin")} FROM {table}"))
            await using (var reader = await sc.ExecuteReaderAsync())
            {
                var mapped = await DataReaderMapper.LoadAsync<NodeRow>(reader, new MapperOptions(Strict: true));
                Assert.Equal(rows.Count, mapped.Count);
                Assert.Equal(HierarchyId.Parse("/1/2.5/-3/"), mapped.Single(r => r.Id == 4).Node);
            }
        });
    }

    [Table(TableName)]
    internal sealed class NodeRow
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("node", DbType.String)] public HierarchyId? Node { get; set; }
        [Column("node_bin", DbType.Binary)] public HierarchyId? NodeBin { get; set; }
    }

    [Table(TableName)]
    internal sealed class NodeTextRow
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("node", DbType.String)] public string? Node { get; set; }
    }
}
