using System.Data;
using pengdows.crud.attributes;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Entity with "evil" identifier names designed to break unquoted or poorly quoted SQL.
/// Tests reserved words, spaces, and mixed case.
/// </summary>
[Table("Default Order")]
public class TortureEntity
{
    [Id]
    [Column("Group By", DbType.Int64)]
    public long Id { get; set; }

    [Column("Select", DbType.String)]
    public string SelectValue { get; set; } = string.Empty;

    [Column("From", DbType.String)]
    public string FromValue { get; set; } = string.Empty;

    [Column("User Name", DbType.String)]
    public string MixedCase { get; set; } = string.Empty;
}
