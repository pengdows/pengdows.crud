using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("TypeRegistry")]
public class DataReaderMapperBranchTests
{
    [Fact]
    public async Task LoadAsync_StringToDateTime_UsesDirectReadExpression()
    {
        await using var reader = new TypedReader<string>("Timestamp", "2024-01-02T03:04:05Z");

        var result = await DataReaderMapper.LoadAsync<DateTimeEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(DateTimeKind.Utc, result[0].Timestamp.Kind);
    }

    [Fact]
    public async Task LoadAsync_StringToDateTimeOffset_UsesDirectReadExpression()
    {
        await using var reader = new TypedReader<string>("OffsetValue", "2024-01-02T03:04:05+02:00");

        var result = await DataReaderMapper.LoadAsync<DateTimeOffsetEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(TimeSpan.FromHours(2), result[0].OffsetValue.Offset);
    }

    [Fact]
    public async Task LoadAsync_DateTimeToDateTimeOffset_UsesDirectReadExpression()
    {
        var dateTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await using var reader = new TypedReader<DateTime>("OffsetValue", dateTime);

        var result = await DataReaderMapper.LoadAsync<DateTimeOffsetEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(dateTime, result[0].OffsetValue.UtcDateTime);
    }

    [Fact]
    public async Task LoadAsync_StringToGuid_UsesDirectReadExpression()
    {
        var guid = Guid.NewGuid();
        await using var reader = new TypedReader<string>("Id", guid.ToString("D"));

        var result = await DataReaderMapper.LoadAsync<GuidEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(guid, result[0].Id);
    }

    [Fact]
    public async Task LoadAsync_BytesToGuid_UsesDirectReadExpression()
    {
        var guid = Guid.NewGuid();
        await using var reader = new TypedReader<byte[]>("Id", guid.ToByteArray());

        var result = await DataReaderMapper.LoadAsync<GuidEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(guid, result[0].Id);
    }

    [Fact]
    public async Task LoadAsync_ObjectFieldType_UsesGetValueBranch()
    {
        await using var reader = new ObjectFieldReader("Payload", "abc");

        var result = await DataReaderMapper.LoadAsync<ObjectEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal("abc", result[0].Payload);
        Assert.True(reader.GetValueCalled);
    }

    [Fact]
    public async Task LoadAsync_InterfaceTargetType_UsesConvertBranch()
    {
        await using var reader = new TypedReader<int>("Value", 123);

        var result = await DataReaderMapper.LoadAsync<InterfaceEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal("123", result[0].Value.ToString(null, null));
    }

    private sealed class DateTimeEntity
    {
        public DateTime Timestamp { get; set; }
    }

    private sealed class DateTimeOffsetEntity
    {
        public DateTimeOffset OffsetValue { get; set; }
    }

    private sealed class GuidEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class ObjectEntity
    {
        public object? Payload { get; set; }
    }

    private sealed class InterfaceEntity
    {
        public IFormattable Value { get; set; } = null!;
    }

    private sealed class NonEnumEntity
    {
        public string? Name { get; set; }
    }

    private enum SampleColor
    {
        Red = 1,
        Blue = 2
    }

    private sealed class EnumEntity
    {
        public SampleColor Color { get; set; }
        public SampleColor? NullableColor { get; set; }
    }

    private sealed class TypedReader<TField> : fakeDbDataReader
    {
        private readonly TField _value;
        private readonly string _name;

        public TypedReader(string name, TField value)
            : base(new[]
            {
                new Dictionary<string, object>
                {
                    [name] = value!
                }
            })
        {
            _name = name;
            _value = value;
        }

        public override string GetName(int i)
        {
            if (i != 0)
            {
                throw new IndexOutOfRangeException();
            }

            return _name;
        }

        public override Type GetFieldType(int ordinal)
        {
            if (ordinal != 0)
            {
                throw new IndexOutOfRangeException();
            }

            return typeof(TField);
        }

        public override T GetFieldValue<T>(int ordinal)
        {
            if (ordinal != 0)
            {
                throw new IndexOutOfRangeException();
            }

            if (typeof(T) == typeof(TField))
            {
                return (T)(object)_value!;
            }

            return (T)Convert.ChangeType(_value!, typeof(T));
        }
    }

    private sealed class ObjectFieldReader : fakeDbDataReader
    {
        private readonly object _value;
        private readonly string _name;

        public ObjectFieldReader(string name, object value)
            : base(new[]
            {
                new Dictionary<string, object>
                {
                    [name] = value
                }
            })
        {
            _name = name;
            _value = value;
        }

        public bool GetValueCalled { get; private set; }

        public override string GetName(int i)
        {
            if (i != 0)
            {
                throw new IndexOutOfRangeException();
            }

            return _name;
        }

        public override Type GetFieldType(int ordinal)
        {
            return typeof(object);
        }

        public override object GetValue(int i)
        {
            GetValueCalled = true;
            return _value;
        }

        public override T GetFieldValue<T>(int ordinal)
        {
            throw new InvalidOperationException("GetFieldValue should not be used for object-typed mapping.");
        }
    }
}
