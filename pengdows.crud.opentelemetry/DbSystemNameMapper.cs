using pengdows.crud.enums;

namespace pengdows.crud.opentelemetry;

/// <summary>
/// Maps <see cref="SupportedDatabase"/> to the OpenTelemetry semantic-convention
/// <c>db.system.name</c> registry values (https://opentelemetry.io/docs/specs/semconv/registry/attributes/db/).
/// Systems not present in the registry (e.g. Snowflake, DuckDB, YugabyteDB, TiDB) fall
/// back to a lowercase, unprefixed identifier per the spec's allowance for custom values.
/// </summary>
internal static class DbSystemNameMapper
{
    public static string Map(SupportedDatabase product) => product switch
    {
        SupportedDatabase.PostgreSql => "postgresql",
        SupportedDatabase.SqlServer => "microsoft.sql_server",
        SupportedDatabase.Oracle => "oracle.db",
        SupportedDatabase.Firebird => "firebirdsql",
        SupportedDatabase.CockroachDb => "cockroachdb",
        SupportedDatabase.MariaDb => "mariadb",
        SupportedDatabase.MySql => "mysql",
        SupportedDatabase.Sqlite => "sqlite",
        // Wire-compatible with the engine they emulate — no distinct registry value exists.
        SupportedDatabase.AuroraMySql => "mysql",
        SupportedDatabase.AuroraPostgreSql => "postgresql",
        // Not present in the OTel registry as of this writing; best-effort custom values.
        SupportedDatabase.DuckDB => "duckdb",
        SupportedDatabase.YugabyteDb => "yugabytedb",
        SupportedDatabase.TiDb => "tidb",
        SupportedDatabase.Snowflake => "snowflake",
        _ => "other_sql"
    };
}
