using System.Data.Common;

namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

/// <summary>
/// Wires each pengdows.crud-supported-database family that has a viable EF Core provider to that
/// provider's own way of accepting an externally-supplied <see cref="DbConnection"/>. Every case
/// below is verified, not just compiled — each was added to <see cref="All"/> one at a time and
/// its test run to confirm the provider genuinely accepts fakeDb, catching two real
/// provider-specific issues along the way: FirebirdSql.EntityFrameworkCore.Firebird's own major
/// version doesn't track EF Core's version number (11.0.0, not 10.0.0, is the first version
/// targeting EF Core 8 — confirmed via the NuGet registration API's dependencyGroups, not
/// documentation), and SQL Server's default (non-retrying) execution strategy reclassifies the
/// interceptor's saturation TimeoutException as transient-looking and wraps it in its own
/// InvalidOperationException — see EfProviderCompatibilityTests for how the shared test accounts
/// for that without weakening what it proves for every other provider.
///
/// DuckDB and Db2 are deliberately absent, not silently skipped: DuckDB's EF Core providers are
/// either very new/unproven or read-only-only as of 2026, and IBM's Db2 provider has documented
/// compatibility breaks with EF Core 9+.
/// </summary>
public static class EfProviders
{
    /// <summary>The databases verified by <see cref="EfProviderCompatibilityTests"/>.</summary>
    public static IEnumerable<object[]> All()
    {
        yield return new object[] { SupportedDatabase.SqlServer };
        yield return new object[] { SupportedDatabase.PostgreSql };
        yield return new object[] { SupportedDatabase.MySql };
        yield return new object[] { SupportedDatabase.MariaDb };
        yield return new object[] { SupportedDatabase.Oracle };
        yield return new object[] { SupportedDatabase.Firebird };
        yield return new object[] { SupportedDatabase.Snowflake };
    }

    public static void Configure(SupportedDatabase database, DbContextOptionsBuilder builder, DbConnection connection)
    {
        switch (database)
        {
            case SupportedDatabase.SqlServer:
                builder.UseSqlServer(connection, contextOwnsConnection: false);
                break;

            case SupportedDatabase.PostgreSql:
                builder.UseNpgsql(connection, contextOwnsConnection: false);
                break;

            case SupportedDatabase.MySql:
            case SupportedDatabase.MariaDb:
                builder.UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 33)));
                break;

            case SupportedDatabase.Oracle:
                builder.UseOracle(connection, contextOwnsConnection: false);
                break;

            case SupportedDatabase.Firebird:
                builder.UseFirebird(connection);
                break;

            case SupportedDatabase.Snowflake:
                builder.UseSnowflake(connection, contextOwnsConnection: false);
                break;

            default:
                throw new NotSupportedException(
                    $"No EF Core provider is wired up for {database} in this test project.");
        }
    }
}
