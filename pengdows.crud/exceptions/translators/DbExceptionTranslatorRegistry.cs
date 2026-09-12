using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal sealed class DbExceptionTranslatorRegistry : IDbExceptionTranslatorRegistry
{
    private static readonly IDbExceptionTranslator SqlServer = new SqlServerExceptionTranslator();
    private static readonly IDbExceptionTranslator Postgres = new PostgresExceptionTranslator();
    private static readonly IDbExceptionTranslator MySql = new MySqlExceptionTranslator();
    private static readonly IDbExceptionTranslator Sqlite = new SqliteExceptionTranslator();
    private static readonly IDbExceptionTranslator DuckDb = new DuckDbExceptionTranslator();
    private static readonly IDbExceptionTranslator Oracle = new OracleExceptionTranslator();
    private static readonly IDbExceptionTranslator Firebird = new FirebirdExceptionTranslator();
    private static readonly IDbExceptionTranslator Db2 = new Db2ExceptionTranslator();
    private static readonly IDbExceptionTranslator Snowflake = new SnowflakeExceptionTranslator();
    private static readonly IDbExceptionTranslator Sybase = new SybaseExceptionTranslator();
    private static readonly IDbExceptionTranslator Informix = new InformixExceptionTranslator();
    private static readonly IDbExceptionTranslator Hana = new HanaExceptionTranslator();
    private static readonly IDbExceptionTranslator Fallback = new FallbackExceptionTranslator();

    public IDbExceptionTranslator Get(SupportedDatabase database)
    {
        return database switch
        {
            SupportedDatabase.SqlServer => SqlServer,
            SupportedDatabase.SybaseASE => Sybase,
            SupportedDatabase.PostgreSql or SupportedDatabase.Spanner or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb or
                SupportedDatabase.AuroraPostgreSql => Postgres,
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.AuroraMySql or
                SupportedDatabase.TiDb or SupportedDatabase.SingleStore => MySql,
            SupportedDatabase.Sqlite => Sqlite,
            SupportedDatabase.DuckDB => DuckDb,
            SupportedDatabase.Oracle => Oracle,
            SupportedDatabase.Firebird => Firebird,
            SupportedDatabase.Db2 => Db2,
            SupportedDatabase.Snowflake => Snowflake,
            SupportedDatabase.Informix => Informix,
            SupportedDatabase.SapHana => Hana,
            _ => Fallback
        };
    }
}
