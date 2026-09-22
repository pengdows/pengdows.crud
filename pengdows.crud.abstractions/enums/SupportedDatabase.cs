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
    AuroraPostgreSql = 8192, // AWS Aurora PostgreSQL flavor
    Db2 = 16384, // IBM enterprise RDBMS with strong SQL standard compliance
    FlatFile = 32768, // pengdows.flatfile: embedded ADO.NET provider over CSV/TSV/delimited/fixed-width/NDJSON files
    SingleStore = 65536, // SingleStore (formerly MemSQL): distributed MySQL-wire-compatible database
    Sybase = 131072, // Sybase (SAP) Adaptive Server Enterprise — T-SQL family, legacy SAP database.
                        // Named Sybase, not bare Sybase, to disambiguate from Sybase IQ (a distinct
                        // product this dialect does not target) — matches 3.0's naming (same bit value).
    Spanner = 262144, // Google Cloud Spanner, connected via its PostgreSQL interface — same bit
                        // value as 3.0's SupportedDatabase.Spanner.
    Informix = 524288, // IBM Informix Dynamic Server — same bit value as 3.0.
    SapHana = 1048576, // SAP HANA — same bit value as 3.0.
    InterBase = 2097152 // Embarcadero InterBase, Firebird's proprietary commercial ancestor —
                        // same bit value as 3.0.

    // Access = 4194304 is deliberately NOT defined here (unlike Spanner/Informix/SapHana/
    // InterBase above, which need no changes to ISqlDialect). AccessDialect on 3.0 genuinely
    // implements 4 of the members ISqlDialect gained there (IsClientServerDatabase,
    // IsEmbeddedSingleWriterEngine, DetectInMemoryKind, CoerceConnectionMode) - adding it would
    // mean either breaking ISqlDialect on this non-breaking patch line or reimplementing Access's
    // mode-coercion against 2.0.6's older hardcoded-switch mechanism as a one-off. The bit value
    // 4194304 (1 << 22) stays reserved/unused here — being an explicit [Flags] literal rather
    // than an auto-numbered enum is exactly what makes this safe: whenever Access is added on
    // this branch, it can still take the same numeric value 3.0 uses, with no renumbering of
    // anything added after it.
}