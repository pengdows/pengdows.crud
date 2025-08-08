namespace pengdows.crud.attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TableAttribute : Attribute
{
    public TableAttribute(string name, string? schema = null)
    {
        Name = name;
        Schema = schema;
    }

    public string Name { get; }
    public string? Schema { get; }
}