namespace pengdow.crud.attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class PrimaryKeyAttribute : Attribute
{
    public PrimaryKeyAttribute(int order)
    {
        Order = order;
    }

    public PrimaryKeyAttribute()
    {
        Order = 0;
    }


    public int Order { get; }
}