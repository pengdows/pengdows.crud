using System;
using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class TypeCoercionHelperAdvancedTests
{
    [Fact]
    public void CoerceBoolean_FromCharacterSucceeds()
    {
        var result = TypeCoercionHelper.Coerce("Y", typeof(string), typeof(bool));

        Assert.True((bool)result!);
    }

    [Fact]
    public void CoerceBoolean_InvalidCharacterThrows()
    {
        Assert.Throws<InvalidCastException>(() => TypeCoercionHelper.Coerce("x", typeof(string), typeof(bool)));
    }

    [Fact]
    public void CoerceGuid_FromCharArray()
    {
        var guid = Guid.NewGuid();
        var chars = guid.ToString().ToCharArray();

        var result = TypeCoercionHelper.Coerce(chars, chars.GetType(), typeof(Guid));

        Assert.Equal(guid, result);
    }

    [Fact]
    public void CoerceGuid_InvalidByteArrayThrows()
    {
        var bytes = Encoding.UTF8.GetBytes("abc");
        Assert.Throws<InvalidCastException>(() => TypeCoercionHelper.Coerce(bytes, bytes.GetType(), typeof(Guid)));
    }

    private static ColumnInfo CreateJsonColumn()
    {
        var property = typeof(SampleEntity).GetProperty(nameof(SampleEntity.Payload))!;
        return new ColumnInfo
        {
            Name = "Payload",
            PropertyInfo = property,
            DbType = DbType.String,
            IsJsonType = true,
            JsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            },
            EnumType = null
        };
    }

    private static ColumnInfo CreateEnumColumn()
    {
        var property = typeof(SampleEntity).GetProperty(nameof(SampleEntity.State))!;
        return new ColumnInfo
        {
            Name = "State",
            PropertyInfo = property,
            DbType = DbType.Int32,
            IsEnum = true,
            EnumType = typeof(SampleState),
            EnumUnderlyingType = Enum.GetUnderlyingType(typeof(SampleState))
        };
    }

    private static ColumnInfo CreateInetColumn()
    {
        var property = typeof(SampleEntity).GetProperty(nameof(SampleEntity.Network))!;
        return new ColumnInfo
        {
            Name = "Network",
            PropertyInfo = property,
            DbType = DbType.String,
            EnumType = null,
            IsJsonType = false
        };
    }

    private sealed class SampleEntity
    {
        public SamplePayload Payload { get; set; } = new();
        public SampleState State { get; set; }
        public Inet Network { get; set; }
    }

    private sealed class SamplePayload
    {
        public DateTimeOffset When { get; set; }
    }

    private enum SampleState
    {
        One = 0,
        Two = 1
    }
}