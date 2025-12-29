#region

using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ColumnAttributeTests
{
    [Fact]
    public void TestConstructor()
    {
        var testSubject = new ColumnAttribute("test_column", DbType.DateTime, 5);
        Assert.Equal("test_column", testSubject.Name);
        Assert.Equal(DbType.DateTime, testSubject.Type);
        Assert.Equal(5, testSubject.Ordinal);
    }

    [Fact]
    public void TestAnnotationImplementation()
    {
        var tmr = new TypeMapRegistry();
        tmr.Register<TestClass>();
        var tableInfo = tmr.GetTableInfo<TestClass>();
        var colVals = tableInfo.Columns.Values;
        var column = colVals.FirstOrDefault();
        Assert.NotNull(column);
        Assert.Equal("column_name", column.Name);
    }

    [Fact]
    public void Constructor_DefaultOrdinalIsZero()
    {
        var attr = new ColumnAttribute("col", DbType.Int32);
        Assert.Equal(0, attr.Ordinal);
    }

    [Table("test_table")]
    private class TestClass
    {
        [Id]
        [Column("column_name", DbType.String)]
        public string ColumnName { get; set; } = string.Empty;
    }
}
