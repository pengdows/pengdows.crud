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
/// <b>Not every database this library supports has its own enum value.</b> Two different patterns
/// exist for a managed-service or wire-compatible variant that behaves identically to an existing
/// dialect at the SQL level:
/// <list type="bullet">
///   <item><description><see cref="AuroraMySql"/>, <see cref="AuroraPostgreSql"/>, and
///   <see cref="SingleStore"/> each get their own enum value — needed to distinguish them for
///   detection/telemetry purposes — while reusing their family's existing dialect
///   <em>class</em> (no dedicated <c>AuroraMySqlDialect</c>/<c>SingleStoreDialect</c> exists).</description></item>
///   <item><description>TimescaleDB, Citus, and Fujitsu Enterprise Postgres get no separate value
///   at all — an application connecting to one of them should use <see cref="PostgreSql"/>
///   directly. Percona likewise gets no separate value — use <see cref="MySql"/> directly.</description></item>
/// </list>
/// See each individual member's remarks below for which family it belongs to.
/// </para>
/// <para>
/// This branch (<c>2.0.6</c>) keeps this enum's underlying type as the default <c>int</c>, unlike
/// pengdows.crud 3.0's <c>SupportedDatabase : ulong</c> — that widening is an ABI/reflection-shape
/// break (<c>Enum.GetUnderlyingType</c> changes) 3.0's own migration notes call out explicitly,
/// and this patch line does not make breaking changes. Every member below keeps the exact same
/// numeric value 3.0 uses, so no future member needs renumbering if the underlying type is ever
/// revisited on a major version.
/// </para>
/// </remarks>
[Flags]
public enum SupportedDatabase
{
    /// <summary>
    /// The database product has not yet been detected, or the connection uses a raw ADO.NET
    /// provider that doesn't map to any of the dialects below. Falls back to a generic,
    /// SQL-92-level dialect.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// PostgreSQL. The most standards-compliant of the supported dialects, and the dialect
    /// several wire-compatible databases (<see cref="CockroachDb"/>, <see cref="YugabyteDb"/>,
    /// <see cref="AuroraPostgreSql"/>, and <see cref="Spanner"/>'s PostgreSQL interface) build on.
    /// </summary>
    /// <remarks>
    /// <b>Also use this value for TimescaleDB, Citus, and Fujitsu Enterprise Postgres.</b> All
    /// three are PostgreSQL-compatible extensions/distributions with no dedicated
    /// <see cref="SupportedDatabase"/> value or dialect class of their own — connect to them as
    /// plain <see cref="PostgreSql"/>.
    /// </remarks>
    PostgreSql = 1,

    /// <summary>
    /// Microsoft SQL Server. Strong SQL standard compliance with T-SQL-specific extensions
    /// (<c>MERGE</c>, <c>OUTPUT</c>, <c>TOP</c>, bracket identifier quoting normalized to ANSI
    /// double-quotes via <c>QUOTED_IDENTIFIER ON</c>).
    /// </summary>
    SqlServer = 2,

    /// <summary>
    /// Oracle Database. Largely standard SQL with long-standing legacy quirks — <c>ROWNUM</c>
    /// paging, PL/SQL anonymous-block stored-procedure calls, no boolean type.
    /// </summary>
    Oracle = 4,

    /// <summary>
    /// Firebird. Good standard SQL adherence with a smaller ecosystem than the mainstream
    /// engines; open-source descendant of <see cref="InterBase"/>.
    /// </summary>
    Firebird = 8,

    /// <summary>
    /// CockroachDB. Modern distributed SQL database using the PostgreSQL wire protocol and
    /// dialect family, with reasonable standard compliance.
    /// </summary>
    CockroachDb = 16,

    /// <summary>
    /// MariaDB. A MySQL-compatible fork with generally better standards adherence than
    /// <see cref="MySql"/> itself, while remaining MySQL-rooted.
    /// </summary>
    MariaDb = 32,

    /// <summary>
    /// MySQL. Historically the least standards-compliant of the mainstream relational databases,
    /// though improving in recent versions.
    /// </summary>
    /// <remarks>
    /// <b>Also use this value for Percona Server for MySQL.</b> Percona has no dedicated
    /// <see cref="SupportedDatabase"/> value or dialect class of its own — connect to it as plain
    /// <see cref="MySql"/>.
    /// </remarks>
    MySql = 64,

    /// <summary>
    /// SQLite. Embedded, file-based (or in-memory) database with a minimal subset of SQL —
    /// useful and ubiquitous, but not standard-compliant. Coerced to <c>SingleWriter</c> (file)
    /// or <c>SingleConnection</c> (<c>:memory:</c>) connection modes to match its concurrency model.
    /// </summary>
    Sqlite = 128,

