// =============================================================================
// FILE: EnumCoercion.cs
// PURPOSE: Converts a value to an enum: a member, its name, its number, and the failure modes a read can choose.
//
// AI SUMMARY:
// - TryGetEnumType: the enum type a target is, nullable or not.
// - CoerceEnum: the member for a string (name, ignoring case), a number (a defined value or a [Flags] combination), or a
//   character. A value that is no member follows the EnumParseFailureMode: throw, log and default (SetNullAndLog, a
//   non-nullable enum takes its default, DRY-007), or the default value.
// - Called by TypeCoercionHelper and by the enum fast paths; the failure modes are defined here once.
// =============================================================================

using System;
using System.Globalization;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.exceptions;

namespace pengdows.crud.types.coercion;

internal static class EnumCoercion
{
    internal static object? CoerceEnum(object value, Type enumType, EnumParseFailureMode parseMode, Type targetType)
    {
        if (enumType.IsInstanceOfType(value))
        {
            return value;
        }

        var isNullable = Nullable.GetUnderlyingType(targetType) != null;
        var stringValue = value as string ?? (value is char c ? c.ToString() : null);

        if (!string.IsNullOrEmpty(stringValue))
        {
            if (ColumnInfo.TryParseEnumLiteral(enumType, stringValue, out var literalResult))
            {
                return literalResult!;
            }

            // Validated like a number: Enum.TryParse accepts "99" as an undefined value (DRY-003).
            if (Enum.TryParse(enumType, stringValue, true, out var parsed) && EnumMappingCache.IsValid(enumType, parsed!))
            {
                return parsed!;
            }

            return HandleEnumFailure(value, enumType, parseMode, isNullable);
        }

        try
        {
            // A fractional number is no member: Convert.ToInt64 rounded 2.7 to 3 (COR-009).
            WholeNumber.Check(value, typeof(long));
            // An integer goes in as itself (Convert.ToInt64 overflowed a ulong above long.MaxValue,
            // REV-070); a whole decimal or floating value by its sign.
            var result = value is decimal or double or float
                ? NumericEnumValue(enumType, value)
                : Enum.ToObject(enumType, value);
            // Defined, or a combination of [Flags] members, as on the gateway (DRY-003).
            if (EnumMappingCache.IsValid(enumType, result))
            {
                return result;
            }

            return HandleEnumFailure(value, enumType, parseMode, isNullable);
        }
        catch
        {
            return HandleEnumFailure(value, enumType, parseMode, isNullable);
        }
    }

    private static object NumericEnumValue(Type enumType, object wholeValue)
    {
        if (Convert.ToDecimal(wholeValue, CultureInfo.InvariantCulture) >= 0)
        {
            return Enum.ToObject(enumType, Convert.ToUInt64(wholeValue, CultureInfo.InvariantCulture));
        }

        return Enum.ToObject(enumType, Convert.ToInt64(wholeValue, CultureInfo.InvariantCulture));
    }

    private static object DefaultOf(Type enumType) =>
        Enum.ToObject(enumType, Activator.CreateInstance(Enum.GetUnderlyingType(enumType))!);

    /// <summary>The warning <see cref="EnumParseFailureMode.SetNullAndLog"/> logs on every read path.</summary>
    internal static void LogEnumFailure(Type enumType) =>
        TypeCoercionHelper.TryLogWarning("Cannot convert value to enum {EnumType}.", enumType);

    private static object? HandleEnumFailure(object _, Type enumType, EnumParseFailureMode parseMode,
        bool targetNullable)
    {
        switch (parseMode)
        {
            case EnumParseFailureMode.Throw:
                throw new EnumValueException($"Cannot convert value to enum {enumType}");
            case EnumParseFailureMode.SetDefaultValue:
                return targetNullable ? null : DefaultOf(enumType);
            case EnumParseFailureMode.SetNullAndLog:
                // Null where the property can hold it; a non-nullable enum gets its default, as on the
                // gateway (null failed to unbox, DRY-007).
                LogEnumFailure(enumType);
                return targetNullable ? null : DefaultOf(enumType);
            default:
                return null;
        }
    }

    internal static bool TryGetEnumType(Type targetType, out Type enumType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsEnum)
        {
            enumType = underlying;
            return true;
        }

        enumType = underlying;
        return false;
    }
}
