#region

using System;
using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("Test")]
public class TestEntity
{
    [Id(false)]
    [Column("Id", DbType.Int32)]
    public int Id { get; set; }


    [PrimaryKey]
    [Column("Name", DbType.String)]
    public string Name { get; set; }

    [CreatedOn]
    [Column("CreatedOn", DbType.DateTime)]
    public DateTime CreatedOn { get; set; }

    [CreatedBy]
    [Column("CreatedBy", DbType.String)]
    public string CreatedBy { get; set; }

    [LastUpdatedOn]
    [Column("LastUpdatedOn", DbType.DateTime)]
    public DateTime LastUpdatedOn { get; set; }

    [LastUpdatedBy]
    [Column("LastUpdatedBy", DbType.String)]
    public string LastUpdatedBy { get; set; }

    [Version]
    [Column("Version", DbType.Int32)]
    public int version { get; set; }
}