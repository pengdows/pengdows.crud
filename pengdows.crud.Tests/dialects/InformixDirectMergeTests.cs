using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// WRT-004/WRT-005, confirmed live (Informix 15, Informix.Net.Core): a MERGE source's select list
/// needs CAST(? AS type) for every value (an untyped ? is a syntax error), which needs the column's
/// declared type for INTERVAL, LIST/SET/MULTISET and BOOLEAN (a bool binds as SMALLINT, which a
/// BOOLEAN column refuses from the source). Binding the non-key values directly in UPDATE SET and
/// INSERT VALUES, where the target column types them as in a plain UPDATE/INSERT, works for all of
/// them; only the keys go through the source. Each value is then used twice ({P}name), and the
/// positional parameters are bound in order of appearance.
/// </summary>
public sealed class InformixDirectMergeTests
{
    [Table("ifx_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("iv", DbType.Object)] public TimeSpan Iv { get; set; }
        [Column("l", DbType.Object)] public int[] L { get; set; } = Array.Empty<int>();
        [Column("b", DbType.Boolean)] public bool B { get; set; }
    }

    [Table("ifx_pk_rows")]
    public sealed class PkRow
    {
        [PrimaryKey(1)] [Column("k", DbType.Int32)] public int K { get; set; }
        [Column("iv", DbType.Object)] public TimeSpan Iv { get; set; }
        [Column("b", DbType.Boolean)] public bool B { get; set; }
    }

    private static (DatabaseContext Context, fakeDbFactory Factory) Informix()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Database=test;Server=ifx;EmulatedProduct=Informix",
            DbMode = DbMode.Standard
        }, factory);
        return (context, factory);
    }

    [Fact]
    public async Task Upsert_KeysThroughTheSource_ValuesBoundDirectly()
    {
        var (context, _) = Informix();
        await using var _c = context;
        var gateway = new TableGateway<Row, int>(context);

        var sql = gateway.BuildUpsert(new Row { Id = 1 }).Query.ToString();

        Assert.Equal(
            "MERGE INTO \"ifx_rows\" t USING (SELECT CAST({P}i0 AS INT) AS \"id\" FROM sysmaster:sysdual) s ON t.\"id\" = s.\"id\" " +
            "WHEN MATCHED THEN UPDATE SET t.\"iv\" = {P}i1, t.\"l\" = {P}i2, t.\"b\" = {P}i3 " +
            "WHEN NOT MATCHED THEN INSERT (\"id\", \"iv\", \"l\", \"b\") VALUES (s.\"id\", {P}i1, {P}i2, {P}i3);",
            sql);
    }

    [Fact]
    public async Task Upsert_BindsEachValueAtEachUse_InOrder()
    {
        var (context, factory) = Informix();
        await using var _c = context;
        var gateway = new TableGateway<Row, int>(context);

        await gateway.UpsertAsync(new Row { Id = 1, Iv = TimeSpan.FromDays(2), L = new[] { 3, 4 }, B = true });

        var command = factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryCommands)
            .Single(c => c.CommandText.StartsWith("MERGE", StringComparison.Ordinal));
        Assert.DoesNotContain("{P}", command.CommandText);
        Assert.Equal(7, command.CommandText.Count(ch => ch == '?'));
        var values = command.Parameters.Select(p => p.Value).ToArray();
        Assert.Equal(7, values.Length);
        Assert.Equal(1, values[0]);
        Assert.Equal(values[1], values[4]);
        Assert.Equal(values[2], values[5]);
        Assert.Equal(values[3], values[6]);
    }

    [Fact]
    public async Task PrimaryKeyGatewayUpsert_KeysThroughTheSource_ValuesBoundDirectly()
    {
        var (context, _) = Informix();
        await using var _c = context;
        var gateway = new PrimaryKeyTableGateway<PkRow>(context);

        var sql = gateway.BuildUpsert(new PkRow { K = 1 }).Query.ToString();

        Assert.Equal(
            "MERGE INTO \"ifx_pk_rows\" t USING (SELECT CAST({P}i0 AS INT) AS \"k\" FROM sysmaster:sysdual) s ON t.\"k\" = s.\"k\" " +
            "WHEN MATCHED THEN UPDATE SET t.\"iv\" = {P}i1, t.\"b\" = {P}i2 " +
            "WHEN NOT MATCHED THEN INSERT (\"k\", \"iv\", \"b\") VALUES (s.\"k\", {P}i1, {P}i2);",
            sql);
    }
}
