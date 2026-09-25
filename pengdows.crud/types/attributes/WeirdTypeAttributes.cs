// =============================================================================
// FILE: WeirdTypeAttributes.cs
// PURPOSE: Attributes for configuring exotic/unusual database type handling.
//
// AI SUMMARY:
// - Collection of attributes for "weird" database type scenarios.
// - EnumStorage: Enum for Name vs Int storage strategy.
// - DbEnumAttribute: Configure enum handling (AllowUnknown, StoreAs).
// - JsonContractAttribute: Specifies JSON schema contract type for a property.
// - ConcurrencyTokenAttribute: Marks property as optimistic concurrency token.
// - RangeTypeAttribute: Configures PostgreSQL range type formatting.
// - ComputedAttribute: Marks computed/generated columns (Stored vs virtual).
// - CaseInsensitiveAttribute: Documents case-insensitive text behavior.
// - AsStringAttribute: Forces numeric to string when precision exceeds .NET limits.
// - MaxLengthForInlineAttribute: Controls binary data memory allocation strategy.
// - AllowZeroDateAttribute: Allows MySQL '0000-00-00' zero dates.
// - CaseFoldOnReadAttribute: Applies case folding when reading text values.
// - SpatialTypeAttribute: Configures spatial SRID enforcement and conversion.
// - CurrencyAttribute: Configures ISO currency code for money types.
// - NOTE: nothing in pengdows.crud currently reads any of these attributes (or EnumStorage);
//   they have no runtime effect, and the descriptions below state intended use only.
// =============================================================================

namespace pengdows.crud.types.attributes;

/// <summary>
/// Specifies how an enum should be stored in the database.
/// </summary>
[Obsolete("EnumStorage is inert and retained for 2.x compatibility; use ColumnAttribute.DbType or EnumColumnAttribute.", false)]
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
[Obsolete("DbEnumAttribute is inert and retained for 2.x compatibility; use EnumColumnAttribute.", false)]
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
[Obsolete("JsonContractAttribute is inert and retained for 2.x compatibility; use JsonAttribute and CLR JSON type mapping.", false)]
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
[Obsolete("ConcurrencyTokenAttribute is inert and retained for 2.x compatibility; use VersionAttribute.", false)]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ConcurrencyTokenAttribute : Attribute
{
}

/// <summary>
/// Configures range type formatting and validation.
/// </summary>
[Obsolete("RangeTypeAttribute is inert and retained for 2.x compatibility; use the mapped range CLR type and dialect behavior.", false)]
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
[Obsolete("ComputedAttribute is inert and retained for 2.x compatibility; use the supported column mapping attributes.", false)]
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
[Obsolete("CaseInsensitiveAttribute is inert and retained for 2.x compatibility; configure collation in the database.", false)]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class CaseInsensitiveAttribute : Attribute
{
}

/// <summary>
/// Forces a numeric type to be stored/read as a string when precision exceeds .NET limits.
/// </summary>
[Obsolete("AsStringAttribute is inert and retained for 2.x compatibility; use an explicit CLR/string mapping.", false)]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class AsStringAttribute : Attribute
{
}

/// <summary>
/// Controls memory allocation strategy for binary data.
/// </summary>
[Obsolete("MaxLengthForInlineAttribute is inert and retained for 2.x compatibility; use the supported binary mapping APIs.", false)]
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
[Obsolete("AllowZeroDateAttribute is inert and retained for 2.x compatibility; configure zero-date handling in the provider.", false)]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class AllowZeroDateAttribute : Attribute
{
}

/// <summary>
/// Applies case folding when reading text values.
/// </summary>
[Obsolete("CaseFoldOnReadAttribute is inert and retained for 2.x compatibility; normalize values explicitly.", false)]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class CaseFoldOnReadAttribute : Attribute
{
}

/// <summary>
/// Configures spatial type handling with SRID enforcement.
/// </summary>
[Obsolete("SpatialTypeAttribute is inert and retained for 2.x compatibility; use Geometry or Geography mapping and explicit SRID values.", false)]
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
[Obsolete("CurrencyAttribute is inert and retained for 2.x compatibility; model currency explicitly.", false)]
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
