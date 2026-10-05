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
}
