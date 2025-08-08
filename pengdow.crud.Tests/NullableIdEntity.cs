#region
using System.Data;
using pengdow.crud.attributes;
#endregion

namespace pengdow.crud.Tests;

[Table("NullableIdEntity")]
public class NullableIdEntity
{
    [Id]
    [Column("Id", DbType.Int32)]
    public int? Id { get; set; }

    [Column("Name", DbType.String)]
    public string? Name { get; set; }
}
