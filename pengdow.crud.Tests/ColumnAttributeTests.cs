#region

using System.Data;
using System.Linq;
using pengdow.crud.attributes;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class ColumnAttributeTests
{
    [Fact]
    public void TestConstructor()
    {
        var testSubject = new ColumnAttribute("test_column", DbType.DateTime);
        Assert.Equal("test_column", testSubject.Name);
        Assert.Equal(DbType.DateTime, testSubject.Type);
    }

    [Fact]
    public void TestAnnotationImplementation()
    {
        var tmr = new TypeMapRegistry();
        tmr.Register<TestClass>();
        var tableInfo = tmr.GetTableInfo<TestClass>();
        var colVals = tableInfo.Columns.Values;
        Assert.Equal("column_name", colVals.FirstOrDefault().Name);
    }

    [Table("test_table")]
    private class TestClass
    {
        [Id]
        [Column("column_name", DbType.String)]
        public string ColumnName { get; set; }
    }
}