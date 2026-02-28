#region

using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("Composite")]
public class CompositeKeyEntity
{
    [PrimaryKey(1)]
    [Column("Key1", DbType.Int32)]
    public int Key1 { get; set; }

    [PrimaryKey(2)]
    [Column("Key2", DbType.Int32)]
    public int Key2 { get; set; }

    [Column("Value", DbType.String)] public string? Value { get; set; }
}