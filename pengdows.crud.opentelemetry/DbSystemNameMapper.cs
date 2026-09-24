using pengdows.crud.enums;

namespace pengdows.crud.opentelemetry;

/// <summary>
/// Maps <see cref="SupportedDatabase"/> to the OpenTelemetry semantic-convention
/// <c>db.system.name</c> registry values (https://opentelemetry.io/docs/specs/semconv/registry/attributes/db/).
/// Systems not present in the registry (e.g. Snowflake, DuckDB, YugabyteDB, TiDB) fall
/// back to a lowercase, unprefixed identifier per the spec's allowance for custom values.
/// <para>
/// <c>PengdowsMetricsObserverTests.DbSystemNameMapper_NoRealProductSilentlyFallsThroughToOtherSql</c>
/// (in <c>pengdows.crud.Tests</c>) asserts every real <see cref="SupportedDatabase"/> value has an
/// explicit case here — a value added without one compiles clean and silently resolves to the
/// generic <c>"other_sql"</c> fallback, indistinguishable from a deliberate choice.
/// </para>
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
        // Registered value, confirmed against the OTel semconv registry
        // (https://opentelemetry.io/docs/specs/semconv/registry/attributes/db/).
        SupportedDatabase.Db2 => "ibm.db2",
        SupportedDatabase.Informix => "ibm.informix",
        SupportedDatabase.SapHana => "sap.hana",
        SupportedDatabase.InterBase => "interbase",
        SupportedDatabase.Spanner => "gcp.spanner",
        // Not present in the OTel registry as of this writing; best-effort custom values.
        SupportedDatabase.SybaseASE => "sap.ase",
        SupportedDatabase.SingleStore => "singlestore",
        SupportedDatabase.FlatFile => "flatfile",
        // Not present in the OTel registry as of this writing; best-effort custom value,
        // vendor-qualified like SqlServer's "microsoft.sql_server" since Access is also
        // a Microsoft product.
        SupportedDatabase.Access => "microsoft.access",
        _ => "other_sql"
    };
}
