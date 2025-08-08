#region

using System.Data;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class ColumnInfoTests
{
    [Fact]
    public void MakeParameterValueFromField_ReturnsInt()
    {
        var obj = new Sample { IntValue = 42 };
        var column = new ColumnInfo
        {
            Name = "IntValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.IntValue)),
            DbType = DbType.Int32
        };

        var result = column.MakeParameterValueFromField(obj);
        Assert.Equal(42, result);
    }

    [Fact]
    public void MakeParameterValueFromField_ReturnsString()
    {
        var obj = new Sample { StringValue = "hello" };
        var column = new ColumnInfo
        {
            Name = "StringValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.StringValue)),
            DbType = DbType.String
        };

        var result = column.MakeParameterValueFromField(obj);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void MakeParameterValueFromField_EnumToString()
    {
        var obj = new Sample { EnumValue = TestEnum.Second };
        var column = new ColumnInfo
        {
            Name = "EnumValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue)),
            DbType = DbType.String,
            EnumType = typeof(TestEnum)
        };

        var result = column.MakeParameterValueFromField(obj);
        Assert.Equal("Second", result);
    }

    [Fact]
    public void MakeParameterValueFromField_EnumToInt()
    {
        var obj = new Sample { EnumValue = TestEnum.Second };
        var column = new ColumnInfo
        {
            Name = "EnumValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue)),
            DbType = DbType.Int32,
            EnumType = typeof(TestEnum)
        };

        var result = column.MakeParameterValueFromField(obj);
        Assert.Equal((int)TestEnum.Second, result);
    }

    [Fact]
    public void MakeParameterValueFromField_NullValue_ReturnsNull()
    {
        var obj = new Sample { StringValue = null };
        var column = new ColumnInfo
        {
            Name = "StringValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.StringValue)),
            DbType = DbType.String
        };

        var result = column.MakeParameterValueFromField(obj);
        Assert.Null(result);
    }

    private enum TestEnum
    {
        None = 0,
        First = 1,
        Second = 2
    }

    private class Sample
    {
        public int IntValue { get; set; }
        public string StringValue { get; set; }
        public TestEnum EnumValue { get; set; }
    }
}