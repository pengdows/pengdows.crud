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
    // Enums are accepted for all numeric and string DbTypes.
    // FrozenSet values ensure the per-DbType sets are fully immutable.
    // Accepted types a value is a subclass of, never an instance of itself: a MemoryStream or FileStream
    // is a Stream, a StringReader a TextReader. Kept out of AcceptableTypes: a FrozenSet of five or more types
    // stops scanning linearly and hashes instead, which cost every string parameter about 3 ns.
    private static readonly Type[] TextAbstractTypes = { typeof(TextReader) };
    private static readonly Type[] BinaryAbstractTypes = { typeof(Stream) };

    private static readonly FrozenDictionary<DbType, FrozenSet<Type>> AcceptableTypes =
        new Dictionary<DbType, FrozenSet<Type>>
        {
            [DbType.Boolean] = new HashSet<Type> { typeof(bool) }.ToFrozenSet(),

            [DbType.String] = new HashSet<Type> { typeof(string), typeof(char), typeof(char[]), typeof(Guid) }.ToFrozenSet(),
            [DbType.StringFixedLength] = new HashSet<Type> { typeof(string), typeof(char), typeof(char[]), typeof(Guid) }.ToFrozenSet(),
            [DbType.AnsiString] = new HashSet<Type> { typeof(string), typeof(char), typeof(char[]), typeof(Guid) }.ToFrozenSet(),
            [DbType.AnsiStringFixedLength] = new HashSet<Type> { typeof(string), typeof(char), typeof(char[]), typeof(Guid) }.ToFrozenSet(),
            [DbType.Xml] = new HashSet<Type> { typeof(string) }.ToFrozenSet(),

            [DbType.DateTime] = new HashSet<Type> { typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(string) }.ToFrozenSet(),
            [DbType.DateTime2] = new HashSet<Type> { typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(string) }.ToFrozenSet(),
            [DbType.Date] = new HashSet<Type> { typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(string) }.ToFrozenSet(),
            [DbType.Time] = new HashSet<Type> { typeof(TimeSpan), typeof(TimeOnly), typeof(DateTime), typeof(string) }.ToFrozenSet(),
            [DbType.DateTimeOffset] = new HashSet<Type> { typeof(DateTimeOffset), typeof(DateTime), typeof(string) }.ToFrozenSet(),

            [DbType.Guid] = new HashSet<Type> { typeof(Guid), typeof(string), typeof(byte[]) }.ToFrozenSet(),

            [DbType.Binary] = new HashSet<Type>
                { typeof(byte[]), typeof(ArraySegment<byte>), typeof(ReadOnlyMemory<byte>), typeof(Guid) }.ToFrozenSet(),

            [DbType.Object] = FrozenSet<Type>.Empty, // Accept anything for DbType.Object
        }.ToFrozenDictionary();

    /// <summary>
    /// Validates that <paramref name="clrType"/> is compatible with the specified
    /// <paramref name="dbType"/>. Throws <see cref="ArgumentException"/> on mismatch.
    /// </summary>
    /// <remarks>
    /// Pass the result of <c>SqlDialect.ResolveClrType&lt;T&gt;(value)</c> — the type must already be
    /// nullable-unwrapped. A null <paramref name="clrType"/> (i.e. value was null) is always
    /// accepted. Enum types are accepted for all numeric and string DbTypes.
    /// DbType.Object accepts any type. Unknown DbTypes are passed through without validation.
    /// </remarks>
    internal static void Validate(DbType dbType, Type? clrType)
    {
        if (clrType != null && !Accepts(dbType, clrType))
        {
            throw new ArgumentException(MismatchMessage(dbType, clrType));
        }
    }

    /// <summary>
    /// True when a value of <paramref name="value"/>'s type can be bound as <paramref name="dbType"/>
    /// (null always can); the non-throwing form of <see cref="Validate(DbType, object?)"/>.
    /// </summary>
    internal static bool IsCompatible(DbType dbType, object? value) =>
        value is null or DBNull || Accepts(dbType, Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType());

    // A loop, not Array.Exists with a lambda: the lambda captures clrType and acceptable, and the compiler then allocates its
    // closure object for the whole scope, so every parameter paid 32 B even when the first check had already answered.
    private static bool AcceptsASubclassOf(DbType dbType, Type clrType)
    {
        var abstractTypes = dbType switch
        {
            DbType.String or DbType.AnsiString => TextAbstractTypes,
            DbType.Binary => BinaryAbstractTypes,
            _ => null
        };
        if (abstractTypes == null)
        {
            return false;
        }

        foreach (var abstractType in abstractTypes)
        {
            if (abstractType.IsAssignableFrom(clrType))
            {
                return true;
            }
        }

        return false;
    }

    // The answer every parameter asks for, kept free of the message text: building a message needs interpolated-string
    // locals, and a method that holds them pays to set them up on every call, accepted or not.
    // clrType is already nullable-unwrapped by ResolveClrType.
    private static bool Accepts(DbType dbType, Type clrType)
    {
        if (clrType.IsEnum)
        {
            return NumericDbTypes.Contains(dbType) || StringDbTypes.Contains(dbType) || dbType == DbType.Object;
        }

        if (dbType == DbType.Object)
        {
            return true;
        }

        if (NumericDbTypes.Contains(dbType))
        {
            return NumericTypes.Contains(clrType);
        }

        if (AcceptableTypes.TryGetValue(dbType, out var acceptable))
        {
            return acceptable.Count == 0 || acceptable.Contains(clrType) || AcceptsASubclassOf(dbType, clrType);
        }

        // Unknown DbType: a numeric CLR type is blocked, anything else passes without validation.
        return !NumericTypes.Contains(clrType);
    }

    private static string MismatchMessage(DbType dbType, Type clrType) =>
        clrType.IsEnum
            ? $"CLR type '{clrType.Name}' (enum) is not compatible with DbType.{dbType}. " +
              $"Use an integer or string DbType for enum values."
            : $"CLR type '{clrType.Name}' is not compatible with DbType.{dbType}. " +
              $"Ensure the value type matches the declared parameter type.";

    /// <summary>
    /// Validates that the CLR type of <paramref name="value"/> is compatible with
    /// the specified <paramref name="dbType"/>. Throws <see cref="ArgumentException"/>
    /// on mismatch.
    /// </summary>
    /// <remarks>
    /// Null/DBNull values are always accepted. Enum values are accepted for all numeric and string DbTypes.
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

        // Enums are compatible with all numeric and string DbTypes
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