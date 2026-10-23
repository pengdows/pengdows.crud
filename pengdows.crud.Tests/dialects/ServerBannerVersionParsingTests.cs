using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// The base ParseVersion takes the last dotted number in a banner, which on most real servers is
/// an OS, package, compiler or toolchain version: SQL Server on Ubuntu 22.04 parsed as 22.4.5,
/// PostgreSQL 18 built with gcc 14 as 14.2.0, CockroachDB as its Go version. Version gates (MERGE,
/// JSON, RETURNING) and any minimum-version check must see the server's own version.
/// Banners marked "live" were captured from the official images.
/// </summary>
public sealed class ServerBannerVersionParsingTests
{
    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database), NullLogger.Instance);

    [Theory]
    // live: postgres:15, postgres:16-alpine, postgres:17
    [InlineData(SupportedDatabase.PostgreSql, "PostgreSQL 15.19 (Debian 15.19-1.pgdg13+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit", "15.19")]
    [InlineData(SupportedDatabase.PostgreSql, "PostgreSQL 16.15 on x86_64-pc-linux-musl, compiled by gcc (Alpine 15.2.0) 15.2.0, 64-bit", "16.15")]
    [InlineData(SupportedDatabase.PostgreSql, "PostgreSQL 17.11 (Debian 17.11-1.pgdg13+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit", "17.11")]
    [InlineData(SupportedDatabase.PostgreSql, "PostgreSQL 14.12 on x86_64-pc-linux-musl, compiled by gcc (Alpine 15.2.0) 15.2.0, 64-bit", "14.12")]
    [InlineData(SupportedDatabase.PostgreSql, "PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit", "18.6")]
    // live: mssql/server 2017, 2019, 2022, 2025 (Linux)
    [InlineData(SupportedDatabase.SqlServer, "Microsoft SQL Server 2017 (RTM-CU31-GDR) (KB5122774) - 14.0.3550.4 (X64) \n\tAug 21 2026 13:00:28 \n\tCopyright (C) 2017 Microsoft Corporation\n\tDeveloper Edition (64-bit) on Linux (Ubuntu 18.04.6 LTS)", "14.0.3550.4")]
    [InlineData(SupportedDatabase.SqlServer, "Microsoft SQL Server 2019 (RTM-CU32-GDR) (KB5122772) - 15.0.4490.9 (X64) \n\tAug 21 2026 12:54:17 \n\tCopyright (C) 2019 Microsoft Corporation\n\tDeveloper Edition (64-bit) on Linux (Ubuntu 20.04.6 LTS) <X64>", "15.0.4490.9")]
    [InlineData(SupportedDatabase.SqlServer, "Microsoft SQL Server 2022 (RTM-CU27) (KB5104824) - 16.0.4295.3 (X64) \n\tAug 26 2026 11:02:22 \n\tCopyright (C) 2022 Microsoft Corporation\n\tDeveloper Edition (64-bit) on Linux (Ubuntu 22.04.5 LTS) <X64>", "16.0.4295.3")]
    [InlineData(SupportedDatabase.SqlServer, "Microsoft SQL Server 2025 (RTM-CU9) (KB5122048) - 17.0.5005.3 (X64) \n\tAug 27 2026 09:30:16 \n\tCopyright (C) 2025 Microsoft Corporation\n\tEnterprise Developer Edition (64-bit) on Linux (Ubuntu 22.04.5 LTS) <X64>", "17.0.5005.3")]
    [InlineData(SupportedDatabase.SqlServer, "Microsoft SQL Server 2019 (RTM-CU22) (KB5027702) - 15.0.4322.2 (X64) \n\tJul 27 2023 18:11:00 \n\tCopyright (C) 2019 Microsoft Corporation\n\tStandard Edition (64-bit) on Windows Server 2019 Standard 10.0 <X64> (Build 17763: ) (Hypervisor)", "15.0.4322.2")]
    // Azure SQL Database and Managed Instance report a fixed 12.0 build; they are versionless and
    // at least SQL Server 2022 (16.0) in features, so they parse as 16.0 rather than below 2016.
    [InlineData(SupportedDatabase.SqlServer, "Microsoft SQL Azure (RTM) - 12.0.2000.8 \n\tSep 18 2024 11:58:52 \n\tCopyright (C) 2022 Microsoft Corporation\n", "16.0")]
    // live: cockroachdb/cockroach:latest
    [InlineData(SupportedDatabase.CockroachDb, "CockroachDB CCL v26.3.2 (x86_64-pc-linux-gnu, built 2026/09/16 12:26:01, go1.26.6)", "26.3.2")]
    // live: mysql:8.0, mysql:8.4, mariadb:10.11, mariadb:11.4; distro packages append their own version.
    [InlineData(SupportedDatabase.MySql, "8.0.46", "8.0.46")]
    [InlineData(SupportedDatabase.MySql, "8.4.11", "8.4.11")]
    [InlineData(SupportedDatabase.MySql, "8.0.35-0ubuntu0.22.04.1", "8.0.35")]
    [InlineData(SupportedDatabase.MySql, "8.0.36-28.1 Percona Server (GPL), Release 28, Revision 47601f19", "8.0.36")]
    // Aurora MySQL reports its own release; v3 is MySQL 8.0.23-compatible, v2 is 5.7.12.
    [InlineData(SupportedDatabase.MySql, "8.0.mysql_aurora.3.04.0", "8.0.23")]
    [InlineData(SupportedDatabase.MySql, "5.7.mysql_aurora.2.11.2", "5.7.12")]
    [InlineData(SupportedDatabase.MariaDb, "10.11.16-MariaDB-ubu2204", "10.11.16")]
    [InlineData(SupportedDatabase.MariaDb, "11.4.13-MariaDB-ubu2404", "11.4.13")]
    [InlineData(SupportedDatabase.MariaDb, "10.6.16-MariaDB-0ubuntu0.22.04.1", "10.6.16")]
    // Older MySQL-protocol clients see MariaDB's replication-compatibility prefix.
    [InlineData(SupportedDatabase.MariaDb, "5.5.5-10.11.6-MariaDB-0+deb12u1", "10.11.6")]
    // live: pingcap/tidb. TiDB's gates follow the TiDB release, not the MySQL version it imitates.
    [InlineData(SupportedDatabase.TiDb, "8.0.11-TiDB-v7.5.1", "7.5.1")]
    // live: gvenzl/oracle-free:23.26.2. Oracle's five-part release outgrows System.Version.
    [InlineData(SupportedDatabase.Oracle, "Oracle AI Database 26ai Free Release 23.26.2.0.0 - Develop, Learn, and Run for Free", "23.26.2.0")]
    [InlineData(SupportedDatabase.Oracle, "Oracle Database 19c Enterprise Edition Release 19.0.0.0.0 - Production", "19.0.0.0")]
    // live: container-registry.oracle.com/database/enterprise:12.2.0.1-slim (the 12.2 floor)
    [InlineData(SupportedDatabase.Oracle, "Oracle Database 12c Enterprise Edition Release 12.2.0.1.0 - 64bit Production", "12.2.0.1")]
    public void ParseVersion_ReadsTheServersVersion_NotTheOsCompilerOrPackageVersion(SupportedDatabase database,
        string banner, string expected)
    {
        Assert.Equal(System.Version.Parse(expected), Dialect(database).ParseVersion(banner));
    }
}
