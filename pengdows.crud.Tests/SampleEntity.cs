#region

using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.Tests;

[Table("Sample")]
public class SampleEntity
{
    [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

    [Column("MaxValue", DbType.Int32)] public int MaxValue { get; set; }

    [EnumColumn(typeof(DbMode))]
    [Column("mode", DbType.String)]
    public DbMode modeColumn { get; set; }
}