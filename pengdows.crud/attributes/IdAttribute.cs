namespace pengdows.crud.attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class IdAttribute : Attribute
{
    public IdAttribute(bool writable = true)
    {
        Writable = writable;
    }

    /// <summary>
    /// Indicates whether the ID field is writable (e.g., not a SQL Server identity column).
    /// </summary>
    public bool Writable { get; }
}