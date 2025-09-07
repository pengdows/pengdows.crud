#region

using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("test_simple")]
public class TestEntitySimple
{
    [Id(writable: false)]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("name", DbType.String)]
    public string Name { get; set; } = string.Empty;
}