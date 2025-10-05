#region

using pengdows.crud.enums;

#endregion

namespace pengdows.crud;

public sealed record TypeCoercionOptions(TimeMappingPolicy TimePolicy, JsonPassThrough JsonPreference, SupportedDatabase Provider)
{
    public static TypeCoercionOptions Default { get; } = new(TypeMappingPolicy.PreferDateTimeOffset, JsonPassThrough.PreferDocument, SupportedDatabase.Unknown);
}
