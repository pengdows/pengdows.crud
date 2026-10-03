using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-033: a gateway is a singleton that may read through any tenant's context. It read every
/// recordset with its own context's coercion options and cached plans by recordset shape only, so
/// a gateway built on PostgreSQL could not read an Informix tenant's LIST column (it parsed the
/// literal as JSON), and the wrong plan could be reused across tenants.
/// </summary>
public sealed class GatewayTenantDialectReadTests
{
    private static DatabaseContext Context(SupportedDatabase product, string connectionString,
        params Dictionary<string, object?>[] rows)
    {
        var factory = new fakeDbFactory(product);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = product });
        var exec = new fakeDbConnection { EmulatedProduct = product };
        exec.EnqueueReaderResult(rows);
        factory.Connections.Add(exec);
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.Standard
        }, factory);
    }

    private static Dictionary<string, object?> Row(object? nums) =>
        new() { ["id"] = 1, ["nums"] = nums, ["names"] = null };

    [Fact]
    public async Task GatewayOnPostgres_ReadingThroughInformixTenant_UsesInformixReadRules()
    {
        await using var postgres = Context(SupportedDatabase.PostgreSql, "Host=x;Database=d;EmulatedProduct=PostgreSql");
        await using var informix = Context(SupportedDatabase.Informix, "Server=x;Database=d;EmulatedProduct=Informix",
            Row("LIST{1          ,2          }"));

        var row = await new TableGateway<InformixCollectionTypeTests.Row, int>(postgres).RetrieveOneAsync(1, informix);

        Assert.Equal(new[] { 1, 2 }, row!.Nums);
    }

    // Both tenants return the column as text, so the recordsets have the same shape: a plan cached
    // by shape alone would read the Informix literal with PostgreSQL's rules (JSON) or vice versa.
    [Fact]
    public async Task OneGateway_SameShapeFromTwoDialects_ReadsEachWithItsOwnRules()
    {
        await using var postgres = Context(SupportedDatabase.PostgreSql, "Host=x;Database=d;EmulatedProduct=PostgreSql",
            Row("[4,5]"));
        await using var informix = Context(SupportedDatabase.Informix, "Server=x;Database=d;EmulatedProduct=Informix",
            Row("LIST{3}"));
        await using var postgresAgain = Context(SupportedDatabase.PostgreSql, "Host=y;Database=d;EmulatedProduct=PostgreSql",
            Row("[6]"));
        var gateway = new TableGateway<InformixCollectionTypeTests.Row, int>(postgres);

        Assert.Equal(new[] { 4, 5 }, (await gateway.RetrieveOneAsync(1, postgres))!.Nums);
        Assert.Equal(new[] { 3 }, (await gateway.RetrieveOneAsync(1, informix))!.Nums);
        Assert.Equal(new[] { 6 }, (await gateway.RetrieveOneAsync(1, postgresAgain))!.Nums);
    }
}
