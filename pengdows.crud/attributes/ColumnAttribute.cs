#region

using System.Data;

#endregion

namespace pengdows.crud.attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute : Attribute
{
    public ColumnAttribute(string name, DbType type, int ordinal = 0)
    {
        Name = name;
        Type = type;
        Ordinal = ordinal;
    }

    public string Name { get; }
    public DbType Type { get; }
    public int Ordinal { get; set; }
}