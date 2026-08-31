using System;
using System.Data;
using System.Reflection;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit;
using MacAddress = pengdows.crud.types.valueobjects.MacAddress;

namespace pengdows.crud.Tests;

// Targeted coverage for TypeCoercionHelper branches left uncovered by the broader existing suite
// (TypeCoercionHelperTests/BranchTests/AdvancedTests/etc.) — see each test's summary for the exact
// line/branch it exercises.
public class TypeCoercionHelperUncoveredBranchTests
{
    private sealed class StubColumnInfo : IColumnInfo
    {
        public string Name { get; init; } = "value";
        public PropertyInfo PropertyInfo { get; init; } = null!;
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
        public bool IsOpaqueVersionColumn => false;
        public bool IsCreatedBy { get; set; }
        public bool IsCreatedOn { get; set; }
        public bool IsLastUpdatedBy { get; set; }
        public bool IsLastUpdatedOn { get; set; }
        public int Ordinal { get; set; }
        public object? MakeParameterValueFromField<T>(T objectToCreate) => null;
    }

    private sealed class JsonHolder
    {
        public JsonValue Value { get; set; } = JsonValue.Parse("null");
    }

    // TypeCoercionHelper.CoerceCore: underlyingTarget == typeof(JsonValue) path (lines ~369-372).
    [Fact]
    public void Coerce_StringToJsonValue_ParsesJson()
    {
        var result = TypeCoercionHelper.Coerce("{\"a\":1}", typeof(string), typeof(JsonValue));

        var jsonValue = Assert.IsType<JsonValue>(result!);
        Assert.Equal("{\"a\":1}", jsonValue.AsString());
    }

    // TypeCoercionHelper.CoerceCore: default-value ternary for a non-numeric, non-special-cased
    // target type from an empty/whitespace string (line ~344's false branch and IsNumericClrType's
    // default->false branch, line ~428) — char is not in the explicit special-case list above and
    // is not in IsNumericClrType's TypeCode switch.
    [Fact]
    public void Coerce_WhitespaceStringToChar_ReturnsNull()
    {
        var result = TypeCoercionHelper.Coerce("   ", typeof(string), typeof(char));

        Assert.Null(result);
    }

    // Same ternary, true side: an unsigned integer type is numeric per IsNumericClrType but has no
    // earlier explicit special case, so it reaches the Activator.CreateInstance branch.
    [Fact]
    public void Coerce_WhitespaceStringToUInt32_ReturnsDefaultInstance()
    {
        var result = TypeCoercionHelper.Coerce("   ", typeof(string), typeof(uint));

        Assert.Equal(0u, result);
    }

    // TypeCoercionHelper.CoerceBoolean: char and double switch cases (lines ~575-582).
    [Theory]
    [InlineData('t', true)]
    [InlineData('n', false)]
    public void Coerce_CharToBoolean_UsesCharEvaluation(char input, bool expected)
    {
        var result = TypeCoercionHelper.Coerce(input, typeof(char), typeof(bool));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Coerce_DoubleToBoolean_NonZeroIsTrue()
    {
        var result = TypeCoercionHelper.Coerce(2.5d, typeof(double), typeof(bool));
        Assert.Equal(true, result);
    }

    // NOTE: CoerceDateTimeOffset's second string case (DateTime.TryParse-only fallback, used when
    // DateTimeOffset.TryParse rejects the string) was investigated but not covered here:
    // DateTimeOffset.TryParse accepts virtually every string DateTime.TryParse does (defaulting to
    // local offset when none is specified), so no plain string reliably reaches that branch via the
    // public Coerce entry point. Left as an acknowledged verification gap rather than forcing a
    // brittle test around undocumented parser edge-case behavior.

    // TypeCoercionHelper.CoerceDateTime: DateTime-string fallback branch (line ~647) — a string that
    // DateTimeOffset.TryParse rejects (no offset/zone info accepted as DateTimeOffset in this culture
    // is still typically accepted; use a format DateTime.TryParse accepts but drop through by using
    // the DateTime.TryParse-only overload path directly instead, which is unambiguous).
    [Fact]
    public void CoerceDateTimeFromString_PlainDateTimeString_ConvertsToUtc()
    {
        var method = typeof(TypeCoercionHelper).GetMethod(
            "CoerceDateTimeFromString", BindingFlags.NonPublic | BindingFlags.Static)
            ?? typeof(TypeCoercionHelper).GetMethod("CoerceDateTimeFromString");
        Assert.NotNull(method);

        var result = (DateTime)method!.Invoke(null, new object?[] { "2024-01-15 10:00:00" })!;

        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    // TypeCoercionHelper.CoerceDateTimeOffsetFromString: DateTime.TryParse fallback (line ~691).
    [Fact]
    public void CoerceDateTimeOffsetFromString_PlainDateTimeString_UsesFlexibleOffset()
    {
        var method = typeof(TypeCoercionHelper).GetMethod(
            "CoerceDateTimeOffsetFromString", BindingFlags.NonPublic | BindingFlags.Static)
            ?? typeof(TypeCoercionHelper).GetMethod("CoerceDateTimeOffsetFromString");
        Assert.NotNull(method);

        var result = (DateTimeOffset)method!.Invoke(null, new object?[] { "2024-01-15 10:00:00" })!;

        Assert.Equal(2024, result.Year);
    }

    // TypeCoercionHelper.CoerceJsonValue: JsonValue target branch (line ~782), reached through the
    // internal Coerce(value, dbFieldType, IColumnInfo, ...) overload with IsJsonType = true.
    [Fact]
    public void Coerce_WithJsonColumnInfo_TargetingJsonValue_ParsesJson()
    {
        var column = new StubColumnInfo
        {
            PropertyInfo = typeof(JsonHolder).GetProperty(nameof(JsonHolder.Value))!,
            IsJsonType = true
        };

        var method = typeof(TypeCoercionHelper).GetMethod(
            "Coerce",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(object), typeof(Type), typeof(IColumnInfo), typeof(EnumParseFailureMode), typeof(TypeCoercionOptions) },
            null);
        Assert.NotNull(method);

        var result = method!.Invoke(null, new object?[]
        {
            "{\"z\":9}", typeof(string), column, EnumParseFailureMode.Throw, null
        });

        var jsonValue = Assert.IsType<JsonValue>(result!);
        Assert.Equal("{\"z\":9}", jsonValue.AsString());
    }

    // TypeCoercionHelper.CoerceCore: AdvancedTypeRegistry legacy-converter fallback (line ~396) —
    // MacAddress has no CoercionRegistry entry, only an AdvancedTypeConverter, so a string source
    // must fall through the primary registry path to reach AdvancedTypeRegistry.Shared.
    [Fact]
    public void Coerce_StringToMacAddress_UsesAdvancedTypeRegistryFallback()
    {
        var result = TypeCoercionHelper.Coerce("08:00:2b:01:02:03", typeof(string), typeof(MacAddress));

        var mac = Assert.IsType<MacAddress>(result!);
        Assert.Equal("08:00:2b:01:02:03", mac.ToString(), StringComparer.OrdinalIgnoreCase);
    }
}
