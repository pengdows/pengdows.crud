using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("TypeRegistry")]
public sealed class CoveragePush_TypeCoercionAndCompiledMapperTests
{
    private enum MapperEnum
    {
        One = 1
    }

    private sealed class MapperEntity
    {
        public int Value { get; set; }
    }

    private static readonly MethodInfo GetReaderMethodMethod =
        typeof(CompiledMapperFactory<MapperEntity>).GetMethod("GetReaderMethod",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ResolveCoercerTypePairMethod =
        typeof(TypeCoercionHelper).GetMethod("ResolveCoercer",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(Type), typeof(Type), typeof(EnumParseFailureMode) },
            null)!;

    [Theory]
    [InlineData(typeof(decimal), nameof(IDataRecord.GetDecimal))]
    [InlineData(typeof(bool), nameof(IDataRecord.GetBoolean))]
    [InlineData(typeof(short), nameof(IDataRecord.GetInt16))]
    [InlineData(typeof(byte), nameof(IDataRecord.GetByte))]
    [InlineData(typeof(double), nameof(IDataRecord.GetDouble))]
    [InlineData(typeof(float), nameof(IDataRecord.GetFloat))]
    [InlineData(typeof(Guid), nameof(IDataRecord.GetGuid))]
    [InlineData(typeof(TimeSpan), nameof(IDataRecord.GetValue))]
    public void CompiledMapperFactory_GetReaderMethod_CoversTypeSwitch(Type fieldType, string expectedMethod)
    {
        var resolvedMethod = (MethodInfo)GetReaderMethodMethod.Invoke(null, new object[] { fieldType })!;

        Assert.Equal(expectedMethod, resolvedMethod.Name);
    }

    [Fact]
    public void EnumMappingCache_ValidAndInvalidNumericValues_AreHandled()
    {
        var valid = EnumMappingCache.ValidateEnumValue(MapperEnum.One);
        Assert.Equal(MapperEnum.One, valid);

        Assert.Throws<ArgumentException>(() => EnumMappingCache.ValidateEnumValue((MapperEnum)77));
    }

    // COR-002: blank text is not a number, Guid, date or flag. It read as 0/Guid.Empty/default/false
    // with no error (the silent wrong value TYPE-008 forbids); it fails instead, nullable targets too
    // (an empty string is not NULL).
    [Theory]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(double))]
    [InlineData(typeof(float))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(short))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(int?))]
    [InlineData(typeof(Guid?))]
    public void Coerce_BlankString_IntoANonStringType_Throws(Type target)
    {
        Assert.Throws<FormatException>(() => TypeCoercionHelper.Coerce(" ", typeof(string), target));
        Assert.Throws<FormatException>(() => TypeCoercionHelper.Coerce("", typeof(string), target));
    }

    [Fact]
    public void CoerceDateTimeFromString_CoversWhitespaceParseAndFailureBranches()
    {
        Assert.Throws<InvalidCastException>(() => TypeCoercionHelper.CoerceDateTimeFromString("   "));
        Assert.Throws<InvalidCastException>(() => TypeCoercionHelper.CoerceDateTimeFromString("not-a-date"));

        var parsed = TypeCoercionHelper.CoerceDateTimeFromString("2026-01-02T03:04:05");
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }

    [Fact]
    public void ResolveCoercer_TypePair_AssignableIdentityBranch_IsCovered()
    {
        var coercer = (Func<object?, object?>)ResolveCoercerTypePairMethod.Invoke(null, new object?[]
        {
            typeof(string),
            typeof(string),
            EnumParseFailureMode.Throw
        })!;

        var value = coercer("identity");
        Assert.Equal("identity", value);
    }

    [Fact]
    public void ReadBytes_LargeBinary_UsesHeapBufferPath()
    {
        var data = new byte[300];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        using var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["payload"] = data }
        });
        Assert.True(reader.Read());

        var bytes = TypeCoercionHelper.ReadBytes(reader, 0);
        Assert.Equal(data, bytes);
    }

    [Fact]
    public void ReadGuidFromBytes_ShortBuffer_ThrowsInvalidValueException()
    {
        using var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["gid"] = new byte[8] }
        });
        Assert.True(reader.Read());

        Assert.Throws<InvalidValueException>(() => TypeCoercionHelper.ReadGuidFromBytes(reader, 0));
    }
}
