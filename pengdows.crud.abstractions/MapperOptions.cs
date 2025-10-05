#region

using pengdows.crud.enums;

#endregion

namespace pengdows.crud;

public sealed record MapperOptions(
    bool Strict = false,
    bool ColumnsOnly = false,
    Func<string, string>? NamePolicy = null,
    EnumParseFailureMode EnumMode = EnumParseFailureMode.Throw,
    TypeCoercionOptions? CoercionOptions = null)
{
    public static readonly MapperOptions Default = new();

    public TypeCoercionOptions EffectiveCoercionOptions => CoercionOptions ?? TypeCoercionOptions.Default;
}

