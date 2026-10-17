using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Contract: a single-row upsert must be an atomic statement on engines where plain MERGE is not.
///
/// These expressed the DESIRED behaviour and failed on 2.0.6 (audit G-01); they pass once the dialect chooses an atomic form:
/// - SQL Server: BuildUpsert emits "MERGE INTO t USING ..." with no HOLDLOCK/SERIALIZABLE hint, so two
///   sessions racing on a new key can both take WHEN NOT MATCHED; one hits a unique violation.
/// - PostgreSQL 15+: BuildUpsert checks SupportsMerge before SupportsInsertOnConflict, so single-row
///   upsert uses MERGE (can raise unique violations / "tuple concurrently updated") while batch upsert
///   uses INSERT ... ON CONFLICT (atomic).
/// They go green once the library chooses an atomic form, or the maintainers decide otherwise and
/// replace them with a test documenting that decision. ConcurrentUpsertTests (integration) is the
/// behavioural proof; these pin the SQL shape without needing a database.
/// </summary>
public class UpsertAtomicityContractTests
{
    [Table("atomicity_rows")]
    public class Row
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [PrimaryKey(1)][Column("code", DbType.String)] public string Code { get; set; } = "";
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    [Table("atomicity_pairs")]
    public class PkRow
    {
        [PrimaryKey(1)][Column("a", DbType.Int32)] public int A { get; set; }
        [PrimaryKey(2)][Column("b", DbType.Int32)] public int B { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    private static DatabaseContext Context(SupportedDatabase db, string? serverVersion = null)
    {
        var factory = new fakeDbFactory(db);
        if (serverVersion != null)
        {
            factory.ServerVersion = serverVersion;
        }

        return new DatabaseContext($"Data Source=test;EmulatedProduct={db}", factory);
    }

    private static string SingleRowSql(DatabaseContext ctx)
    {
        var gw = new TableGateway<Row, long>(ctx);
        using var sc = gw.BuildUpsert(new Row { Id = 1, Code = "c", Name = "n" });
        return sc.Query.ToString();
    }

    private static string PkSingleRowSql(DatabaseContext ctx)
    {
        var gw = new PrimaryKeyTableGateway<PkRow>(ctx);
        using var sc = gw.BuildUpsert(new PkRow { A = 1, B = 2, Name = "n" });
        return sc.Query.ToString();
    }

    [Fact]
    public void SqlServer_SingleRowUpsert_SerializesTheMergeTarget()
    {
        var sql = SingleRowSql(Context(SupportedDatabase.SqlServer));

        Assert.Contains("MERGE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HOLDLOCK", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_PrimaryKeyGatewayUpsert_SerializesTheMergeTarget()
    {
        var sql = PkSingleRowSql(Context(SupportedDatabase.SqlServer));

        Assert.Contains("HOLDLOCK", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_BatchUpsert_SerializesTheMergeTarget()
    {
        var gw = new TableGateway<Row, long>(Context(SupportedDatabase.SqlServer));
        var statements = gw.BuildBatchUpsert(new List<Row>
        {
            new() { Id = 1, Code = "a", Name = "x" },
            new() { Id = 2, Code = "b", Name = "y" }
        }).Select(c => c.Query.ToString()).ToList();

        Assert.NotEmpty(statements);
        Assert.All(statements, sql =>
        {
            Assert.Contains("MERGE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("HOLDLOCK", sql, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("15.0")]
    [InlineData("16.0")]
    [InlineData("17.0")]
    public void PostgreSql15Plus_SingleRowUpsert_UsesAtomicOnConflict(string serverVersion)
    {
        var sql = SingleRowSql(Context(SupportedDatabase.PostgreSql, serverVersion));

        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSql15Plus_SingleRowAndBatchUpsert_UseTheSameForm()
    {
        var ctx = Context(SupportedDatabase.PostgreSql, "16.0");
        var gw = new TableGateway<Row, long>(ctx);
        var single = SingleRowSql(ctx);
        var batch = gw.BuildBatchUpsert(new List<Row>
        {
            new() { Id = 1, Code = "a", Name = "x" },
            new() { Id = 2, Code = "b", Name = "y" }
        }).Single().Query.ToString();

        Assert.Equal(
            single.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase),
            batch.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase));
    }
}
