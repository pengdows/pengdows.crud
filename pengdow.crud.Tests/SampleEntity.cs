#region

using System.Data;
using pengdow.crud.attributes;
using pengdow.crud.enums;

#endregion

namespace pengdow.crud.Tests;

[Table("Sample")]
public class SampleEntity
{
    [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

    [Column("MaxValue", DbType.Int32)] public int MaxValue { get; set; }

    [EnumColumn(typeof(DbMode))]
    [Column("mode", DbType.String)]
    public DbMode modeColumn { get; set; }
}