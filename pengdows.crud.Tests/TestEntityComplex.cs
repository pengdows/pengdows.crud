#region

using System;
using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("test_entity_complex")]
public class TestEntityComplex
{
    [Id(false)]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

    [Column("created_on", DbType.DateTime)]
    public DateTime CreatedOn { get; set; }

    [Column("is_active", DbType.Boolean)] public bool IsActive { get; set; }

    [Column("score", DbType.Decimal)] public decimal Score { get; set; }
}