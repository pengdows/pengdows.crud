#region

using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class UtilsTests
{
    public static IEnumerable<object[]> ZeroValues => new List<object[]>
    {
        new object[] { (byte)0 },
        new object[] { (sbyte)0 },
        new object[] { (short)0 },
        new object[] { (ushort)0 },
        new object[] { 0 },
        new object[] { 0u },
        new object[] { 0L },
        new object[] { 0UL },
        new object[] { 0f },
        new object[] { 0d },
        new object[] { 0m }
    };

    [Fact]
    public void IsNullOrDbNull_ReturnsTrueForNull()
    {
        Assert.True(Utils.IsNullOrDbNull(null));
        Assert.True(Utils.IsNullOrDbNull(DBNull.Value));
    }

    [Fact]
    public void IsZeroNumeric_ReturnsTrueForZero()
    {
        Assert.True(Utils.IsZeroNumeric(0));
        Assert.True(Utils.IsZeroNumeric(0.0));
        Assert.False(Utils.IsZeroNumeric(1));
    }

    public static TAttribute GetAttributeFromProperty<TAttribute>(
        Type containerType,
        string nestedClassName,
        string propertyName
    ) where TAttribute : Attribute
    {
        var nestedType = containerType.GetNestedType(nestedClassName, BindingFlags.NonPublic | BindingFlags.Public);
        if (nestedType == null)
        {
            throw new ArgumentException($"Nested class '{nestedClassName}' not found in '{containerType.Name}'.");
        }

        var propInfo = nestedType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (propInfo == null)
        {
            throw new ArgumentException($"Property '{propertyName}' not found in '{nestedClassName}'.");
        }

        var attr = propInfo.GetCustomAttribute<TAttribute>();
        if (attr == null)
        {
            throw new ArgumentException(
                $"Attribute '{typeof(TAttribute).Name}' not found on property '{propertyName}'.");
        }

        return attr;
    }

    [Fact]
    public void IsNullOrDbNull_ReturnsTrue_ForNull()
    {
        Assert.True(Utils.IsNullOrDbNull(null));
    }

    [Fact]
    public void IsNullOrDbNull_ReturnsTrue_ForDbNull()
    {
        Assert.True(Utils.IsNullOrDbNull(DBNull.Value));
    }

    [Fact]
    public void IsNullOrDbNull_ReturnsFalse_ForValue()
    {
        Assert.False(Utils.IsNullOrDbNull("not null"));
    }

    [Theory]
    [MemberData(nameof(ZeroValues))]
    public void IsZeroNumeric_ReturnsTrue_ForZeroNumbers(object value)
    {
        Assert.True(Utils.IsZeroNumeric(value));
    }


    [Theory]
    [InlineData((byte)1)]
    [InlineData("text")]
    [InlineData(null)]
    [InlineData(true)]
    public void IsZeroNumeric_ReturnsFalse_ForNonZeroOrInvalid(object? value)
    {
        Assert.False(Utils.IsZeroNumeric(value!));
    }

    [Fact]
    public void IsNullOrEmpty_ReturnsTrue_ForNullCollection()
    {
        Assert.True(Utils.IsNullOrEmpty<string>(null));
    }

    [Fact]
    public void IsNullOrEmpty_ReturnsTrue_ForEmptyList()
    {
        var empty = new List<int>();
        Assert.True(Utils.IsNullOrEmpty(empty));
    }

    [Fact]
    public void IsNullOrEmpty_ReturnsFalse_ForPopulatedList()
    {
        var list = new List<string> { "item" };
        Assert.False(Utils.IsNullOrEmpty(list));
    }

    [Fact]
    public void IsNullOrEmpty_ReturnsTrue_ForEmptyEnumerable()
    {
        IEnumerable<int> GetEmpty()
        {
            yield break;
        }

        Assert.True(Utils.IsNullOrEmpty(GetEmpty()));
    }

    [Fact]
    public void IsNullOrEmpty_ReturnsFalse_ForPopulatedEnumerable()
    {
        IEnumerable<int> GetItems()
        {
            yield return 1;
        }

        Assert.False(Utils.IsNullOrEmpty(GetItems()));
    }
}
