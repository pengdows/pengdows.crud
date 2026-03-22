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
    private static readonly FrozenSet<Type> NumericTypes = new HashSet<Type>
    {
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal)
    }.ToFrozenSet();

    private static readonly FrozenSet<DbType> NumericDbTypes = new HashSet<DbType>
    {
        DbType.Byte, DbType.SByte,
        DbType.Int16, DbType.UInt16,
        DbType.Int32, DbType.UInt32,
        DbType.Int64, DbType.UInt64,
        DbType.Single, DbType.Double,
        DbType.Decimal, DbType.Currency, DbType.VarNumeric
    }.ToFrozenSet();

    private static readonly FrozenSet<DbType> StringDbTypes = new HashSet<DbType>
    {
        DbType.String, DbType.StringFixedLength,
        DbType.AnsiString, DbType.AnsiStringFixedLength,
        DbType.Xml
    }.ToFrozenSet();

    private static readonly FrozenSet<DbType> DateTimeDbTypes = new HashSet<DbType>
    {
        DbType.DateTime, DbType.DateTime2, DbType.Date,
        DbType.Time, DbType.DateTimeOffset
    }.ToFrozenSet();

    // Maps each DbType to the set of CLR types that are directly compatible.
    // Numeric types are validated as a group (any numeric CLR → any numeric DbType).
    // Enums are accepted for all integer and string DbTypes.
    // FrozenSet values ensure the per-DbType sets are fully immutable.
    private static readonly FrozenDictionary<DbType, FrozenSet<Type>> AcceptableTypes =
        new Dictionary<DbType, FrozenSet<Type>>
        {
            [DbType.Boolean] = new HashSet<Type> { typeof(bool) }.ToFrozenSet(),

            [DbType.String] = new HashSet<Type> { typeof(string), typeof(char), typeof(char[]), typeof(Guid) }.ToFrozenSet(),
            [DbType.StringFixedLength] = new HashSet<Type> { typeof(string), typeof(char), typeof(char[]), typeof(Guid) }.ToFrozenSet(),
            [DbType.AnsiString] = new HashSet<Type> { typeof(string), typeof(char), typeof(char[]), typeof(Guid) }.ToFrozenSet(),
            [DbType.AnsiStringFixedLength] = new HashSet<Type> { typeof(string), typeof(char), typeof(char[]), typeof(Guid) }.ToFrozenSet(),
            [DbType.Xml] = new HashSet<Type> { typeof(string) }.ToFrozenSet(),

            [DbType.DateTime] = new HashSet<Type> { typeof(DateTime), typeof(DateTimeOffset), typeof(string) }.ToFrozenSet(),
            [DbType.DateTime2] = new HashSet<Type> { typeof(DateTime), typeof(DateTimeOffset), typeof(string) }.ToFrozenSet(),
            [DbType.Date] = new HashSet<Type> { typeof(DateTime), typeof(DateTimeOffset), typeof(string) }.ToFrozenSet(),
            [DbType.Time] = new HashSet<Type> { typeof(TimeSpan), typeof(DateTime), typeof(string) }.ToFrozenSet(),
            [DbType.DateTimeOffset] = new HashSet<Type> { typeof(DateTimeOffset), typeof(DateTime), typeof(string) }.ToFrozenSet(),

            [DbType.Guid] = new HashSet<Type> { typeof(Guid), typeof(string), typeof(byte[]) }.ToFrozenSet(),

            [DbType.Binary] = new HashSet<Type>
                { typeof(byte[]), typeof(ArraySegment<byte>), typeof(ReadOnlyMemory<byte>), typeof(Stream) }.ToFrozenSet(),

            [DbType.Object] = FrozenSet<Type>.Empty, // Accept anything for DbType.Object
        }.ToFrozenDictionary();

    /// <summary>
    /// Validates that <paramref name="clrType"/> is compatible with the specified
    /// <paramref name="dbType"/>. Throws <see cref="ArgumentException"/> on mismatch.
    /// </summary>
    /// <remarks>
    /// Pass the result of <c>ResolveClrType&lt;T&gt;(value)</c> — the type must already be
    /// nullable-unwrapped. A null <paramref name="clrType"/> (i.e. value was null) is always
    /// accepted. Enum types are accepted for all integer and string DbTypes.
    /// DbType.Object accepts any type. Unknown DbTypes are passed through without validation.
    /// </remarks>
    internal static void Validate(DbType dbType, Type? clrType)
    {
        if (clrType == null)
        {
            return; // value was null — always valid
        }

        // clrType is already nullable-unwrapped by ResolveClrType.

        if (clrType.IsEnum)
        {
            if (NumericDbTypes.Contains(dbType) || StringDbTypes.Contains(dbType) || dbType == DbType.Object)
            {
                return;
            }

            throw new ArgumentException(
                $"CLR type '{clrType.Name}' (enum) is not compatible with DbType.{dbType}. " +
                $"Use an integer or string DbType for enum values.");
        }

        if (dbType == DbType.Object)
        {
            return;
        }

        if (NumericDbTypes.Contains(dbType))
        {
            if (NumericTypes.Contains(clrType))
            {
                return;
            }

            throw new ArgumentException(
                $"CLR type '{clrType.Name}' is not compatible with DbType.{dbType}. " +
                $"Ensure the value type matches the declared parameter type.");
        }

        if (AcceptableTypes.TryGetValue(dbType, out var acceptable))
        {
            if (acceptable.Count == 0 || acceptable.Contains(clrType))
            {
                return;
            }

            throw new ArgumentException(
                $"CLR type '{clrType.Name}' is not compatible with DbType.{dbType}. " +
                $"Ensure the value type matches the declared parameter type.");
        }

        if (NumericTypes.Contains(clrType))
        {
            throw new ArgumentException(
                $"CLR type '{clrType.Name}' is not compatible with DbType.{dbType}. " +
                $"Ensure the value type matches the declared parameter type.");
        }

        // Unknown DbType with non-numeric CLR type — pass through without blocking
    }

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
        {
            return;
        }

        var clrType = value.GetType();

        // Unwrap nullable
        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        // Enums are compatible with all integer and string DbTypes
        if (clrType.IsEnum)
        {
            if (NumericDbTypes.Contains(dbType) || StringDbTypes.Contains(dbType) || dbType == DbType.Object)
            {
                return;
            }

            throw new ArgumentException(
                $"CLR type '{clrType.Name}' (enum) is not compatible with DbType.{dbType}. " +
                $"Use an integer or string DbType for enum values.");
        }

        // DbType.Object accepts anything
        if (dbType == DbType.Object)
        {
            return;
        }

        // Any numeric CLR type is accepted for any numeric DbType.
        // The provider handles narrowing and will throw on overflow at execution time.
        if (NumericDbTypes.Contains(dbType))
        {
            if (NumericTypes.Contains(clrType))
            {
                return;
            }

            // Non-numeric CLR type with numeric DbType — reject
            throw new ArgumentException(
                $"CLR type '{clrType.Name}' is not compatible with DbType.{dbType}. " +
                $"Ensure the value type matches the declared parameter type.");
        }

        // Check specific type maps for non-numeric DbTypes
        if (AcceptableTypes.TryGetValue(dbType, out var acceptable))
        {
            if (acceptable.Count == 0 || acceptable.Contains(clrType))
            {
                return; // Empty set (Object) or explicit match
            }

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