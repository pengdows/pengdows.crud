using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002 (found live on SQL Server 2025): a JSON column declared DbType.Object is bound as its
/// serialized text, so the parameter must carry a string DbType. Left as DbType.Object, SqlClient
/// sends sql_variant, which SQL Server refuses for a json column ("Operand type clash: sql_variant
/// is incompatible with json"). The compiled insert/upsert/update binders skipped the JSON
/// parameter marking that the uncompiled paths apply.
/// </summary>
public class JsonParameterTextBindingTests
{
    [Table("json_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Json] [Column("doc", DbType.Object)] public JsonValue Doc { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "n";
    }

    private static TableGateway<Row, int> Gateway(out DatabaseContext context)
    {
        context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer));
        return new TableGateway<Row, int>(context);
    }

    private static IEnumerable<DbParameter> Params(ISqlContainer sc) =>
        ((IDictionary<string, DbParameter>)typeof(SqlContainer)
            .GetField("_parameters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sc)!).Values;

    private static Row Sample() => new() { Id = 1, Doc = new JsonValue("{\"a\":1}") };

    [Fact]
    public void BuildCreate_JsonColumnDeclaredObject_BindsText()
    {
        var sc = Gateway(out _).BuildCreate(Sample());

        var p = Params(sc).Single(x => Equals(x.Value, "{\"a\":1}"));
        Assert.Equal(DbType.String, p.DbType);
    }

    [Fact]
    public void BuildUpsert_JsonColumnDeclaredObject_BindsText()
    {
        var sc = Gateway(out _).BuildUpsert(Sample());

        Assert.All(Params(sc).Where(x => Equals(x.Value, "{\"a\":1}")),
            p => Assert.Equal(DbType.String, p.DbType));
        Assert.Contains(Params(sc), x => Equals(x.Value, "{\"a\":1}"));
    }

    [Fact]
    public async Task BuildUpdateAsync_JsonColumnDeclaredObject_BindsText()
    {
        var sc = await Gateway(out _).BuildUpdateAsync(Sample(), loadOriginal: false);

        var p = Params(sc).Single(x => Equals(x.Value, "{\"a\":1}"));
        Assert.Equal(DbType.String, p.DbType);
    }
}
