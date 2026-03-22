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

    [Fact]
    public void IsNullOrEmpty_UsesGenericCollectionBranch()
    {
        var collection = new GenericOnlyCollection<int>(new[] { 1, 2 });
        Assert.False(Utils.IsNullOrEmpty(collection));

        var empty = new GenericOnlyCollection<int>(Array.Empty<int>());
        Assert.True(Utils.IsNullOrEmpty(empty));
    }

    [Fact]
    public void IsNullOrEmpty_UsesReadOnlyCollectionBranch()
    {
        var ro = new ReadOnlyOnlyCollection<int>(new[] { 7 });
        Assert.False(Utils.IsNullOrEmpty(ro));

        var empty = new ReadOnlyOnlyCollection<int>(Array.Empty<int>());
        Assert.True(Utils.IsNullOrEmpty(empty));
    }

    [Fact]
    public void IsNullOrDbNull_Generic_ReturnsTrue_ForNull()
    {
        string? value = null;
        Assert.True(Utils.IsNullOrDbNull<string?>(value));
    }

    [Fact]
    public void IsNullOrDbNull_Generic_ReturnsTrue_ForDbNull()
    {
        Assert.True(Utils.IsNullOrDbNull<DBNull>(DBNull.Value));
    }

    [Fact]
    public void IsNullOrDbNull_Generic_ReturnsFalse_ForValue()
    {
        Assert.False(Utils.IsNullOrDbNull("value"));
    }

    private sealed class GenericOnlyCollection<T> : ICollection<T>
    {
        private readonly List<T> _items;

        public GenericOnlyCollection(IEnumerable<T> items)
        {
            _items = new List<T>(items);
        }

        public int Count => _items.Count;
        public bool IsReadOnly => true;
        public void Add(T item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(T item) => _items.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public bool Remove(T item) => throw new NotSupportedException();
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }

    private sealed class ReadOnlyOnlyCollection<T> : IReadOnlyCollection<T>
    {
        private readonly List<T> _items;

        public ReadOnlyOnlyCollection(IEnumerable<T> items)
        {
            _items = new List<T>(items);
        }

        public int Count => _items.Count;
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
