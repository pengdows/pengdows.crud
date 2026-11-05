using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// The store must read the statement shapes the library emits for each product, not only plain
/// VALUES lists. Server-side conversion of a bound value (SQL Server's CASE WHEN @p IS NULL ... geometry::
/// STGeomFromWKB(...), Snowflake's PARSE_JSON(:p)::VECTOR(...), SAP HANA's ST_GeomFromEWKB(?)) is the
/// identity in fakeDb: the stored value is the value that was bound, and reads return it. The live
/// integration matrix is what proves the server's own conversion is right.
/// </summary>
public class FakeDataStoreExpressionTests
{
    private static fakeDbConnection MakeConnection()
    {
        var conn = new fakeDbConnection(new FakeDataStore()) { EnableDataPersistence = true };
        conn.Open();
        return conn;
    }

    private static fakeDbParameterValue P(string name, object? value) => new(name, value);

    private readonly record struct fakeDbParameterValue(string Name, object? Value);

    private static async Task ExecAsync(fakeDbConnection conn, string sql, params fakeDbParameterValue[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var a in args)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = a.Name;
            p.Value = a.Value ?? System.DBNull.Value;
            cmd.Parameters.Add(p);
        }
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(fakeDbConnection conn, string sql, params fakeDbParameterValue[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var a in args)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = a.Name;
            p.Value = a.Value ?? System.DBNull.Value;
            cmd.Parameters.Add(p);
        }
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(reader.Read());
        return reader.IsDBNull(0) ? null : reader.GetValue(0);
    }

    [Fact]
    public async Task InsertSelectForm_WithCastSuffix_StoresTheBoundValue()
    {
        using var conn = MakeConnection();
        await ExecAsync(conn, "INSERT INTO docs (id, v) SELECT :i0, PARSE_JSON(:i1)::VECTOR(INT, 0)",
            P("i0", 1), P("i1", "[1,2]"));

        Assert.Equal("[1,2]", await ScalarAsync(conn, "SELECT v FROM docs WHERE id = :p0", P("p0", 1)));
    }

    [Fact]
    public async Task ValuesTuple_WithNestedParentheses_KeepsEveryValue()
    {
        using var conn = MakeConnection();
        await ExecAsync(conn, "INSERT INTO t (id, v) VALUES (@i0, CAST(@i1 AS varchar(10)))",
            P("@i0", 1), P("@i1", "abc"));

        Assert.Equal("abc", await ScalarAsync(conn, "SELECT v FROM t WHERE id = @p0", P("@p0", 1)));
    }

    [Fact]
    public async Task ConditionalWrapper_OverOneParameter_StoresThatParameter()
    {
        using var conn = MakeConnection();
        var wkb = new byte[] { 0, 0, 0, 0, 1, 2, 3 };
        // A conditional over one parameter stores that parameter (a conversion the store does not emulate stays the identity).
        await ExecAsync(conn,
            "INSERT INTO shapes (id, g) VALUES (@i0, CASE WHEN @i1 IS NULL THEN NULL ELSE " +
            "CAST(@i1 AS varbinary(max)) END)",
            P("@i0", 1), P("@i1", wkb));

        Assert.Equal(wkb, await ScalarAsync(conn, "SELECT g FROM shapes WHERE id = @p0", P("@p0", 1)));
    }

    [Fact]
    public async Task PositionalWrapper_StoresTheBoundValue()
    {
        using var conn = MakeConnection();
        var ewkb = new byte[] { 1, 2, 3 };
        await ExecAsync(conn, "INSERT INTO shapes (id, g) VALUES (?, ST_GeomFromEWKB(?))",
            P("p0", 1), P("p1", ewkb));

        Assert.Equal(ewkb, await ScalarAsync(conn, "SELECT g FROM shapes WHERE id = ?", P("p0", 1)));
    }

    [Fact]
    public async Task CastWithoutParentheses_OnAParameter_StoresTheBoundValue()
    {
        using var conn = MakeConnection();
        await ExecAsync(conn, "INSERT INTO docs (id, v) VALUES (@i0, @i1::jsonb)",
            P("@i0", 1), P("@i1", "{\"a\":1}"));

        Assert.Equal("{\"a\":1}", await ScalarAsync(conn, "SELECT v FROM docs WHERE id = @p0", P("@p0", 1)));
    }

    [Fact]
    public async Task FunctionOverColumnWithFormat_ReturnsTheStoredColumn()
    {
        using var conn = MakeConnection();
        var at = new System.DateTime(2026, 10, 5, 13, 45, 30, System.DateTimeKind.Utc);
        await ExecAsync(conn, "INSERT INTO t (id, v) VALUES (@i0, @i1)", P("@i0", 1), P("@i1", at));

        Assert.Equal(at,
            await ScalarAsync(conn,
                "SELECT TO_VARCHAR(\"a\".\"v\", 'YYYY-MM-DD HH24:MI:SS.FF7') AS \"v\" FROM t \"a\" WHERE \"a\".\"id\" = @p0",
                P("@p0", 1)));
    }

    [Fact]
    public async Task ConvertWithATypeArgument_ReturnsTheStoredColumn()
    {
        using var conn = MakeConnection();
        await ExecAsync(conn, "INSERT INTO t (id, v) VALUES (@i0, @i1)", P("@i0", 1), P("@i1", "2026-10-05 13:45:30.123456"));

        Assert.Equal("2026-10-05 13:45:30.123456",
            await ScalarAsync(conn,
                "SELECT CONVERT(VARCHAR(26), \"a\".\"v\", 140) AS \"v\" FROM t \"a\" WHERE \"a\".\"id\" = @p0",
                P("@p0", 1)));
    }

    [Fact]
    public async Task SelectListWithAFunctionCallWithACommaArgument_KeepsTheFollowingColumns()
    {
        using var conn = MakeConnection();
        await ExecAsync(conn, "INSERT INTO t (id, v) VALUES (@i0, @i1)", P("@i0", 7), P("@i1", "x"));

        using var select = conn.CreateCommand();
        select.CommandText =
            "SELECT TO_VARCHAR(\"a\".\"v\", 'YYYY-MM-DD') AS \"v\", \"a\".\"id\" FROM t \"a\" WHERE \"a\".\"id\" = @p0";
        var p = select.CreateParameter();
        p.ParameterName = "@p0";
        p.Value = 7;
        select.Parameters.Add(p);
        using var reader = await select.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("x", reader["v"]?.ToString());
        Assert.Equal(7, reader["id"]);
    }

    [Fact]
    public async Task SelectColumnThroughMethodCall_ReturnsTheStoredColumn()
    {
        using var conn = MakeConnection();
        var ewkb = new byte[] { 9, 8, 7 };
        await ExecAsync(conn, "INSERT INTO shapes (id, g) VALUES (@i0, @i1)", P("@i0", 1), P("@i1", ewkb));

        Assert.Equal(ewkb,
            await ScalarAsync(conn, "SELECT \"a\".\"g\".ST_AsEWKB() AS \"g\" FROM shapes \"a\" WHERE \"a\".\"id\" = @p0",
                P("@p0", 1)));
    }
}
