#region

using System.Data;
using pengdows.crud.attributes;

#endregion

namespace testbed;

[Table("test_table")]
public class TestTable
{
[Id]
[Column("id", DbType.Int64)]
public long Id { get; set; }

    [Column("name", DbType.String)]
    [EnumColumn(typeof(NameEnum))]
    public NameEnum Name { get; set; }

    [Column("description", DbType.String)]
    public string Description { get; set; } = string.Empty;

    [Column("value", DbType.Int32)]
    public int Value { get; set; }

    [Column("is_active", DbType.Boolean)]
    public bool IsActive { get; set; } = true;

    [CreatedOn]
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }

    // Alias for integration tests that expect CreatedOn
    public DateTime CreatedOn
    {
        get => CreatedAt;
        set => CreatedAt = value;
    }

    [CreatedBy]
    [Column("created_by", DbType.String)]
    public string CreatedBy { get; set; } = string.Empty;

    [LastUpdatedOn]
    [Column("updated_at", DbType.DateTime)]
    public DateTime UpdatedAt { get; set; }

    [LastUpdatedBy]
    [Column("updated_by", DbType.String)]
    public string UpdatedBy { get; set; } = string.Empty;
}

public enum NameEnum
{
    Test,
    Test2
}