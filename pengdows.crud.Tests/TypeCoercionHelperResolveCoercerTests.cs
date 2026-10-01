using System;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

public class TypeCoercionHelperResolveCoercerTests
{
    private sealed class FieldTypeTarget
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class FieldTypeHolder
    {
        public FieldTypeTarget Payload { get; set; } = new();
    }

    private sealed class FieldTypeObserverCoercion : DbCoercion<FieldTypeTarget>
    {
        public Type? LastObservedDbType { get; private set; }

        public override bool TryRead(in DbValue src, out FieldTypeTarget? value)
        {
            LastObservedDbType = src.DbType;
            if (src.RawValue is string text)
            {
                value = new FieldTypeTarget { Value = text };
                return true;
            }

            value = null;
            return false;
        }

        public override bool TryWrite(FieldTypeTarget? value, DbParameter parameter) => false;
    }

    private enum SampleStatus { Active, Inactive }

    private sealed class EnumHolder
    {
        public SampleStatus Status { get; set; }
    }

    private sealed class DateTimeOffsetHolder
    {
        public DateTimeOffset Value { get; set; }
    }

    private sealed class DateTimeHolder
    {
        public DateTime Value { get; set; }
    }

    private sealed class JsonTypedHolder
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class NeverRegisteredTarget
    {
        public string Text { get; set; } = string.Empty;
    }

    private sealed class NeverRegisteredHolder
    {
        public NeverRegisteredTarget Value { get; set; } = new();
    }

    private static Func<object?, object?> InvokePrivateResolveCoercer(Type sourceType, Type targetType)
    {
        var method = typeof(TypeCoercionHelper).GetMethod(
            "ResolveCoercer",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(Type), typeof(Type), typeof(EnumParseFailureMode) },
            null);

