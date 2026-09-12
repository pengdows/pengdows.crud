namespace pengdows.crud.enums;

/// <summary>
/// Identifies a supported database product.
/// </summary>
/// <remarks>
/// <para>
/// The <c>[Flags]</c> attribute is intentional. Values can be combined with bitwise OR for
/// multi-product matching or filtering — for example,
/// <code>
/// var mysqlFamily = SupportedDatabase.MySql | SupportedDatabase.AuroraMySql | SupportedDatabase.MariaDb;
/// bool isMysqlCompatible = (mysqlFamily &amp; context.Product) != 0;
/// </code>
/// </para>
/// <para>
/// <c>Unknown = 0</c> is the correct zero value for a flags enum and means the database
/// product has not yet been detected or is not recognised.
/// </para>
/// </remarks>
[Flags]
public enum SupportedDatabase : ulong
{
    Unknown = 0,// Always first

    PostgreSql       = 1UL << 0,  // Most standard-compliant (closest to SQL spec)
    SqlServer        = 1UL << 1,  // Strong compliance, with Microsoft-specific extensions
    Oracle           = 1UL << 2,  // Largely standard but with quirks and legacy oddities
    Firebird         = 1UL << 3,  // Good standard adherence, smaller ecosystem
    CockroachDb      = 1UL << 4,  // Modern, Postgres-flavored, reasonable compliance
    MariaDb          = 1UL << 5,  // Better than MySQL, but still MySQL-rooted
    MySql            = 1UL << 6,  // Historically non-standard, improving over time
    Sqlite           = 1UL << 7,  // Minimal subset of SQL; useful, but not standard-compliant
    DuckDB           = 1UL << 8,  // Modern analytical database with excellent SQL:2016 compliance
    YugabyteDb       = 1UL << 9,  // Distributed SQL database (PostgreSQL-compatible)
    TiDb             = 1UL << 10, // Distributed SQL database (MySQL-compatible)
    Snowflake        = 1UL << 11, // Cloud data warehouse with strong SQL:2016 compliance
    AuroraMySql      = 1UL << 12, // AWS Aurora MySQL flavor
    AuroraPostgreSql = 1UL << 13, // AWS Aurora PostgreSQL flavor
    Db2              = 1UL << 14, // IBM enterprise RDBMS with strong SQL standard compliance
    FlatFile         = 1UL << 15, // pengdows.flatfile: embedded ADO.NET provider over CSV/TSV/delimited/fixed-width/NDJSON files
    SingleStore      = 1UL << 16, // SingleStore (formerly MemSQL): distributed MySQL-wire-compatible database
    SybaseASE        = 1UL << 17, // Sybase (SAP) Adaptive Server Enterprise — T-SQL family, legacy SAP database
    Spanner          = 1UL << 18, // Google Cloud Spanner PostgreSQL interface (including Spanner Omni via PGAdapter)
    Informix         = 1UL << 19, // IBM Informix Dynamic Server (IDS) — owner-qualified schemas, positional (?) parameters
    SapHana          = 1UL << 20 // SAP HANA — column-store in-memory RDBMS, positional (?) parameters, MVCC isolation
}