using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002, confirmed live on PostgreSQL 17: a C# enum property written as its name reached a
/// user-defined ENUM column as a text parameter, which PostgreSQL refuses ("column is of type mood
/// but expression is of type text"). On the PostgreSQL family the enum's parameter is now sent
/// untyped (NpgsqlDbType.Unknown), so the server applies the column's type. A plain string column
/// keeps its text type. Known gap (3.0): a MERGE upsert's VALUES list still types it as text.
/// </summary>
public sealed class PostgreSqlEnumParameterTests
{
    public enum Mood { Sad, Ok, Happy }

    [Table("moods")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("mood", DbType.String)] public Mood Mood { get; set; }
        [Column("note", DbType.String)] public string Note { get; set; } = "n";
    }

    [Table("keyed_moods")]
    public sealed class KeyedRow
    {
        [PrimaryKey(1)] [Column("code", DbType.String)] public string Code { get; set; } = "c";
        [Column("mood", DbType.String)] public Mood Mood { get; set; }
    }

    private static DatabaseContext Context(SupportedDatabase product = SupportedDatabase.PostgreSql) =>
        new($"Host=x;EmulatedProduct={product}",
            new fakeDbFactory(product) { EmulatesNpgsqlParameterMetadata = true });

    private static List<fakeDbNpgsqlParameter> Params(ISqlContainer sc) =>
        ((IDictionary<string, DbParameter>)typeof(SqlContainer)
            .GetField("_parameters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sc)!)
        .Values.Cast<fakeDbNpgsqlParameter>().ToList();

    private static void AssertEnumUntyped(ISqlContainer sc)
    {
        var parameters = Params(sc);
        Assert.Contains(parameters, p => Equals(p.Value, "Happy"));
        Assert.All(parameters.Where(p => Equals(p.Value, "Happy")),
            p => Assert.Equal(fakeNpgsqlDbType.Unknown, p.NpgsqlDbType));
        Assert.All(parameters.Where(p => Equals(p.Value, "note text")),
            p => Assert.NotEqual(fakeNpgsqlDbType.Unknown, p.NpgsqlDbType));
    }

    // TYPE-020: in your own SQL ("WHERE mood = {P}m"), a C# enum value is a name the server must read
    // as the column's type, so it is sent untyped too; plain strings keep their text type.
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void UserSql_EnumValueParameter_IsUntyped(SupportedDatabase product)
    {
        using var context = Context(product);
        using var sc = context.CreateSqlContainer();

        var p = (fakeDbNpgsqlParameter)sc.AddParameterWithValue("m", DbType.String, Mood.Happy);
        var text = (fakeDbNpgsqlParameter)sc.AddParameterWithValue("t", DbType.String, "Happy");

        Assert.Equal(fakeNpgsqlDbType.Unknown, p.NpgsqlDbType);
        Assert.Equal("Happy", p.Value); // Npgsql can't write a CLR enum (confirmed live): its name goes
        Assert.NotEqual(fakeNpgsqlDbType.Unknown, text.NpgsqlDbType);
    }

    [Fact]
    public void UserSql_EnumValueParameter_OnCockroachDb_IsItsNameAsText()
    {
        using var context = Context(SupportedDatabase.CockroachDb);
        using var sc = context.CreateSqlContainer();

        var p = (fakeDbNpgsqlParameter)sc.AddParameterWithValue("m", DbType.String, Mood.Happy);

        Assert.Equal("Happy", p.Value);
        Assert.NotEqual(fakeNpgsqlDbType.Unknown, p.NpgsqlDbType);
    }

    private static Row Sample(int id = 1) => new() { Id = id, Mood = Mood.Happy, Note = "note text" };

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void BuildCreate_EnumProperty_IsUntyped(SupportedDatabase product) =>
        AssertEnumUntyped(new TableGateway<Row, int>(Context(product)).BuildCreate(Sample()));

    // CockroachDB assigns a text parameter to an ENUM column itself, but refuses an untyped one in a
    // VALUES list ("could not determine data type of placeholder"), which its batch update uses
    // (both confirmed live, CockroachDB 25.1), so the enum keeps its text type there.
    [Fact]
    public void BuildBatchUpdate_CockroachDb_EnumPropertyStaysText()
    {
        var sc = Assert.Single(new TableGateway<Row, int>(Context(SupportedDatabase.CockroachDb))
            .BuildBatchUpdate(new[] { Sample(1), Sample(2) }));

        Assert.All(Params(sc), p => Assert.NotEqual(fakeNpgsqlDbType.Unknown, p.NpgsqlDbType));
    }

    [Fact]
    public async Task BuildUpdateAsync_EnumProperty_IsUntyped() =>
        AssertEnumUntyped(await new TableGateway<Row, int>(Context()).BuildUpdateAsync(Sample(), loadOriginal: false));

    [Fact]
    public void BuildBatchCreate_EnumProperty_IsUntyped() =>
        AssertEnumUntyped(Assert.Single(new TableGateway<Row, int>(Context()).BuildBatchCreate(new[] { Sample(1), Sample(2) })));

    [Fact]
    public void BuildBatchUpdate_EnumProperty_IsUntyped() =>
        AssertEnumUntyped(Assert.Single(new TableGateway<Row, int>(Context()).BuildBatchUpdate(new[] { Sample(1), Sample(2) })));

    [Fact]
    public void BuildUpsert_EnumProperty_IsUntyped() =>
        AssertEnumUntyped(new TableGateway<Row, int>(Context()).BuildUpsert(Sample()));

    [Fact]
    public async Task PrimaryKeyGateway_EveryWritePath_EnumPropertyIsUntyped()
    {
        var gateway = new PrimaryKeyTableGateway<KeyedRow>(Context());
        var row = new KeyedRow { Code = "a", Mood = Mood.Happy };

        AssertEnumUntyped(gateway.BuildCreate(row));
        AssertEnumUntyped(await gateway.BuildUpdateAsync(row));
        AssertEnumUntyped(gateway.BuildUpsert(row));
        AssertEnumUntyped(Assert.Single(gateway.BuildBatchCreate(new[] { row })));
    }

    [Fact]
    public void BuildCreate_OtherDatabase_LeavesEnumTyped()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite) { EmulatesNpgsqlParameterMetadata = true });

        var sc = new TableGateway<Row, int>(context).BuildCreate(Sample());

        Assert.All(Params(sc), p => Assert.NotEqual(fakeNpgsqlDbType.Unknown, p.NpgsqlDbType));
    }
}
