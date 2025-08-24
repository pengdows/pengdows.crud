#region
using System;
using System.Data;
using pengdows.crud.attributes;
#endregion

namespace pengdows.crud.Tests;

[Table("NoKey")]
public class NoKeyEntity
{
    [Id(false)]
    [Column("Id", DbType.Int32)]
    public int Id { get; set; }

    [Column("Value", DbType.String)]
    public string Value { get; set; }
}
