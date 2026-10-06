#region

using pengdows.crud.enums;
using pengdows.crud.infrastructure;

#endregion

namespace pengdows.crud;

[Obsolete("TypeCoercionOptions is retained for 2.x compatibility and is not supported application API.", false)]
public sealed record TypeCoercionOptions(
    TimeMappingPolicy TimePolicy,
    JsonPassThrough JsonPreference,
    SupportedDatabase Provider)
{
    public static TypeCoercionOptions Default { get; } = new(TimeMappingPolicy.PreferDateTimeOffset,
        JsonPassThrough.PreferDocument, SupportedDatabase.Unknown);

    /// <summary>
    /// Byte order of a Guid stored as 16 bytes (<c>SqlDialect.StoresGuidBytesBigEndian</c>): RFC 4122
    /// big-endian when true, .NET's mixed-endian <see cref="Guid.ToByteArray()"/> order when false.
    /// </summary>
    internal bool GuidBytesBigEndian { get; init; }

    /// <summary>
    /// The driver reports offset timestamps as DateTime but returns the DateTimeOffset from GetValue
    /// (<c>SqlDialect.ReportsOffsetTimestampsAsDateTime</c>).
    /// </summary>
    internal bool ReadsOffsetTimestampsFromValue { get; init; }

    /// <summary>
    /// The provider data type name of an offset timestamp column whose GetValue may drop the offset
    /// (<c>SqlDialect.OffsetTimestampDataTypeName</c>); null when there is none.
    /// </summary>
    internal string? OffsetTimestampDataTypeName { get; init; }

    /// <summary>
    /// A DateTimeOffset property on a column reported as DateTime reads the column's value
    /// (<see cref="pengdows.crud.@internal.OffsetTimestampFieldReader"/>), not its DateTime.
    /// </summary>
    internal bool ReadsOffsetTimestampsSpecially => ReadsOffsetTimestampsFromValue || OffsetTimestampDataTypeName != null;

    /// <summary>
    /// Collection columns arrive as literal text (<c>SqlDialect.ReturnsCollectionsAsLiteralText</c>).
    /// </summary>
    internal bool ReadsCollectionLiterals { get; init; }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<dialects.ISqlDialect, TypeCoercionOptions>
        ByDialect = new();

    /// <summary>
    /// The coercion options a dialect's values are read with: one instance per dialect, so callers
    /// that key caches by options compare by reference on the hot path (REV-033).
    /// </summary>
    internal static TypeCoercionOptions For(dialects.ISqlDialect dialect) =>
        ByDialect.GetValue(dialect, static d => Build(d));

    private static TypeCoercionOptions Build(dialects.ISqlDialect dialect) => Default with
    {
        Provider = dialect is dialects.SqlDialect sqlDialect ? sqlDialect.TypeMappingProvider : dialect.DatabaseType,
        GuidBytesBigEndian = dialect is not dialects.SqlDialect { StoresGuidBytesBigEndian: false },
        ReadsOffsetTimestampsFromValue = dialect is dialects.SqlDialect { ReportsOffsetTimestampsAsDateTime: true },
        OffsetTimestampDataTypeName = (dialect as dialects.SqlDialect)?.OffsetTimestampDataTypeName,
        ReadsCollectionLiterals = dialect is dialects.SqlDialect { ReturnsCollectionsAsLiteralText: true }
    };
}
