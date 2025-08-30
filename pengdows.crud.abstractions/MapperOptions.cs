#region

using System;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud;

public sealed record MapperOptions(
    bool Strict = false,
    bool ColumnsOnly = false,
    Func<string, string>? NamePolicy = null,
    EnumParseFailureMode EnumMode = EnumParseFailureMode.Throw)
{
    public static readonly MapperOptions Default = new();
}

