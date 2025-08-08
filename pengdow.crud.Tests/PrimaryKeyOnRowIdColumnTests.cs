#region

using System.Data;
using pengdow.crud.attributes;
using pengdow.crud.exceptions;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class PrimaryKeyOnRowIdColumnTests
{
    [Fact]
    public void TestShouldThrowException()
    {
        var tmr = new TypeMapRegistry();
        var ex = Assert.Throws<PrimaryKeyOnRowIdColumn>(() => tmr.Register<TestClass>());

        Assert.Equal(ex.GetBaseException().Message, ex.Message);
    }

    [Fact]
    public void TestMessageShouldHaveGivenMessage()
    {
        var testSubject = new PrimaryKeyOnRowIdColumn("test message");
        Assert.True(testSubject.Message == "test message");
    }

    [Table("test_table")]
    private class TestClass
    {
        [PrimaryKey]
        [Id]
        [Column("column_name", DbType.String)]
        public string ColumnName { get; set; }
    }
}