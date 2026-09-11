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
    /// When true, throws <see cref="pengdows.crud.exceptions.DataMappingException"/> if mapping a
    /// column's value onto its matched property's setter fails (e.g. a type-coercion failure).
    /// When false (the default), the failure is only logged as a warning and that one property is
    /// left at its default value — the row is still returned, with no exception raised.
    /// </summary>
    /// <remarks>
    /// This does not govern unmatched columns/properties at all — nothing in the mapper throws
    /// for a column with no matching property or a property with no matching column, regardless
    /// of this setting; unmatched members are simply skipped. Callers who need a hard failure on
    /// any coercion problem (rather than a silently-defaulted property) should set this to true;
    /// the alternative is a per-row, best-effort mapping that never throws for this reason.
    /// </remarks>
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