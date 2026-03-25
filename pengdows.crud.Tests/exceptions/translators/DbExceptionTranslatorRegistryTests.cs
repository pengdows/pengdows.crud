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
    public void Registry_Routes_Firebird_To_FirebirdExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<FirebirdExceptionTranslator>(registry.Get(SupportedDatabase.Firebird));
    }

    [Theory]
    [InlineData("violation of PRIMARY or UNIQUE KEY constraint \"PK_x\" on table \"Job\"")]
    [InlineData("violation of UNIQUE KEY constraint \"UQ_Job_State\" on table \"Job\"")]
    public void FirebirdTranslator_UniqueConstraintViolation_Returns_UniqueConstraintViolationException(string message)
    {
        var translator = new FirebirdExceptionTranslator();
        var inner = new InvalidOperationException(message);

        var result = translator.Translate(SupportedDatabase.Firebird, inner, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void FirebirdTranslator_UniqueViolation_WithTimeoutInKeyValue_IsNotMisclassifiedAsTimeout()
    {
        // Regression: distributed lock resources like "lock-timeout-{guid}" embed "timeout"
        // in the key value, which appears in the Firebird exception message. The unique
        // constraint check must run before LooksLikeTimeout so the PK violation is not
        // swallowed as a CommandTimeoutException.
        const string message =
            "violation of PRIMARY or UNIQUE KEY constraint \"PK_HangFire_hf_lock\" on table \"hf_lock\"\n" +
            "Problematic key value is (\"resource\" = 'lock-timeout-abc123')";
        var translator = new FirebirdExceptionTranslator();
        var inner = new InvalidOperationException(message);

        var result = translator.Translate(SupportedDatabase.Firebird, inner, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void FirebirdTranslator_GenericError_Returns_DatabaseOperationException()
    {
        var translator = new FirebirdExceptionTranslator();
        var inner = new InvalidOperationException("some unrecognized Firebird error");

        var result = translator.Translate(SupportedDatabase.Firebird, inner, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
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
