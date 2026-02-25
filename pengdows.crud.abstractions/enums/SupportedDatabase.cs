namespace pengdows.crud.enums;

/// <summary>
/// Identifies a supported database product.
/// </summary>
/// <remarks>
/// <para>
/// The <c>[Flags]</c> attribute is intentional. Values can be combined with bitwise OR for
/// multi-product matching or filtering — for example:
/// <code>
/// var mysqlFamily = SupportedDatabase.MySql | SupportedDatabase.AuroraMySql | SupportedDatabase.MariaDb;
/// bool isMysqlCompatible = (mysqlFamily &amp; context.Product) != 0;
/// </code>
/// </para>
/// <para>
/// <c>Unknown = 0</c> is the correct zero value for a flags enum and means the database
/// product has not yet been detected or is not recognised.
/// </para>
/// <para>
/// Values are ordered roughly by SQL standard compliance, from most to least standard-conforming.
/// </para>
/// </remarks>
[Flags]
public enum SupportedDatabase
{
    Unknown = 0, // Always first
    PostgreSql = 1, // Most standard-compliant (closest to SQL spec)
    SqlServer = 2, // Strong compliance, with Microsoft-specific extensions
    Oracle = 4, // Largely standard but with quirks and legacy oddities
    Firebird = 8, // Good standard adherence, smaller ecosystem
    CockroachDb = 16, // Modern, Postgres-flavored, reasonable compliance
    MariaDb = 32, // Better than MySQL, but still MySQL-rooted
    MySql = 64, // Historically non-standard, improving over time
    Sqlite = 128, // Minimal subset of SQL; useful, but not standard-compliant
    DuckDB = 256, // Modern analytical database with excellent SQL:2016 compliance
    YugabyteDb = 512, // Distributed SQL database (PostgreSQL-compatible)
    TiDb = 1024, // Distributed SQL database (MySQL-compatible)
    Snowflake = 2048, // Cloud data warehouse with strong SQL:2016 compliance
    AuroraMySql = 4096, // AWS Aurora MySQL flavor
    AuroraPostgreSql = 8192 // AWS Aurora PostgreSQL flavor
}