using System;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class DbExceptionTranslatorRegistryTests
{
    [Fact]
    public void Registry_Routes_Postgres_Family_To_PostgresTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<PostgresExceptionTranslator>(registry.Get(SupportedDatabase.PostgreSql));
        Assert.IsType<PostgresExceptionTranslator>(registry.Get(SupportedDatabase.CockroachDb));
        Assert.IsType<PostgresExceptionTranslator>(registry.Get(SupportedDatabase.YugabyteDb));
        Assert.IsType<PostgresExceptionTranslator>(registry.Get(SupportedDatabase.AuroraPostgreSql));
    }

    [Fact]
    public void Registry_Routes_MySql_Family_To_MySqlTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<MySqlExceptionTranslator>(registry.Get(SupportedDatabase.MySql));
        Assert.IsType<MySqlExceptionTranslator>(registry.Get(SupportedDatabase.MariaDb));
        Assert.IsType<MySqlExceptionTranslator>(registry.Get(SupportedDatabase.AuroraMySql));
        Assert.IsType<MySqlExceptionTranslator>(registry.Get(SupportedDatabase.TiDb));
    }

    [Fact]
    public void Registry_Routes_SqlServer_To_SqlServerTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<SqlServerExceptionTranslator>(registry.Get(SupportedDatabase.SqlServer));
    }

    [Fact]
    public void Registry_Routes_Sqlite_To_SqliteTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<SqliteExceptionTranslator>(registry.Get(SupportedDatabase.Sqlite));
    }

    [Fact]
    public void Registry_Routes_Firebird_To_FallbackTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<FallbackExceptionTranslator>(registry.Get(SupportedDatabase.Firebird));
    }

    [Fact]
    public void Registry_Routes_DuckDB_To_DuckDbExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<DuckDbExceptionTranslator>(registry.Get(SupportedDatabase.DuckDB));
    }

    [Fact]
    public void Registry_Routes_Oracle_To_OracleExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<OracleExceptionTranslator>(registry.Get(SupportedDatabase.Oracle));
    }

    [Fact]
    public void FallbackTranslator_NonTimeout_Returns_DatabaseOperationException()
    {
        var translator = new FallbackExceptionTranslator();
        var inner = new InvalidOperationException("some error");

        var result = translator.Translate(SupportedDatabase.Firebird, inner, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.Equal(SupportedDatabase.Firebird, result.Database);
        Assert.Same(inner, result.InnerException);
    }

    [Fact]
    public void FallbackTranslator_TimeoutException_Returns_CommandTimeoutException()
    {
        var translator = new FallbackExceptionTranslator();
        var inner = new TimeoutException("query timed out");

        var result = translator.Translate(SupportedDatabase.DuckDB, inner, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
        Assert.Equal(SupportedDatabase.DuckDB, result.Database);
        Assert.True(result.IsTransient);
        Assert.Same(inner, result.InnerException);
    }
}
