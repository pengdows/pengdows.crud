#region

using System.Data;
using pengdows.crud.attributes;
#endregion

namespace pengdows.crud.Tests;

[Table("NoKey")]
public class NoKeyEntity
{
    [Column("Value", DbType.String)]
    public string Value { get; set; }
}
