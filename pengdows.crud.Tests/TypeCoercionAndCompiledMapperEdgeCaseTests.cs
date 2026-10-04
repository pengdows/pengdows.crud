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
public sealed class TypeCoercionAndCompiledMapperEdgeCaseTests
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

    // ADO.NET lets GetBytes return fewer bytes than requested (streaming providers do), so
    // ReadBytes must keep reading until it has the whole value, not trust one call.
    [Theory]
    [InlineData(100)]  // a small value (once read through a pooled buffer)
    [InlineData(1000)]
    public void ReadBytes_ProviderReturnsPartialChunks_ReadsWholeValue(int size)
    {
        var data = new byte[size];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251 + 1);
        }

        using var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["payload"] = data }
        })
        {
            MaxBytesPerGetBytesCall = 7
        };
        Assert.True(reader.Read());

        Assert.Equal(data, TypeCoercionHelper.ReadBytes(reader, 0));
    }

    // COR-014: COR-003 asked for a 17th byte to prove a value held exactly 16; MySql.Data throws
    // IndexOutOfRangeException for a read at the end of a value (MySqlConnector, Npgsql, SqlClient and
    // Microsoft.Data.Sqlite return 0, probed live 2026-10-04), so every BINARY(16) Guid on it failed.
    [Fact]
    public void ReadGuidFromBytes_ProviderThrowsReadingPastTheEnd_ReadsTheGuid()
    {
        var guid = Guid.NewGuid();
        using var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["gid"] = guid.ToByteArray() }
        })
        {
            ThrowsReadingPastEnd = true
        };
        Assert.True(reader.Read());

        Assert.Equal(guid, TypeCoercionHelper.ReadGuidFromBytes(reader, 0));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(17)]
    public void ReadGuidFromBytes_ProviderThrowsReadingPastTheEnd_WrongLengthStillFails(int size)
    {
        using var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["gid"] = new byte[size] }
        })
        {
            ThrowsReadingPastEnd = true
        };
        Assert.True(reader.Read());

        Assert.Throws<InvalidValueException>(() => TypeCoercionHelper.ReadGuidFromBytes(reader, 0));
    }

    [Fact]
    public void FakeReader_ThrowsReadingPastEnd_ThrowsOnlyForAReadAtTheEnd()
    {
        using var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["payload"] = new byte[4] }
        })
        {
            ThrowsReadingPastEnd = true
        };
        Assert.True(reader.Read());
        var buffer = new byte[8];

        Assert.Equal(4, reader.GetBytes(0, 0, null, 0, 0));
        Assert.Equal(4, reader.GetBytes(0, 0, buffer, 0, 8));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetBytes(0, 4, buffer, 4, 1));
    }

    [Fact]
    public void ReadGuidFromBytes_ProviderReturnsPartialChunks_ReadsWholeGuid()
    {
        var guid = Guid.NewGuid();
        using var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["gid"] = guid.ToByteArray() }
        })
        {
            MaxBytesPerGetBytesCall = 5
        };
        Assert.True(reader.Read());

        Assert.Equal(guid, TypeCoercionHelper.ReadGuidFromBytes(reader, 0));
    }

    [Fact]
    public void FakeReader_MaxBytesPerGetBytesCall_CapsEachCall()
    {
        using var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["payload"] = new byte[20] }
        })
        {
            MaxBytesPerGetBytesCall = 7
        };
        Assert.True(reader.Read());

        Assert.Equal(20, reader.GetBytes(0, 0, null, 0, 0));
        Assert.Equal(7, reader.GetBytes(0, 0, new byte[20], 0, 20));
        Assert.Equal(6, reader.GetBytes(0, 14, new byte[20], 0, 20));
    }

    // COR-003: a value longer than 16 bytes was read as its first 16 bytes, a silently wrong Guid.
    [Fact]
    public void ReadGuidFromBytes_LongerThan16Bytes_ThrowsInvalidValueException()
    {
        using var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["gid"] = new byte[20] }
        });
        Assert.True(reader.Read());

        Assert.Throws<InvalidValueException>(() => TypeCoercionHelper.ReadGuidFromBytes(reader, 0));
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

    // COR-011: the coercer DataReaderMapper resolves per column converted DateTime/DateTimeOffset
    // with the default options, ignoring the TimePolicy Coerce honors for the same options.
    [Fact]
    public void ResolveCoercer_DateTimeIntoDateTimeOffset_HonorsTheTimePolicy()
    {
        var forceUtc = TypeCoercionOptions.Default with { TimePolicy = TimeMappingPolicy.ForceUtcDateTime };
        var local = new DateTime(2026, 10, 4, 7, 30, 0, DateTimeKind.Local);

        var coercer = TypeCoercionHelper.ResolveCoercer(typeof(DateTime), typeof(DateTimeOffset),
            EnumParseFailureMode.Throw, forceUtc);

        var expected = TypeCoercionHelper.Coerce(local, typeof(DateTime), typeof(DateTimeOffset), forceUtc);
        Assert.Equal(TimeSpan.Zero, ((DateTimeOffset)expected!).Offset);
        Assert.Equal(expected, coercer(local));
        Assert.Equal(TimeSpan.Zero, ((DateTimeOffset)coercer(local)!).Offset);
    }
}
