using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal sealed class DbExceptionTranslatorRegistry : IDbExceptionTranslatorRegistry
{
    private static readonly IDbExceptionTranslator SqlServer = new SqlServerExceptionTranslator();
    private static readonly IDbExceptionTranslator Postgres = new PostgresExceptionTranslator();
    private static readonly IDbExceptionTranslator MySql = new MySqlExceptionTranslator();
    private static readonly IDbExceptionTranslator Sqlite = new SqliteExceptionTranslator();
    private static readonly IDbExceptionTranslator Fallback = new FallbackExceptionTranslator();

    public IDbExceptionTranslator Get(SupportedDatabase database)
    {
        return database switch
        {
            SupportedDatabase.SqlServer => SqlServer,
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb or
                SupportedDatabase.AuroraPostgreSql => Postgres,
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.AuroraMySql or
                SupportedDatabase.TiDb => MySql,
            SupportedDatabase.Sqlite => Sqlite,
            SupportedDatabase.Firebird or SupportedDatabase.DuckDB => Fallback,
            _ => Fallback
        };
    }
}
