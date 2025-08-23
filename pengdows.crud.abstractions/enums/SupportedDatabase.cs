namespace pengdows.crud.enums;

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
    DuckDb = 256 // Lightweight, in-process, with limited SQL support
}