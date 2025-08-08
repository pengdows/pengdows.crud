namespace pengdow.crud.attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class EnumColumnAttribute : Attribute
{
    public EnumColumnAttribute(Type enumType)
    {
        if (!enumType.IsEnum)
        {
            throw new ArgumentException("Provided type must be an enum", nameof(enumType));
        }
        EnumType = enumType;
    }

    public Type EnumType { get; }
}