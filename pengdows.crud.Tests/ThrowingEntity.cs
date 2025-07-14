#region

using System;
using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("Throwing")]
public class ThrowingEntity
{
    [Id(false)]
    [Column("Id", DbType.Int32)]
    public int Id { get; set; }

    [Column("Name", DbType.String)]
    public string Name
    {
        get => string.Empty;
        set => throw new InvalidOperationException("boom");
    }
}