        return (Func<object?, object?>)method!.Invoke(null, new object?[]
        {
            sourceType,
            targetType,
            EnumParseFailureMode.Throw
        })!;
    }

    private sealed class StubColumnInfo : IColumnInfo
    {
        private static readonly PropertyInfo PayloadProperty =
            typeof(FieldTypeHolder).GetProperty(nameof(FieldTypeHolder.Payload))!;

        public string Name { get; init; } = "payload";
        public PropertyInfo PropertyInfo { get; init; } = PayloadProperty;
        public bool IsId { get; init; }
        public DbType DbType { get; set; }
        public bool IsNonUpdateable { get; set; }
        public bool IsNonInsertable { get; set; }
        public bool IsEnum { get; set; }
        public Type? EnumType { get; set; }
        public Type? EnumUnderlyingType { get; set; }
        public bool EnumAsString { get; set; }
        public bool IsJsonType { get; set; }
        public JsonSerializerOptions JsonSerializerOptions { get; set; } = new();
        public bool IsIdWritable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsCorrelationToken { get; set; }
        public int PkOrder { get; set; }
        public bool IsVersion { get; set; }
        public bool IsOpaqueVersionColumn => PropertyInfo.PropertyType == typeof(byte[]) || PropertyInfo.PropertyType == typeof(pengdows.crud.types.valueobjects.RowVersion);
        public bool IsCreatedBy { get; set; }
        public bool IsCreatedOn { get; set; }
        public bool IsLastUpdatedBy { get; set; }
        public bool IsLastUpdatedOn { get; set; }
        public int Ordinal { get; set; }
        public object? MakeParameterValueFromField<T>(T objectToCreate) => PayloadProperty.GetValue(objectToCreate);
    }

    [Fact]
    public void ResolveCoercer_UsesProvidedFieldTypeWhenObservingDbType()
    {
        var coercion = new FieldTypeObserverCoercion();
        CoercionRegistry.Shared.Register(coercion);

        var resolved = TypeCoercionHelper.ResolveCoercer(
            new StubColumnInfo(),
            SupportedDatabase.Sqlite,
            EnumParseFailureMode.Throw,
            TypeCoercionOptions.Default,
            fieldType: typeof(string));

        var result = resolved("payload");

        var typed = Assert.IsType<FieldTypeTarget>(result!);
        Assert.Equal("payload", typed.Value);
        Assert.Equal(typeof(string), coercion.LastObservedDbType);
    }

    [Fact]
    public void ResolveCoercer_ForTypePair_ConvertsValue()
    {
        var method = typeof(TypeCoercionHelper).GetMethod(
            "ResolveCoercer",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(Type), typeof(Type), typeof(EnumParseFailureMode) },
            null);

        Assert.NotNull(method);

        var resolved = (Func<object?, object?>)method!.Invoke(null, new object?[]
        {
            typeof(string),
            typeof(int),
            EnumParseFailureMode.Throw
        })!;

        var result = resolved("42");

        Assert.Equal(42, result);
    }

    // --- ResolveCoercer(IColumnInfo, ...) branch coverage ---

    [Fact]
    public void ResolveCoercer_WithEnumColumn_HandlesNullAndNonNull()
    {
        var column = new StubColumnInfo
        {
            PropertyInfo = typeof(EnumHolder).GetProperty(nameof(EnumHolder.Status))!,
            EnumType = typeof(SampleStatus),
            IsEnum = true
        };

        var resolved = TypeCoercionHelper.ResolveCoercer(
            column, SupportedDatabase.Sqlite, EnumParseFailureMode.Throw, TypeCoercionOptions.Default);

        Assert.Null(resolved(null));
        Assert.Equal(SampleStatus.Active, resolved("Active"));
    }

    [Fact]
    public void ResolveCoercer_WithJsonColumn_HandlesNullAndNonNull()
    {
        var column = new StubColumnInfo
        {
            PropertyInfo = typeof(JsonTypedHolder).GetProperty(nameof(JsonTypedHolder.Value))!,
            IsJsonType = true
        };

        var resolved = TypeCoercionHelper.ResolveCoercer(
            column, SupportedDatabase.Sqlite, EnumParseFailureMode.Throw, TypeCoercionOptions.Default);

        Assert.Null(resolved(null));
        Assert.Equal("hello", resolved("hello"));
    }

    [Fact]
    public void ResolveCoercer_WithDateTimeOffsetColumn_HandlesNullAndNonNull()
    {
        var column = new StubColumnInfo
        {
            PropertyInfo = typeof(DateTimeOffsetHolder).GetProperty(nameof(DateTimeOffsetHolder.Value))!
        };

        var resolved = TypeCoercionHelper.ResolveCoercer(
            column, SupportedDatabase.Sqlite, EnumParseFailureMode.Throw, TypeCoercionOptions.Default);

        Assert.Null(resolved(null));
        var result = resolved(DateTime.UtcNow);
        Assert.IsType<DateTimeOffset>(result);
    }

    [Fact]
    public void ResolveCoercer_WithDateTimeColumn_HandlesNullAndNonNull()
    {
        var column = new StubColumnInfo
        {
            PropertyInfo = typeof(DateTimeHolder).GetProperty(nameof(DateTimeHolder.Value))!
        };

        var resolved = TypeCoercionHelper.ResolveCoercer(
            column, SupportedDatabase.Sqlite, EnumParseFailureMode.Throw, TypeCoercionOptions.Default);

        Assert.Null(resolved(null));
        var result = resolved(DateTimeOffset.UtcNow);
        Assert.IsType<DateTime>(result);
    }

    [Fact]
    public void ResolveCoercer_WithRegisteredCoercion_HandlesNullSuccessAndFallback()
    {
        var coercion = new FieldTypeObserverCoercion();
        CoercionRegistry.Shared.Register(coercion);

        var resolved = TypeCoercionHelper.ResolveCoercer(
            new StubColumnInfo(), SupportedDatabase.Sqlite, EnumParseFailureMode.Throw, TypeCoercionOptions.Default,
            fieldType: typeof(string));

        Assert.Null(resolved(null));

        var success = Assert.IsType<FieldTypeTarget>(resolved("payload")!);
        Assert.Equal("payload", success.Value);

        // TryRead returns false for a non-string source, forcing the ConvertWithCache fallback path.
        Assert.ThrowsAny<Exception>(() => resolved(123));
    }

    [Fact]
    public void ResolveCoercer_FallsBackToFullCoerceDispatch_WhenNoOtherPathMatches()
    {
        var column = new StubColumnInfo
        {
            PropertyInfo = typeof(NeverRegisteredHolder).GetProperty(nameof(NeverRegisteredHolder.Value))!
        };

        var resolved = TypeCoercionHelper.ResolveCoercer(
            column, SupportedDatabase.Sqlite, EnumParseFailureMode.Throw, TypeCoercionOptions.Default,
            fieldType: typeof(NeverRegisteredTarget));

        Assert.Null(resolved(null));

        var identity = new NeverRegisteredTarget { Text = "same-instance" };
        Assert.Same(identity, resolved(identity));
    }

    // --- ResolveCoercer(Type, Type, EnumParseFailureMode) branch coverage ---

    [Fact]
    public void ResolveCoercer_ForTypePair_JsonValueTarget_HandlesNullExistingAndParsedString()
    {
        var resolved = InvokePrivateResolveCoercer(typeof(string), typeof(pengdows.crud.types.valueobjects.JsonValue));

        Assert.Null(resolved(null));

        var existing = pengdows.crud.types.valueobjects.JsonValue.Parse("{\"x\":1}");
        Assert.Equal(existing, resolved(existing));

        var parsed = resolved("{\"y\":2}");
        Assert.IsType<pengdows.crud.types.valueobjects.JsonValue>(parsed);
    }

    [Fact]
    public void ResolveCoercer_ForTypePair_DateTimeOffsetTarget_HandlesNullAndNonNull()
    {
        var resolved = InvokePrivateResolveCoercer(typeof(DateTime), typeof(DateTimeOffset));

        Assert.Null(resolved(null));
        Assert.IsType<DateTimeOffset>(resolved(DateTime.UtcNow));
    }

    [Fact]
    public void ResolveCoercer_ForTypePair_DateTimeTarget_HandlesNullAndNonNull()
    {
        var resolved = InvokePrivateResolveCoercer(typeof(DateTimeOffset), typeof(DateTime));

        Assert.Null(resolved(null));
        Assert.IsType<DateTime>(resolved(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ResolveCoercer_ForTypePair_AssignableSourceAndTarget_HandlesNullAndNonNull()
    {
        var resolved = InvokePrivateResolveCoercer(typeof(string), typeof(string));

        Assert.Null(resolved(null));
        Assert.Equal("same", resolved("same"));
    }

    [Fact]
    public void ResolveCoercer_ForTypePair_WithRegisteredCoercion_HandlesNullSuccessAndFallback()
    {
        CoercionRegistry.Shared.Register(new FieldTypeObserverCoercion());

        var resolved = InvokePrivateResolveCoercer(typeof(string), typeof(FieldTypeTarget));

        Assert.Null(resolved(null));

        var success = Assert.IsType<FieldTypeTarget>(resolved("payload")!);
        Assert.Equal("payload", success.Value);

        Assert.ThrowsAny<Exception>(() => resolved(123));
    }

    [Fact]
    public void ResolveCoercer_ForTypePair_FallsBackToFullCoerceDispatch_WhenNoOtherPathMatches()
    {
        var resolved = InvokePrivateResolveCoercer(typeof(string), typeof(NeverRegisteredTarget));

        Assert.Null(resolved(null));
        Assert.ThrowsAny<Exception>(() => resolved("some text"));
    }
}