using System;
using System.Collections.Generic;
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
/// TYPE-020, confirmed live (Snowflake, Snowflake.Data): a VECTOR column takes a value only as
/// PARSE_JSON(:p)::VECTOR(FLOAT|INT, n), with n its exact dimension (text, an ARRAY or a cast without
/// the dimension are refused: "expecting VECTOR(FLOAT, 3) but got ARRAY"), and the driver reports the
/// column only as VECTOR, so the dimension is the value's length, set when the command is prepared.
/// A VECTOR reads back as JSON array text ("[1.000000,2.000000,3.500000]").
/// </summary>
public sealed class SnowflakeVectorTests
{
    [Table("sf_vectors")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("vf", DbType.Object)] public float[]? Vf { get; set; }
        [Column("vd", DbType.Object)] public double[]? Vd { get; set; }
        [Column("vi", DbType.Object)] public int[]? Vi { get; set; }
    }

    private static (DatabaseContext Context, fakeDbConnection Exec) Snowflake(IEnumerable<Dictionary<string, object?>>? rows = null)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake };
        if (rows != null)
        {
            exec.EnqueueReaderResult(rows);
        }

        factory.Connections.Add(exec);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Account=x;EmulatedProduct=Snowflake",
            DbMode = DbMode.Standard
        }, factory), exec);
    }

    private static CapturedCommand Single(fakeDbConnection exec, string start) =>
        exec.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith(start, StringComparison.Ordinal));

    [Fact]
    public async Task Create_CastsEachVectorToItsDimension()
    {
        var (context, exec) = Snowflake();
        await using var _ = context;

        await new TableGateway<Row, int>(context).CreateAsync(new Row
        {
            Id = 1, Vf = new[] { 1.5f, 2f, -3f }, Vd = new[] { 0.25 }, Vi = new[] { 7, -8 }
        });

        var insert = Single(exec, "INSERT");
        Assert.Matches(@"PARSE_JSON\(:\w+\)::VECTOR\(FLOAT, 3\)", insert.CommandText);
        Assert.Matches(@"PARSE_JSON\(:\w+\)::VECTOR\(FLOAT, 1\)", insert.CommandText);
        Assert.Matches(@"PARSE_JSON\(:\w+\)::VECTOR\(INT, 2\)", insert.CommandText);
        var values = insert.Parameters.Select(p => p.Value).ToList();
        Assert.Contains("[1.5,2,-3]", values);
        Assert.Contains("[0.25]", values);
        Assert.Contains("[7,-8]", values);
    }

    [Fact]
    public async Task Create_NullVector_IsWrittenAsNull()
    {
        var (context, exec) = Snowflake();
        await using var _ = context;

        await new TableGateway<Row, int>(context).CreateAsync(new Row { Id = 1, Vf = new[] { 1f } });

        var insert = Single(exec, "INSERT");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(insert.CommandText, @"::VECTOR\("));
        Assert.Equal(2, insert.Parameters.Count);
    }

    [Fact]
    public async Task BatchCreate_EachRowGetsItsOwnDimension()
    {
        var (context, exec) = Snowflake();
        await using var _ = context;

        await new TableGateway<Row, int>(context).BatchCreateAsync(new[]
        {
            new Row { Id = 1, Vf = new[] { 1f, 2f }, Vd = new[] { 1.0 }, Vi = new[] { 1 } },
            new Row { Id = 2, Vf = new[] { 1f, 2f, 3f }, Vd = new[] { 1.0 }, Vi = new[] { 1 } }
        });

        var insert = Single(exec, "INSERT");
        Assert.Contains("::VECTOR(FLOAT, 2)", insert.CommandText);
        Assert.Contains("::VECTOR(FLOAT, 3)", insert.CommandText);
    }

    [Fact]
    public async Task Update_CastsToTheDimension()
    {
        var (context, exec) = Snowflake();
        await using var _ = context;
        exec.NonQueryResults.Enqueue(1);

        await new TableGateway<Row, int>(context).UpdateAsync(new Row { Id = 1, Vf = new[] { 4f, 5f, 6f }, Vd = new[] { 1.0 }, Vi = new[] { 2 } });

        Assert.Contains("::VECTOR(FLOAT, 3)", Single(exec, "UPDATE").CommandText);
    }

    [Fact]
    public async Task Retrieve_ReadsTheJsonText()
    {
        var (context, _) = Snowflake(new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = 1, ["vf"] = "[1.000000,2.000000,3.500000]", ["vd"] = "[0.250000]", ["vi"] = "[7,-8]"
            }
        });
        await using var _c = context;

        var row = (await new TableGateway<Row, int>(context).RetrieveOneAsync(1))!;

        Assert.Equal(new[] { 1f, 2f, 3.5f }, row.Vf);
        Assert.Equal(new[] { 0.25 }, row.Vd);
        Assert.Equal(new[] { 7, -8 }, row.Vi);
    }
}
