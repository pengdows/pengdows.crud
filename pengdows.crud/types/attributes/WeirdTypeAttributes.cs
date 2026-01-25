using System;

namespace pengdows.crud.types.attributes;

/// <summary>
/// Specifies how an enum should be stored in the database.
/// </summary>
public enum EnumStorage
{
    /// <summary>Store as enum name (string)</summary>
    Name,

    /// <summary>Store as enum integer value</summary>
    Int
}

/// <summary>
/// Configures database enum handling for a property or type.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Enum)]
public class DbEnumAttribute : Attribute
{
    /// <summary>
    /// Whether to allow unknown enum values when reading from database.
    /// </summary>
    public bool AllowUnknown { get; set; } = false;

    /// <summary>
    /// How to store the enum value in the database.
    /// </summary>
    public EnumStorage StoreAs { get; set; } = EnumStorage.Name;
}

/// <summary>
/// Specifies a JSON schema contract for a property.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class JsonContractAttribute : Attribute
{
    public Type ShapeType { get; }

    public JsonContractAttribute(Type shapeType)
    {
        ShapeType = shapeType ?? throw new ArgumentNullException(nameof(shapeType));
    }
}

/// <summary>
/// Marks a property as a concurrency token (like SQL Server rowversion).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ConcurrencyTokenAttribute : Attribute
{
}

/// <summary>
/// Configures range type formatting and validation.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class RangeTypeAttribute : Attribute
{
    /// <summary>
    /// The canonical format to use for the range type.
    /// </summary>
    public string? CanonicalFormat { get; set; }
}

/// <summary>
/// Marks a computed column with storage information.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ComputedAttribute : Attribute
{
    /// <summary>
    /// Whether the computed column is stored (materialized) or virtual.
    /// </summary>
    public bool Stored { get; set; } = false;
}

/// <summary>
/// Indicates that a property should be treated as case-insensitive text.
/// Used for documentation - actual case handling depends on database collation.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class CaseInsensitiveAttribute : Attribute
{
}

/// <summary>
/// Forces a numeric type to be stored/read as a string when precision exceeds .NET limits.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class AsStringAttribute : Attribute
{
}

/// <summary>
/// Controls memory allocation strategy for binary data.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class MaxLengthForInlineAttribute : Attribute
{
    /// <summary>
    /// Maximum length for eager inline reading. Larger values use streams.
    /// </summary>
    public int MaxLength { get; }

    public MaxLengthForInlineAttribute(int maxLength)
    {
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Max length must be positive");
        }

        MaxLength = maxLength;
    }
}

/// <summary>
/// Allows MySQL zero dates ('0000-00-00') to be read instead of throwing.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class AllowZeroDateAttribute : Attribute
{
}

/// <summary>
/// Applies case folding when reading text values.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class CaseFoldOnReadAttribute : Attribute
{
}

/// <summary>
/// Configures spatial type handling with SRID enforcement.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class SpatialTypeAttribute : Attribute
{
    /// <summary>
    /// Expected SRID for the spatial data. -1 means no enforcement.
    /// </summary>
    public int ExpectedSrid { get; set; } = -1;

    /// <summary>
    /// Whether to allow implicit conversion between geometry and geography.
    /// </summary>
    public bool AllowGeometryGeographyConversion { get; set; } = false;
}

/// <summary>
/// Configures currency/money type handling.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class CurrencyAttribute : Attribute
{
    /// <summary>
    /// ISO currency code (e.g., "USD", "EUR").
    /// </summary>
    public string CurrencyCode { get; }

    public CurrencyAttribute(string currencyCode)
    {
        CurrencyCode = currencyCode ?? throw new ArgumentNullException(nameof(currencyCode));
    }
}