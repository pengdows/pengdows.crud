using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// The databases that share a dialect, listed once. A test for one of these dialects takes the whole family so a member
/// added later is covered by default; a test that cannot run on a member removes it with a stated capability reason
/// (<c>DatabaseFamilies.PostgreSql.Except(new[] { SupportedDatabase.CockroachDb })</c>, CockroachDB has no range
/// types), so the exclusion is visible. <c>IntegrationFamilyCoverageGuardTests</c> fails when a test lists two members
/// of one family by hand.
/// </summary>
internal static class DatabaseFamilies
{
    /// <summary>
    /// Every database that speaks the PostgreSQL dialect: PostgreSQL itself, Aurora PostgreSQL (delegates to it),
    /// CockroachDB and YugabyteDB. Spanner's PostgreSQL interface has its own dialect and its own capability limits, so
    /// it is not a member.
    /// </summary>
    public static readonly SupportedDatabase[] PostgreSql =
    {
        SupportedDatabase.PostgreSql,
        SupportedDatabase.AuroraPostgreSql,
        SupportedDatabase.CockroachDb,
        SupportedDatabase.YugabyteDb
    };

    /// <summary>
    /// Every database that speaks the MySQL wire protocol and dialect: MySQL, Aurora MySQL (delegates to it), MariaDB,
    /// TiDB and SingleStore.
    /// </summary>
    public static readonly SupportedDatabase[] MySql =
    {
        SupportedDatabase.MySql,
        SupportedDatabase.AuroraMySql,
        SupportedDatabase.MariaDb,
        SupportedDatabase.TiDb,
        SupportedDatabase.SingleStore
    };
}
