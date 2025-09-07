#region

using System;
using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("test_entity_with_guid")]
public class TestEntityWithGuid
{
    [Id(writable: false)]
    [Column("id", DbType.Guid)]
    public Guid Id { get; set; }

    [Column("name", DbType.String)]
    public string Name { get; set; } = string.Empty;
}