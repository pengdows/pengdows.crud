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
public class DataReaderMapperBranchCoverageTests
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

    [Fact]
    public void PrivateNumericHelpers_ExerciseAllConversionBranches()
    {
        var buildNumericConversion = typeof(DataReaderMapper)
            .GetMethod("BuildNumericConversion", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildNumericConversion not found.");

        var resolveConvertMethod = typeof(DataReaderMapper)
            .GetMethod("ResolveConvertMethod", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolveConvertMethod not found.");

        var rawInt = Expression.Parameter(typeof(int), "v");
        var rawLong = Expression.Parameter(typeof(long), "v");
        var rawDouble = Expression.Parameter(typeof(double), "v");
        var rawDecimal = Expression.Parameter(typeof(decimal), "v");

        _ = buildNumericConversion.Invoke(null, new object[] { rawInt, typeof(int), typeof(int) });
        _ = buildNumericConversion.Invoke(null, new object[] { rawLong, typeof(long), typeof(int) });
        _ = buildNumericConversion.Invoke(null, new object[] { rawDouble, typeof(double), typeof(int) });
        _ = buildNumericConversion.Invoke(null, new object[] { rawDouble, typeof(double), typeof(decimal) });
        _ = buildNumericConversion.Invoke(null, new object[] { rawDecimal, typeof(decimal), typeof(double) });
        _ = buildNumericConversion.Invoke(null, new object[] { rawDecimal, typeof(decimal), typeof(char) });

        var unsupported = resolveConvertMethod.Invoke(null, new object[] { typeof(DateTime), typeof(int) });
        Assert.Null(unsupported);
    }

    [Fact]
    public void PrivateTypeChecks_ExerciseNumericAndIntegralSwitches()
    {
        var isNumericType = typeof(DataReaderMapper)
            .GetMethod("IsNumericType", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("IsNumericType not found.");
        var isIntegralType = typeof(DataReaderMapper)
            .GetMethod("IsIntegralType", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("IsIntegralType not found.");

        var numericTypes = new[]
        {
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long),
            typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(string)
        };

        foreach (var type in numericTypes)
        {
            _ = isNumericType.Invoke(null, new object[] { type });
        }

        var integralTypes = new[]
        {
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long),
            typeof(ulong), typeof(decimal)
        };

        foreach (var type in integralTypes)
        {
            _ = isIntegralType.Invoke(null, new object[] { type });
        }
    }

    [Fact]
    public void PrivateEnumFailureHandling_ExercisesAllModes()
    {
        var tryHandleEnumFailure = typeof(DataReaderMapper)
            .GetMethod("TryHandleEnumFailure", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TryHandleEnumFailure not found.");

        var nonEnumProperty = typeof(NonEnumEntity).GetProperty(nameof(NonEnumEntity.Name))!;
        var nonEnumArgs = new object?[] { "x", nonEnumProperty, EnumParseFailureMode.SetDefaultValue, new Exception(), null };
        var nonEnumHandled = (bool)tryHandleEnumFailure.Invoke(null, nonEnumArgs)!;
        Assert.False(nonEnumHandled);

        var enumProperty = typeof(EnumEntity).GetProperty(nameof(EnumEntity.Color))!;
        var throwArgs = new object?[] { "x", enumProperty, EnumParseFailureMode.Throw, new Exception(), null };
        var throwHandled = (bool)tryHandleEnumFailure.Invoke(null, throwArgs)!;
        Assert.False(throwHandled);

        var originalLogger = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;

            var setNullAndLogArgs = new object?[]
                { "x", enumProperty, EnumParseFailureMode.SetNullAndLog, new Exception(), null };
            var setNullAndLogHandled = (bool)tryHandleEnumFailure.Invoke(null, setNullAndLogArgs)!;
            Assert.True(setNullAndLogHandled);
            Assert.Equal(Enum.ToObject(typeof(SampleColor), 0), setNullAndLogArgs[4]);
        }
        finally
        {
            TypeCoercionHelper.Logger = originalLogger;
        }

        var nullableEnumProperty = typeof(EnumEntity).GetProperty(nameof(EnumEntity.NullableColor))!;
        var setDefaultArgs = new object?[]
            { "x", nullableEnumProperty, EnumParseFailureMode.SetDefaultValue, new Exception(), null };
        var setDefaultHandled = (bool)tryHandleEnumFailure.Invoke(null, setDefaultArgs)!;
        Assert.True(setDefaultHandled);
        Assert.Null(setDefaultArgs[4]);

        var invalidModeArgs = new object?[] { "x", enumProperty, (EnumParseFailureMode)999, new Exception(), null };
        var invalidModeHandled = (bool)tryHandleEnumFailure.Invoke(null, invalidModeArgs)!;
        Assert.False(invalidModeHandled);
    }

    [Fact]
    public void CoerceValue_WhenInputIsNull_ReturnsNull()
    {
        var coerceValue = typeof(DataReaderMapper)
            .GetMethod("CoerceValue", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CoerceValue not found.");
        var property = typeof(NonEnumEntity).GetProperty(nameof(NonEnumEntity.Name))!;

        var result = coerceValue.Invoke(null, new object?[] { null, property, typeof(string), EnumParseFailureMode.Throw });

        Assert.Null(result);
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
