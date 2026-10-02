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

    /// <summary>The coercion options a dialect's values are read with.</summary>
    internal static TypeCoercionOptions For(dialects.ISqlDialect dialect) => Default with
    {
        Provider = dialect is dialects.SqlDialect sqlDialect ? sqlDialect.TypeMappingProvider : dialect.DatabaseType,
        GuidBytesBigEndian = dialect is not dialects.SqlDialect { StoresGuidBytesBigEndian: false }
    };
}
