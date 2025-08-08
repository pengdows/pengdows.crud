#region

using System.Data;

#endregion

namespace pengdows.crud.attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute : Attribute
{
    public ColumnAttribute(string name, DbType type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }
    public DbType Type { get; }
}