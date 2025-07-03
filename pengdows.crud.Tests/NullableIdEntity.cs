#region
using System.Data;
using pengdows.crud.attributes;
#endregion

namespace pengdows.crud.Tests;

[Table("NullableIdEntity")]
public class NullableIdEntity
{
    [Id]
    [Column("Id", DbType.Int32)]
    public int? Id { get; set; }

    [Column("Name", DbType.String)]
    public string? Name { get; set; }
}
