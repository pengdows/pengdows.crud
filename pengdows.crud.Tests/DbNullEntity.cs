#region

using System.Data;
using pengdows.crud.attributes;

#endregion

namespace pengdows.crud.Tests;

[Table("DbNullEntity")]
public class DbNullEntity
{
    [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

    [Column("Data", DbType.String)] public object? Data { get; set; }
}