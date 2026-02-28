#region

using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("test_entity_with_writable_id")]
public class TestEntityWithWritableId
{
    [Id(true)]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
}