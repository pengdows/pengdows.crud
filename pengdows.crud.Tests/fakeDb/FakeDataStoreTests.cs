using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// Verifies FakeDataStore core operations work correctly without relying on
/// the broad catch blocks that were suppressing internal exceptions.
/// These regression tests confirm the catch blocks were dead defensive code —
/// removing them leaves all normal paths green.
/// </summary>
public class FakeDataStoreTests
{
    private static fakeDbConnection MakeConnection()
    {
        var store = new FakeDataStore();
        var conn = new fakeDbConnection(store);
        conn.EnableDataPersistence = true;
        conn.Open();
        return conn;
    }

    // ── INSERT ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Insert_ValidColumnsAndValues_ReturnsOneRowAffected()
    {
        using var conn = MakeConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO users (name) VALUES ('Alice')";

        var result = await cmd.ExecuteNonQueryAsync();

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Insert_ThenSelect_RowIsPersisted()
    {
        using var conn = MakeConnection();

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO things (name) VALUES ('hello')";
        await insertCmd.ExecuteNonQueryAsync();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM things";
        using var reader = await selectCmd.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("hello", reader["name"]?.ToString());
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Insert_WithParameter_RowIsPersisted()
    {
        using var conn = MakeConnection();

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO widgets (label) VALUES (@label)";
        var p = insertCmd.CreateParameter();
        p.ParameterName = "@label";
        p.Value = "widget-one";
        insertCmd.Parameters.Add(p);
        await insertCmd.ExecuteNonQueryAsync();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM widgets";
        using var reader = await selectCmd.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("widget-one", reader["label"]?.ToString());
    }

    // ── UPDATE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ValidSql_ReturnsAffectedRowCount()
    {
        using var conn = MakeConnection();

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO items (name) VALUES ('old')";
        await insertCmd.ExecuteNonQueryAsync();

        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE items SET name = 'new'";
        var affected = await updateCmd.ExecuteNonQueryAsync();

        Assert.True(affected >= 1);
    }

    [Fact]
    public async Task Update_WithWhereClause_UpdatesOnlyMatchingRows()
    {
        using var conn = MakeConnection();

        using var insert1 = conn.CreateCommand();
        insert1.CommandText = "INSERT INTO things (name) VALUES ('keep')";
        await insert1.ExecuteNonQueryAsync();

        using var insert2 = conn.CreateCommand();
        insert2.CommandText = "INSERT INTO things (name) VALUES ('change')";
        await insert2.ExecuteNonQueryAsync();

        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE things SET name = 'changed' WHERE name = 'change'";
        var affected = await updateCmd.ExecuteNonQueryAsync();

        Assert.Equal(1, affected);

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM things WHERE name = 'keep'";
        using var reader = await selectCmd.ExecuteReaderAsync();
        Assert.True(reader.Read()); // 'keep' row unchanged
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ValidSql_RemovesRow()
    {
        using var conn = MakeConnection();

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO things (name) VALUES ('to-delete')";
        await insertCmd.ExecuteNonQueryAsync();

        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM things WHERE name = 'to-delete'";
        var affected = await deleteCmd.ExecuteNonQueryAsync();

        Assert.Equal(1, affected);

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM things";
        using var reader = await selectCmd.ExecuteReaderAsync();
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Delete_WithoutWhere_DeletesAllRows()
    {
        using var conn = MakeConnection();

        using var insert1 = conn.CreateCommand();
        insert1.CommandText = "INSERT INTO things (name) VALUES ('a')";
        await insert1.ExecuteNonQueryAsync();

        using var insert2 = conn.CreateCommand();
        insert2.CommandText = "INSERT INTO things (name) VALUES ('b')";
        await insert2.ExecuteNonQueryAsync();

        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM things";
        var affected = await deleteCmd.ExecuteNonQueryAsync();

        Assert.Equal(2, affected);
    }

    // ── SELECT ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_SpecificColumn_ReturnsOnlyRequestedColumn()
    {
        using var conn = MakeConnection();

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO products (name, price) VALUES ('Widget', 9)";
        await insertCmd.ExecuteNonQueryAsync();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT name FROM products";
        using var reader = await selectCmd.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.NotNull(reader["name"]);
    }

    [Fact]
    public async Task Select_WithLikeWhere_ReturnsMatchingRowsOnly()
    {
        using var conn = MakeConnection();

        using var insert1 = conn.CreateCommand();
        insert1.CommandText = "INSERT INTO items (name) VALUES ('Apple')";
        await insert1.ExecuteNonQueryAsync();

        using var insert2 = conn.CreateCommand();
        insert2.CommandText = "INSERT INTO items (name) VALUES ('Banana')";
        await insert2.ExecuteNonQueryAsync();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM items WHERE name LIKE 'App%'";
        using var reader = await selectCmd.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("Apple", reader["name"]?.ToString());
        Assert.False(reader.Read()); // Banana must not appear
    }

    [Fact]
    public async Task Select_WithEqualityWhere_FiltersCorrectly()
    {
        using var conn = MakeConnection();

        using var insert1 = conn.CreateCommand();
        insert1.CommandText = "INSERT INTO colors (name) VALUES ('red')";
        await insert1.ExecuteNonQueryAsync();

        using var insert2 = conn.CreateCommand();
        insert2.CommandText = "INSERT INTO colors (name) VALUES ('blue')";
        await insert2.ExecuteNonQueryAsync();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM colors WHERE name = 'red'";
        using var reader = await selectCmd.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("red", reader["name"]?.ToString());
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Select_WithIsNullWhere_ReturnsNullRows()
    {
        using var conn = MakeConnection();

        using var insert1 = conn.CreateCommand();
        insert1.CommandText = "INSERT INTO things (name) VALUES (@n)";
        var p1 = insert1.CreateParameter();
        p1.ParameterName = "@n";
        p1.Value = null;
        insert1.Parameters.Add(p1);
        await insert1.ExecuteNonQueryAsync();

        using var insert2 = conn.CreateCommand();
        insert2.CommandText = "INSERT INTO things (name) VALUES ('not-null')";
        await insert2.ExecuteNonQueryAsync();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM things WHERE name IS NULL";
        using var reader = await selectCmd.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.False(reader.Read()); // only one null row
    }

    // ── IN clause ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_WithInClause_ReturnsOnlyMatchingRows()
    {
        using var conn = MakeConnection();

        using var i1 = conn.CreateCommand();
        i1.CommandText = "INSERT INTO colors (name) VALUES ('red')";
        await i1.ExecuteNonQueryAsync();

        using var i2 = conn.CreateCommand();
        i2.CommandText = "INSERT INTO colors (name) VALUES ('blue')";
        await i2.ExecuteNonQueryAsync();

        using var i3 = conn.CreateCommand();
        i3.CommandText = "INSERT INTO colors (name) VALUES ('green')";
        await i3.ExecuteNonQueryAsync();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM colors WHERE name IN (@p0, @p1)";
        var pa = selectCmd.CreateParameter();
        pa.ParameterName = "@p0";
        pa.Value = "red";
        selectCmd.Parameters.Add(pa);
        var pb = selectCmd.CreateParameter();
        pb.ParameterName = "@p1";
        pb.Value = "green";
        selectCmd.Parameters.Add(pb);
        using var reader = await selectCmd.ExecuteReaderAsync();

        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader["name"]?.ToString() ?? "");
        }

        Assert.Equal(2, rows.Count);
        Assert.Contains("red", rows);
        Assert.Contains("green", rows);
        Assert.DoesNotContain("blue", rows);
    }

    [Fact]
    public async Task Select_WithInClause_SingleParam_ReturnsMatchingRow()
    {
        using var conn = MakeConnection();

        using var i1 = conn.CreateCommand();
        i1.CommandText = "INSERT INTO items (name) VALUES ('alpha')";
        await i1.ExecuteNonQueryAsync();

        using var i2 = conn.CreateCommand();
        i2.CommandText = "INSERT INTO items (name) VALUES ('beta')";
        await i2.ExecuteNonQueryAsync();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM items WHERE name IN (@w0)";
        var p = selectCmd.CreateParameter();
        p.ParameterName = "@w0";
        p.Value = "alpha";
        selectCmd.Parameters.Add(p);
        using var reader = await selectCmd.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("alpha", reader["name"]?.ToString());
        Assert.False(reader.Read()); // beta must not appear
    }

    // ── Comparison operators ───────────────────────────────────────────────────

    [Fact]
    public async Task Select_WithGreaterThanOrEqual_FiltersCorrectly()
    {
        using var conn = MakeConnection();

        for (var i = 1; i <= 5; i++)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT INTO nums (val) VALUES (@val)";
            var p = ins.CreateParameter();
            p.ParameterName = "@val";
            p.Value = i;
            ins.Parameters.Add(p);
            await ins.ExecuteNonQueryAsync();
        }

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM nums WHERE val >= 3";
        using var reader = await selectCmd.ExecuteReaderAsync();

        var rows = new List<object?>();
        while (reader.Read())
        {
            rows.Add(reader["val"]);
        }

        Assert.Equal(3, rows.Count); // 3, 4, 5
    }

    [Fact]
    public async Task Select_WithNotEqual_FiltersCorrectly()
    {
        using var conn = MakeConnection();

        using var i1 = conn.CreateCommand();
        i1.CommandText = "INSERT INTO colors (name) VALUES ('red')";
        await i1.ExecuteNonQueryAsync();

        using var i2 = conn.CreateCommand();
        i2.CommandText = "INSERT INTO colors (name) VALUES ('blue')";
        await i2.ExecuteNonQueryAsync();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM colors WHERE name != 'red'";
        using var reader = await selectCmd.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal("blue", reader["name"]?.ToString());
        Assert.False(reader.Read());
    }

    // ── Unrecognized predicate ─────────────────────────────────────────────────

    [Fact]
    public async Task Select_WithUnsupportedPredicate_ThrowsNotSupportedException()
    {
        using var conn = MakeConnection();

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO things (name) VALUES ('x')";
        await insertCmd.ExecuteNonQueryAsync();

        // BETWEEN is not supported; verify it throws rather than silently matching everything
        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM things WHERE name BETWEEN 'a' AND 'z'";

        await Assert.ThrowsAsync<NotSupportedException>(
            () => selectCmd.ExecuteReaderAsync());
    }
}
