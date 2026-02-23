using pengdows.crud.enums;

namespace pengdows.crud;

/// <summary>
/// Configuration for <see cref="IDataReaderMapper"/> controlling how results
/// are mapped to entity types.
/// </summary>
public interface IMapperOptions
{
    /// <summary>
    /// When true, throws if a column in the result set has no matching property.
    /// </summary>
    bool Strict { get; }

    /// <summary>
    /// When true, only maps columns that exist in the result set (ignores unmapped properties).
    /// </summary>
    bool ColumnsOnly { get; }

    /// <summary>
    /// Optional name transformation policy applied to column names before matching.
    /// </summary>
    Func<string, string>? NamePolicy { get; }

    /// <summary>
    /// Controls behavior when enum parsing fails.
    /// </summary>
    EnumParseFailureMode EnumMode { get; }
}