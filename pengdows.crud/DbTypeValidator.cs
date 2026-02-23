using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Data;

namespace pengdows.crud;

/// <summary>
/// Validates CLR type compatibility with <see cref="DbType"/> before parameter execution.
/// Catches mismatches early with clear error messages instead of deferring to
/// provider-specific errors at execution time.
/// </summary>
internal static class DbTypeValidator
{
    // All numeric CLR types. Any numeric type is accepted for any numeric DbType —
    // the provider handles narrowing (e.g., long→int) and will throw if the actual
    // value overflows. Blocking this at the validator level would break legitimate
    // library patterns (e.g., TRowID=long with DbType.Int32 columns).
    private static readonly HashSet<Type> NumericTypes = new()
    {
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal)
    };

    private static readonly HashSet<DbType> NumericDbTypes = new()
    {
        DbType.Byte, DbType.SByte,
        DbType.Int16, DbType.UInt16,
        DbType.Int32, DbType.UInt32,
        DbType.Int64, DbType.UInt64,
        DbType.Single, DbType.Double,
        DbType.Decimal, DbType.Currency, DbType.VarNumeric
    };

    private static readonly HashSet<DbType> StringDbTypes = new()
    {
        DbType.String, DbType.StringFixedLength,
        DbType.AnsiString, DbType.AnsiStringFixedLength,
        DbType.Xml
    };

    private static readonly HashSet<DbType> DateTimeDbTypes = new()
    {
        DbType.DateTime, DbType.DateTime2, DbType.Date,
        DbType.Time, DbType.DateTimeOffset
    };

    // Maps each DbType to the set of CLR types that are directly compatible.
    // Numeric types are validated as a group (any numeric CLR → any numeric DbType).
    // Enums are accepted for all integer and string DbTypes.
    private static readonly FrozenDictionary<DbType, HashSet<Type>> AcceptableTypes =
        new Dictionary<DbType, HashSet<Type>>
        {
            [DbType.Boolean] = new() { typeof(bool) },

            [DbType.String] = new() { typeof(string), typeof(char), typeof(char[]), typeof(Guid) },
            [DbType.StringFixedLength] = new() { typeof(string), typeof(char), typeof(char[]), typeof(Guid) },
            [DbType.AnsiString] = new() { typeof(string), typeof(char), typeof(char[]), typeof(Guid) },
            [DbType.AnsiStringFixedLength] = new() { typeof(string), typeof(char), typeof(char[]), typeof(Guid) },
            [DbType.Xml] = new() { typeof(string) },

            [DbType.DateTime] = new() { typeof(DateTime), typeof(DateTimeOffset), typeof(string) },
            [DbType.DateTime2] = new() { typeof(DateTime), typeof(DateTimeOffset), typeof(string) },
            [DbType.Date] = new() { typeof(DateTime), typeof(DateTimeOffset), typeof(string) },
            [DbType.Time] = new() { typeof(TimeSpan), typeof(DateTime), typeof(string) },
            [DbType.DateTimeOffset] = new() { typeof(DateTimeOffset), typeof(DateTime), typeof(string) },

            [DbType.Guid] = new() { typeof(Guid), typeof(string), typeof(byte[]) },

            [DbType.Binary] = new()
                { typeof(byte[]), typeof(ArraySegment<byte>), typeof(ReadOnlyMemory<byte>), typeof(Stream) },

            [DbType.Object] = new() // Accept anything for DbType.Object
        }.ToFrozenDictionary();

    /// <summary>
    /// Validates that the CLR type of <paramref name="value"/> is compatible with
    /// the specified <paramref name="dbType"/>. Throws <see cref="ArgumentException"/>
    /// on mismatch.
    /// </summary>
    /// <remarks>
    /// Null/DBNull values are always accepted. Enum values are accepted for all integer DbTypes.
    /// DbType.Object accepts any type. Unknown DbTypes are passed through without validation.
    /// </remarks>
    internal static void Validate(DbType dbType, object? value)
    {
        if (value == null || value is DBNull)
            return;

        var clrType = value.GetType();

        // Unwrap nullable
        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        // Enums are compatible with all integer and string DbTypes
        if (clrType.IsEnum)
        {
            if (NumericDbTypes.Contains(dbType) || StringDbTypes.Contains(dbType) || dbType == DbType.Object)
                return;

            throw new ArgumentException(
                $"CLR type '{clrType.Name}' (enum) is not compatible with DbType.{dbType}. " +
                $"Use an integer or string DbType for enum values.");
        }

        // DbType.Object accepts anything
        if (dbType == DbType.Object)
            return;

        // Any numeric CLR type is accepted for any numeric DbType.
        // The provider handles narrowing and will throw on overflow at execution time.
        if (NumericDbTypes.Contains(dbType))
        {
            if (NumericTypes.Contains(clrType))
                return;

            // Non-numeric CLR type with numeric DbType — reject
            throw new ArgumentException(
                $"CLR type '{clrType.Name}' is not compatible with DbType.{dbType}. " +
                $"Ensure the value type matches the declared parameter type.");
        }

        // Check specific type maps for non-numeric DbTypes
        if (AcceptableTypes.TryGetValue(dbType, out var acceptable))
        {
            if (acceptable.Count == 0 || acceptable.Contains(clrType))
                return; // Empty set (Object) or explicit match

            throw new ArgumentException(
                $"CLR type '{clrType.Name}' is not compatible with DbType.{dbType}. " +
                $"Ensure the value type matches the declared parameter type.");
        }

        // Numeric CLR type with non-numeric, unmapped DbType — reject
        if (NumericTypes.Contains(clrType))
        {
            throw new ArgumentException(
                $"CLR type '{clrType.Name}' is not compatible with DbType.{dbType}. " +
                $"Ensure the value type matches the declared parameter type.");
        }

        // Unknown DbType with non-numeric CLR type — pass through without blocking
    }
}