using System;
using System.Data;
using pengdow.crud.attributes;

namespace pengdow.crud.Tests;

[Table("AuditOnOnlyEntity")]
public class AuditOnOnlyEntity
{
    [Id(false)]
    [Column("Id", DbType.Int32)]
    public int Id { get; set; }

    [Column("Name", DbType.String)]
    public string Name { get; set; } = string.Empty;

    [CreatedOn]
    [Column("CreatedOn", DbType.DateTime)]
    public DateTime CreatedOn { get; set; }

    [LastUpdatedOn]
    [Column("LastUpdatedOn", DbType.DateTime)]
    public DateTime LastUpdatedOn { get; set; }
}
