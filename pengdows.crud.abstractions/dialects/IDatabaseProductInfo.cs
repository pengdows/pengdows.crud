using pengdows.crud.enums;

namespace pengdows.crud.dialects;

public interface IDatabaseProductInfo
{
    string ProductName { get; set; }
    string ProductVersion { get; set; }
    Version? ParsedVersion { get; set; }
    SupportedDatabase DatabaseType { get; set; }
    SqlStandardLevel StandardCompliance { get; set; }
}