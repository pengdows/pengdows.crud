#region

using System.Data;
using pengdows.crud.attributes;

#endregion

namespace testbed;

[Table("test_table")]
public class TestTable
{
    [Id] [Column("id", DbType.Int64)] public long Id { get; set; }

    [PrimaryKey]
    [Column("name", DbType.String)]
    [EnumColumn(typeof(NameEnum))]
    public NameEnum? Name { get; set; }

    [Column("description", DbType.String)] public string? Description { get; set; }

    [CreatedOn]
    [Column("created_at", DbType.DateTime)]
    public DateTime? CreatedAt { get; set; }

    [CreatedBy]
    [Column("created_by", DbType.String)]
    public string? CreatedBy { get; set; }

    [LastUpdatedOn]
    [Column("updated_at", DbType.DateTime)]
    public DateTime? UpdatedAt { get; set; }

    [LastUpdatedBy]
    [Column("updated_by", DbType.String)]
    public string? UpdatedBy { get; set; }
}

public enum NameEnum
{
    Test,
    Test2
}