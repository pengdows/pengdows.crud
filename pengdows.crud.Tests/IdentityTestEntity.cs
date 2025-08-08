#region

using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("IdentityTest")]
public class IdentityTestEntity
{
    [Id(false)] // non-writable ID (e.g., SQL Server identity)
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

    [Version] public int Version { get; set; }
}