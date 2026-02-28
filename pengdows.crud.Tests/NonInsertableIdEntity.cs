using System.Data;
using pengdows.crud.attributes;

namespace pengdows.crud.Tests;

[Table("NonInsertableIdEntity")]
public class NonInsertableIdEntity
{
    [Id]
    [NonInsertable]
    [NonUpdateable]
    [Column("Id", DbType.Int32)]
    public int Id { get; set; }

    [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;
}