    /// <summary>
    /// DuckDB. Embedded analytical (OLAP) database with excellent SQL:2016 compliance; shares
    /// SQLite's embedded connection-mode coercion rules.
    /// </summary>
    DuckDB = 256,

    /// <summary>
    /// YugabyteDB. Distributed SQL database, PostgreSQL wire- and dialect-compatible.
    /// </summary>
    YugabyteDb = 512,

    /// <summary>
    /// TiDB. Distributed SQL database, MySQL wire- and dialect-compatible.
    /// </summary>
    TiDb = 1024,

    /// <summary>
    /// Snowflake. Cloud data warehouse with strong SQL:2016 compliance. Opt-in in the integration
    /// test matrix (<c>INCLUDE_SNOWFLAKE=true</c>) since it requires live cloud credentials rather
    /// than running in a standard Docker container.
    /// </summary>
    Snowflake = 2048,

    /// <summary>
    /// AWS Aurora MySQL. A managed AWS service with no Docker image, detected at runtime via
    /// version-string probing and routed to the <see cref="MySql"/> dialect — there is no
    /// separate Aurora MySQL dialect class.
    /// </summary>
    AuroraMySql = 4096,

    /// <summary>
    /// AWS Aurora PostgreSQL. A managed AWS service with no Docker image, detected at runtime via
    /// version-string probing and routed to the <see cref="PostgreSql"/> dialect — there is no
    /// separate Aurora PostgreSQL dialect class.
    /// </summary>
    AuroraPostgreSql = 8192,

    /// <summary>
    /// IBM Db2 (LUW). Enterprise RDBMS with strong SQL standard compliance
    /// (<c>FETCH FIRST n ROWS ONLY</c> paging, <c>CALL</c>-style stored procedures).
    /// </summary>
    Db2 = 16384,

    /// <summary>
    /// <c>pengdows.flatfile</c>'s embedded ADO.NET provider over CSV/TSV/delimited/fixed-width/
    /// NDJSON files — not a traditional RDBMS, but exposed as a dialect for the same
    /// SQL-generation pipeline.
    /// </summary>
    FlatFile = 32768,

    /// <summary>
    /// SingleStore (formerly MemSQL). Distributed, MySQL-wire-compatible database. Gets its own
    /// enum value (for detection/telemetry), but reuses <see cref="MySql"/>'s dialect class —
    /// there is no dedicated SingleStore dialect.
    /// </summary>
    SingleStore = 65536,

    /// <summary>
    /// Sybase (SAP) Adaptive Server Enterprise — T-SQL family, legacy SAP database. Named
    /// <c>Sybase</c>, not <c>SybaseASE</c>, on this branch — matches 3.0's naming/bit value, but
    /// this branch deliberately keeps the pre-3.0 name since the rename is a breaking API change
    /// this patch line does not make.
    /// </summary>
    Sybase = 131072,

    /// <summary>
    /// Google Cloud Spanner, accessed via its PostgreSQL interface (including Spanner Omni
    /// through the PGAdapter proxy). pengdows.crud has no support for Spanner's native GoogleSQL
    /// dialect — only the PostgreSQL-compatible interface.
    /// </summary>
    Spanner = 262144,

    /// <summary>
    /// IBM Informix Dynamic Server (IDS). Owner-qualified schemas and positional (<c>?</c>)
    /// parameters.
    /// </summary>
    Informix = 524288,

    /// <summary>
    /// SAP HANA. Column-store, in-memory RDBMS with positional (<c>?</c>) parameters and MVCC
    /// isolation. Opt-in in the integration test matrix (<c>INCLUDE_SAPHANA=true</c>) — a real
    /// Docker image exists, but needs 16-32GB RAM, beyond a standard CI runner.
    /// </summary>
    SapHana = 1048576,

    /// <summary>
    /// Embarcadero InterBase — <see cref="Firebird"/>'s proprietary commercial ancestor. Named
    /// (<c>@</c>) parameters, <c>ROWS</c>-based paging, <c>GEN_ID</c>-based sequence generation.
    /// Opt-in in the integration test matrix (<c>INCLUDE_INTERBASE=true</c>) — requires a
    /// personal, node-locked Developer Edition license and a native <c>libgds.so</c> on the host.
    /// </summary>
    InterBase = 2097152,

    /// <summary>
    /// Microsoft Access (Jet/ACE), via <c>System.Data.OleDb</c> and the Microsoft Access Database
    /// Engine Redistributable (<c>Microsoft.ACE.OLEDB.16.0</c>) — there is no native ADO.NET
    /// Jet/ACE client. Positional (<c>?</c>) parameters, no stored procedures, no <c>MERGE</c>.
    /// Opt-in in the integration test matrix (<c>INCLUDE_ACCESS=true</c>) — Windows-only and
    /// COM-interop-dependent (ADOX), with no Docker image at all.
    /// </summary>
    Access = 4194304
}
