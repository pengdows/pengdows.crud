using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.@internal;

/// <summary>
/// Array and collection-literal reads from a provider column. The nullable-element column (Npgsql on Spanner refuses
/// GetValue for it) is emulated by fakeDb's <c>NullableElementArrayColumns</c>.
/// </summary>
public class ProviderValueFieldReaderTests
{
    private static fakeDbDataReader OneRow(object value, bool nullableElementArray = false)
    {
        var reader = new fakeDbDataReader(new List<Dictionary<string, object>>
        {
            new() { ["v"] = value }
        });
        if (nullableElementArray)
        {
            reader.NullableElementArrayColumns = new HashSet<string> { "v" };
        }

        Assert.True(reader.Read());
        return reader;
    }

    [Fact]
    public void ReadArray_ArrayColumn_ReturnsTheElements()
    {
        var reader = OneRow(new[] { 1, 2, 3 });

        Assert.Equal(new[] { 1, 2, 3 }, ProviderValueFieldReader.ReadArray<int>(reader, 0));
    }

    [Fact]
    public void ReadArray_NullableElementColumnWithoutNulls_ReadsThroughTheNullableArray()
    {
        var reader = OneRow(new int?[] { 4, 5, 6 }, nullableElementArray: true);

        Assert.Equal(new[] { 4, 5, 6 }, ProviderValueFieldReader.ReadArray<int>(reader, 0));
    }

    [Fact]
    public void ReadArray_NullableElementColumnWithNull_IsNotReadableAsValueElements()
    {
        var reader = OneRow(new int?[] { 1, null, 3 }, nullableElementArray: true);

        Assert.ThrowsAny<Exception>(() => ProviderValueFieldReader.ReadArray<int>(reader, 0));
    }

    [Fact]
    public void ReadArrayOrNull_NullColumn_ReturnsNull()
    {
        var reader = OneRow(DBNull.Value);

        Assert.Null(ProviderValueFieldReader.ReadArrayOrNull<int>(reader, 0));
    }

    [Fact]
    public void ReadArrayOrNull_ArrayColumn_ReturnsTheElements()
    {
        var reader = OneRow(new[] { 8, 9 });

        Assert.Equal(new[] { 8, 9 }, ProviderValueFieldReader.ReadArrayOrNull<int>(reader, 0));
    }

    [Fact]
    public void ReadArrayOrNull_NullableElementColumn_ReadsThroughTheNullableArray()
    {
        var reader = OneRow(new int?[] { 10, 11 }, nullableElementArray: true);

        Assert.Equal(new[] { 10, 11 }, ProviderValueFieldReader.ReadArrayOrNull<int>(reader, 0));
    }

    [Fact]
    public void ReadCollectionLiteral_ReturnsTheParsedElements()
    {
        var reader = OneRow("LIST{1,2,3}");

        Assert.Equal(new[] { 1, 2, 3 }, ProviderValueFieldReader.ReadCollectionLiteral<int>(reader, 0));
    }

    [Fact]
    public void ReadCollectionLiteralOrNull_NullColumn_ReturnsNull()
    {
        var reader = OneRow(DBNull.Value);

        Assert.Null(ProviderValueFieldReader.ReadCollectionLiteralOrNull<int>(reader, 0));
    }

    [Fact]
    public void ReadCollectionLiteralOrNull_TextColumn_ReturnsTheParsedElements()
    {
        var reader = OneRow("SET{4,5}");

        Assert.Equal(new[] { 4, 5 }, ProviderValueFieldReader.ReadCollectionLiteralOrNull<int>(reader, 0));
    }

    [Fact]
    public void TryReadNullableElementArray_ReferenceElementArray_ReturnsFalse()
    {
        // Only a value-type element can be a nullable-element array, so a string[] column never qualifies.
        var reader = OneRow(new[] { "a" });

        Assert.False(ProviderValueFieldReader.TryReadNullableElementArray(reader, 0, out _));
    }

    [Fact]
    public void TryReadNullableElementArray_FieldTypeReportedAsSystemArray_ReturnsFalse()
    {
        // The element type is unknown when the provider reports only System.Array (Spanner does), so there is nothing
        // to read the elements as.
        var reader = OneRow(new int?[] { 2, 3 }, nullableElementArray: true);

        Assert.False(ProviderValueFieldReader.TryReadNullableElementArray(reader, 0, out _));
    }

    [Fact]
    public void IsReadableArray_OneDimensionalValueArrays_AreReadable()
    {
        Assert.True(ProviderValueFieldReader.IsReadableArray(typeof(int[])));
        Assert.True(ProviderValueFieldReader.IsReadableArray(typeof(string[])));
    }

    [Fact]
    public void IsReadableArray_ByteCharAndMultiDimensional_AreNotReadableArrays()
    {
        Assert.False(ProviderValueFieldReader.IsReadableArray(typeof(byte[])));
        Assert.False(ProviderValueFieldReader.IsReadableArray(typeof(char[])));
        Assert.False(ProviderValueFieldReader.IsReadableArray(typeof(int[,])));
        Assert.False(ProviderValueFieldReader.IsReadableArray(typeof(int)));
    }

    [Fact]
    public void IsArrayColumn_ArrayAndSystemArray_AreArrayColumns()
    {
        Assert.True(ProviderValueFieldReader.IsArrayColumn(typeof(int[])));
        Assert.True(ProviderValueFieldReader.IsArrayColumn(typeof(Array)));
    }

    [Fact]
    public void IsArrayColumn_ByteBufferAndScalar_AreNotArrayColumns()
    {
        Assert.False(ProviderValueFieldReader.IsArrayColumn(typeof(byte[])));
        Assert.False(ProviderValueFieldReader.IsArrayColumn(typeof(char[])));
        Assert.False(ProviderValueFieldReader.IsArrayColumn(typeof(string)));
    }

    [Fact]
    public void BindArrayRead_ValueElement_ReadsThroughTheValueArrayReader()
    {
        var record = Expression.Parameter(typeof(IDataRecord), "record");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        var body = ProviderValueFieldReader.BindArrayRead(typeof(int), record, ordinal);
        var read = Expression.Lambda<Func<IDataRecord, int, int[]>>(body, record, ordinal).Compile();

        Assert.Equal(new[] { 12, 13 }, read(OneRow(new[] { 12, 13 }), 0));
    }

    [Fact]
    public void BindArrayRead_ReferenceElement_ReadsThroughTheGenericArrayReader()
    {
        var record = Expression.Parameter(typeof(IDataRecord), "record");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        var body = ProviderValueFieldReader.BindArrayRead(typeof(string), record, ordinal);
        var read = Expression.Lambda<Func<IDataRecord, int, string[]>>(body, record, ordinal).Compile();

        Assert.Equal(new[] { "a", "b" }, read(OneRow(new[] { "a", "b" }), 0));
    }

    [Fact]
    public void BindArrayReadOrNull_NullColumn_CompilesToNull()
    {
        var record = Expression.Parameter(typeof(IDataRecord), "record");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        var body = ProviderValueFieldReader.BindArrayReadOrNull(typeof(int), record, ordinal);
        var read = Expression.Lambda<Func<IDataRecord, int, int[]?>>(body, record, ordinal).Compile();

        Assert.Null(read(OneRow(DBNull.Value), 0));
        Assert.Equal(new[] { 14 }, read(OneRow(new[] { 14 }), 0));
    }
}
