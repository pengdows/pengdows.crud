#region

using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("test_entity_with_default_id")]
public class TestEntityWithDefaultId
{
    [Id(writable: false)]
    [Column("id", DbType.Int32)]
    public int Id { get; set; } = 99; // Default value

    [Column("name", DbType.String)]
    public string Name { get; set; } = string.Empty;
}