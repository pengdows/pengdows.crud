using System;

namespace pengdows.crud.attributes;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EnumLiteralAttribute : Attribute
{
    public EnumLiteralAttribute(string literal)
    {
        Literal = literal ?? throw new ArgumentNullException(nameof(literal));
    }

    public string Literal { get; }
}
