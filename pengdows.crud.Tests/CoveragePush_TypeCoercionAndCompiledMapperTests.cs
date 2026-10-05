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
            new[] { typeof(Type), typeof(Type), typeof(EnumParseFailureMode), typeof(TypeCoercionOptions) },
            null)!;

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

}
