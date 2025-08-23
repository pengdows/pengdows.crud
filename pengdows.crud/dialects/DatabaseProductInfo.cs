using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// Contains database product information detected from the connection
/// </summary>
public class DatabaseProductInfo: IDatabaseProductInfo
{
    public string ProductName { get; set; } = string.Empty;
    public string ProductVersion { get; set; } = string.Empty;
    public Version? ParsedVersion { get; set; }
    public SupportedDatabase DatabaseType { get; set; }
    public SqlStandardLevel StandardCompliance { get; set; } = SqlStandardLevel.Sql92;
}
