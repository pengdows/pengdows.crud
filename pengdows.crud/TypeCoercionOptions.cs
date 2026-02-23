#region

using pengdows.crud.enums;

#endregion

namespace pengdows.crud;

public sealed record TypeCoercionOptions(
    TimeMappingPolicy TimePolicy,
    JsonPassThrough JsonPreference,
    SupportedDatabase Provider)
{
    public static TypeCoercionOptions Default { get; } = new(TimeMappingPolicy.PreferDateTimeOffset,
        JsonPassThrough.PreferDocument, SupportedDatabase.Unknown);
}