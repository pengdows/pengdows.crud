#region

using System;
using System.Data;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ColumnInfoTests
{
    [Fact]
    public void MakeParameterValueFromField_ReturnsInt()
    {
        var obj = new Sample { IntValue = 42 };
        var column = new ColumnInfo
        {
            Name = "IntValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.IntValue))!,
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
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.StringValue))!,
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
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue))!,
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
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue))!,
            DbType = DbType.Int32,
            EnumType = typeof(TestEnum),
            EnumUnderlyingType = Enum.GetUnderlyingType(typeof(TestEnum))
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
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.StringValue))!,
            DbType = DbType.String
        };

        var result = column.MakeParameterValueFromField(obj);
        Assert.Null(result);
    }

    [Fact]
    public void MakeParameterValueFromField_EnumToString_CachesSameEnumValue()
    {
        // TDD: Verify that the same enum value returns a cached string instance
        var obj1 = new Sample { EnumValue = TestEnum.Second };
        var obj2 = new Sample { EnumValue = TestEnum.Second };

        var column = new ColumnInfo
        {
            Name = "EnumValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue))!,
            DbType = DbType.String,
            EnumType = typeof(TestEnum)
        };

        var result1 = column.MakeParameterValueFromField(obj1);
        var result2 = column.MakeParameterValueFromField(obj2);

        // Both should be "Second"
        Assert.Equal("Second", result1);
        Assert.Equal("Second", result2);

        // Should be the exact same cached instance (reference equality)
        Assert.True(ReferenceEquals(result1, result2),
            "Expected same enum value to return cached string instance");
    }

    [Fact]
    public void MakeParameterValueFromField_EnumToString_CachesDifferentEnumValuesSeparately()
    {
        // TDD: Verify different enum values cache separately
        var obj1 = new Sample { EnumValue = TestEnum.First };
        var obj2 = new Sample { EnumValue = TestEnum.Second };

        var column = new ColumnInfo
        {
            Name = "EnumValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue))!,
            DbType = DbType.String,
            EnumType = typeof(TestEnum)
        };

        var result1 = column.MakeParameterValueFromField(obj1);
        var result2 = column.MakeParameterValueFromField(obj2);

        // Should be different values
        Assert.Equal("First", result1);
        Assert.Equal("Second", result2);
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void MakeParameterValueFromField_EnumToString_CacheWorksAcrossColumnInfoInstances()
    {
        // TDD: Verify cache is shared across different ColumnInfo instances
        var obj = new Sample { EnumValue = TestEnum.Second };

        var column1 = new ColumnInfo
        {
            Name = "EnumValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue))!,
            DbType = DbType.String,
            EnumType = typeof(TestEnum)
        };

        var column2 = new ColumnInfo
        {
            Name = "EnumValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue))!,
            DbType = DbType.String,
            EnumType = typeof(TestEnum)
        };

        var result1 = column1.MakeParameterValueFromField(obj);
        var result2 = column2.MakeParameterValueFromField(obj);

        // Both should return the same cached instance
        Assert.Equal("Second", result1);
        Assert.Equal("Second", result2);
        Assert.True(ReferenceEquals(result1, result2),
            "Expected cache to be shared across ColumnInfo instances");
    }

    [Fact]
    public void MakeParameterValueFromField_EnumToString_DifferentEnumTypesCacheSeparately()
    {
        // TDD: Verify different enum types maintain separate caches
        var obj1 = new Sample { EnumValue = TestEnum.First };
        var obj2 = new Sample2 { StatusValue = StatusEnum.Active };

        var column1 = new ColumnInfo
        {
            Name = "EnumValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue))!,
            DbType = DbType.String,
            EnumType = typeof(TestEnum)
        };

        var column2 = new ColumnInfo
        {
            Name = "StatusValue",
            PropertyInfo = typeof(Sample2).GetProperty(nameof(Sample2.StatusValue))!,
            DbType = DbType.String,
            EnumType = typeof(StatusEnum)
        };

        var result1 = column1.MakeParameterValueFromField(obj1);
        var result2 = column2.MakeParameterValueFromField(obj2);

        // Different enum types should work correctly
        Assert.Equal("First", result1);
        Assert.Equal("Active", result2);
    }

    [Fact]
    public void MakeParameterValueFromField_EnumToInt_DoesNotUseStringCache()
    {
        // TDD: Verify numeric enum storage doesn't use string cache
        var obj = new Sample { EnumValue = TestEnum.Second };
        var column = new ColumnInfo
        {
            Name = "EnumValue",
            PropertyInfo = typeof(Sample).GetProperty(nameof(Sample.EnumValue))!,
            DbType = DbType.Int32,
            EnumType = typeof(TestEnum),
            EnumUnderlyingType = Enum.GetUnderlyingType(typeof(TestEnum))
        };

        var result = column.MakeParameterValueFromField(obj);

        // Should return the integer value, not a string
        Assert.IsType<int>(result);
        Assert.Equal(2, result);
    }

    private enum TestEnum
    {
        None = 0,
        First = 1,
        Second = 2
    }

    private enum StatusEnum
    {
        Active = 1,
        Inactive = 2
    }

    private class Sample
    {
        public int IntValue { get; set; }
        public string? StringValue { get; set; }
        public TestEnum EnumValue { get; set; }
    }

    private class Sample2
    {
        public StatusEnum StatusValue { get; set; }
    }
}