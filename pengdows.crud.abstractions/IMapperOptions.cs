using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud;

/// <summary>
/// Configuration for <see cref="IDataReaderMapper"/> controlling how results
/// are mapped to entity types.
/// </summary>
public interface IMapperOptions
{
    /// <summary>
    /// When true, throws a <c>DataMappingException</c> if a column's value fails to map to its
    /// matching property; when false, the failure is logged and the property is left unset.
    /// Result-set columns with no matching property are ignored either way.
    /// </summary>
    bool Strict { get; }

    /// <summary>
    /// When true, only maps properties decorated with <c>[Column]</c>, matched by the attribute's
    /// column name (and <see cref="NamePolicy"/> is not applied). When false, matches result-set
    /// columns to public settable properties by property name.
    /// </summary>
    bool ColumnsOnly { get; }

    /// <summary>
    /// Optional name transformation policy applied to column names before matching.
    /// Ignored when <see cref="ColumnsOnly"/> is true.
    /// </summary>
    Func<string, string>? NamePolicy { get; }

    /// <summary>
    /// Controls behavior when enum parsing fails.
    /// </summary>
    EnumParseFailureMode EnumMode { get; }
}