using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// REV-052: the base ParseVersion takes the last dotted number in a banner, which on packaged
/// servers is an OS or package version: Ubuntu MySQL 8.0.35 parsed as 0.22.4.1, so version-gated
/// features (TIME truncation at 8.0.8, MariaDB RETURNING at 10.5, SQL Server JSON at 13) turned off.
/// </summary>
public sealed class ServerBannerVersionParsingTests
{
    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database), NullLogger.Instance);

    [Theory]
    [InlineData(SupportedDatabase.MySql, "8.0.35-0ubuntu0.22.04.1", "8.0.35")]
    [InlineData(SupportedDatabase.MySql, "8.4.2", "8.4.2")]
    [InlineData(SupportedDatabase.MySql, "8.0.36-28.1 Percona Server (GPL), Release 28, Revision 47601f19", "8.0.36")]
    // Aurora MySQL reports its own release; v3 is MySQL 8.0.23-compatible, v2 is 5.7.12.
    [InlineData(SupportedDatabase.MySql, "8.0.mysql_aurora.3.04.0", "8.0.23")]
    [InlineData(SupportedDatabase.MySql, "5.7.mysql_aurora.2.11.2", "5.7.12")]
    [InlineData(SupportedDatabase.MariaDb, "10.6.16-MariaDB-0ubuntu0.22.04.1", "10.6.16")]
    // Older MySQL-protocol clients see MariaDB's replication-compatibility prefix.
    [InlineData(SupportedDatabase.MariaDb, "5.5.5-10.11.6-MariaDB-0+deb12u1", "10.11.6")]
    [InlineData(SupportedDatabase.SqlServer,
        "Microsoft SQL Server 2019 (RTM-CU22) (KB5027702) - 15.0.4322.2 (X64) \n\tJul 27 2023 18:11:00 \n\tCopyright (C) 2019 Microsoft Corporation\n\tStandard Edition (64-bit) on Windows Server 2019 Standard 10.0 <X64> (Build 17763: ) (Hypervisor)",
        "15.0.4322.2")]
    [InlineData(SupportedDatabase.SqlServer,
        "Microsoft SQL Server 2022 (RTM-CU13) (KB5036432) - 16.0.4125.3 (X64) \n\tMay  1 2024 15:05:56 \n\tCopyright (C) 2022 Microsoft Corporation\n\tDeveloper Edition (64-bit) on Linux (Ubuntu 22.04.4 LTS) <X64>",
        "16.0.4125.3")]
    public void ParseVersion_ReadsTheServersVersion_NotThePackageOrOsVersion(SupportedDatabase database,
        string banner, string expected)
    {
        Assert.Equal(System.Version.Parse(expected), Dialect(database).ParseVersion(banner));
    }
}
