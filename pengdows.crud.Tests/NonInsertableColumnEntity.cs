using System.Data;
using pengdows.crud.attributes;

namespace pengdows.crud.Tests;

[Table("NonInsertableColumnEntity")]
public class NonInsertableColumnEntity
{
    [Id]
    [Column("Id", DbType.Int32)]
    public int Id { get; set; }

    [Column("Name", DbType.String)]
    public string Name { get; set; } = string.Empty;

    [NonInsertable]
    [NonUpdateable]
    [Column("Secret", DbType.String)]
    public string? Secret { get; set; }
}
