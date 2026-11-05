using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// The store must find a WHERE parameter whichever marker style the emulated product uses: @name
/// (SQL Server, MySQL, PostgreSQL's named form), :name (Oracle, Snowflake), $name (DuckDB), and a bare
/// positional ? (SAP HANA, Access, Informix). Without this, a row written by one command can't be
/// read back by the next one on those products, and round-trip tests report "row not found".
/// </summary>
public class FakeDataStoreMarkerTests
{
    private static fakeDbConnection MakeConnection()
    {
        var store = new FakeDataStore();
        var conn = new fakeDbConnection(store) { EnableDataPersistence = true };
        conn.Open();
        return conn;
    }

    private static async Task InsertWidgetAsync(fakeDbConnection conn, int id, string label)
    {
        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO widgets (id, label) VALUES (@id, @label)";
        var pid = insert.CreateParameter();
        pid.ParameterName = "@id";
        pid.Value = id;
        insert.Parameters.Add(pid);
        var pl = insert.CreateParameter();
        pl.ParameterName = "@label";
        pl.Value = label;
        insert.Parameters.Add(pl);
        await insert.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Where_NamedAtMarker_FindsTheRow()
    {
        using var conn = MakeConnection();
        await InsertWidgetAsync(conn, 1, "one");

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT label FROM widgets WHERE id = @k0";
        var p = select.CreateParameter();
        p.ParameterName = "@k0";
        p.Value = 1;
        select.Parameters.Add(p);
        using var reader = await select.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("one", reader["label"]?.ToString());
    }

    [Fact]
    public async Task Where_ColonMarker_OracleStyle_FindsTheRow()
    {
        using var conn = MakeConnection();
        await InsertWidgetAsync(conn, 1, "one");

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT label FROM widgets WHERE id = :p0";
        var p = select.CreateParameter();
        p.ParameterName = "p0";
        p.Value = 1;
        select.Parameters.Add(p);
        using var reader = await select.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("one", reader["label"]?.ToString());
    }

    [Fact]
    public async Task Where_DollarMarker_DuckDbStyle_FindsTheRow()
    {
        using var conn = MakeConnection();
        await InsertWidgetAsync(conn, 1, "one");

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT label FROM widgets WHERE id = $p0";
        var p = select.CreateParameter();
        p.ParameterName = "p0";
        p.Value = 1;
        select.Parameters.Add(p);
        using var reader = await select.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("one", reader["label"]?.ToString());
    }

    [Fact]
    public async Task Where_PositionalQuestionMark_FindsTheRow()
    {
        using var conn = MakeConnection();
        await InsertWidgetAsync(conn, 1, "one");
        await InsertWidgetAsync(conn, 2, "two");

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT label FROM widgets WHERE id = ?";
        var p = select.CreateParameter();
        p.ParameterName = "p0";
        p.Value = 2;
        select.Parameters.Add(p);
        using var reader = await select.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("two", reader["label"]?.ToString());
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Where_TwoPositionalMarkers_BindInOrder()
    {
        using var conn = MakeConnection();
        await InsertWidgetAsync(conn, 1, "one");
        await InsertWidgetAsync(conn, 2, "two");

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT label FROM widgets WHERE id = ? AND label = ?";
        var a = select.CreateParameter();
        a.ParameterName = "p0";
        a.Value = 2;
        select.Parameters.Add(a);
        var b = select.CreateParameter();
        b.ParameterName = "p1";
        b.Value = "two";
        select.Parameters.Add(b);
        using var reader = await select.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("two", reader["label"]?.ToString());
    }
}
