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
}
