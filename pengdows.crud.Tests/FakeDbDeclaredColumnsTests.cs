using System;
using System.Collections.Generic;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// fakeDb reports a result's declared columns the way real providers do: FieldCount, GetName,
/// GetOrdinal, GetFieldType and GetDataTypeName from the declared columns, also when there are no
/// rows (a "SELECT cols FROM t WHERE 1 = 0" metadata probe).
/// </summary>
public class FakeDbDeclaredColumnsTests
{
    private static readonly fakeDbColumn[] Columns =
    {
        new("id", typeof(int), "INTEGER"),
        new("doc", typeof(byte[]), "BLOB")
    };

    [Fact]
    public void EmptyResult_ReportsTheDeclaredColumns()
    {
        var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>()) { Columns = Columns };

        Assert.False(reader.Read());
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("doc", reader.GetName(1));
        Assert.Equal(1, reader.GetOrdinal("doc"));
        Assert.Equal(typeof(byte[]), reader.GetFieldType(1));
        Assert.Equal("BLOB", reader.GetDataTypeName(1));
    }

    [Fact]
    public void Rows_UseTheDeclaredTypeNames()
    {
        var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["id"] = 1, ["doc"] = new byte[] { 1 } } })
        {
            Columns = Columns
        };

        Assert.True(reader.Read());
        Assert.Equal("INTEGER", reader.GetDataTypeName(0));
        Assert.Equal("BLOB", reader.GetDataTypeName(1));
        Assert.Equal(new byte[] { 1 }, reader.GetValue(1));
    }

    // A real provider's schema table carries a temporal column's scale (SqlClient, ODP.NET), which the
    // gateways read to truncate values to it (DRY-028).
    [Fact]
    public void GetSchemaTable_ReportsTheDeclaredColumns_WithPrecisionAndScale()
    {
        var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>())
        {
            Columns = new[]
            {
                new fakeDbColumn("id", typeof(int), "INTEGER"),
                new fakeDbColumn("at", typeof(DateTime), "datetime2") { NumericScale = 3 },
                new fakeDbColumn("span", typeof(TimeSpan), "IntervalDS") { NumericPrecision = 9, NumericScale = 6 }
            }
        };

        var table = reader.GetSchemaTable();

        Assert.NotNull(table);
        Assert.Equal(3, table!.Rows.Count);
        Assert.Equal("at", table.Rows[1]["ColumnName"]);
        Assert.Equal(1, table.Rows[1]["ColumnOrdinal"]);
        Assert.Equal(typeof(DateTime), table.Rows[1]["DataType"]);
        Assert.Equal("datetime2", table.Rows[1]["DataTypeName"]);
        Assert.Equal((short)3, table.Rows[1]["NumericScale"]);
        Assert.Equal(DBNull.Value, table.Rows[0]["NumericScale"]);
        Assert.Equal((short)9, table.Rows[2]["NumericPrecision"]);
    }

    [Fact]
    public void GetSchemaTable_WithoutDeclaredColumns_IsNull()
    {
        Assert.Null(new fakeDbDataReader(Array.Empty<Dictionary<string, object>>()).GetSchemaTable());
    }
}
