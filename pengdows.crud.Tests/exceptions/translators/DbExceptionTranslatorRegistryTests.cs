using System;
using System.Collections.Generic;
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
    public void Registry_Routes_Db2_To_Db2ExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<Db2ExceptionTranslator>(registry.Get(SupportedDatabase.Db2));
    }

    // Databases that intentionally have no dedicated translator and fall back to the generic
    // FallbackExceptionTranslator. Adding a SupportedDatabase value here is a REVIEWABLE decision,
    // not a silent default — see Registry_HasExplicitRoutingDecision_ForEveryDatabase below.
    //   - Unknown: not a real database, never a live connection target.
    //   - FlatFile: pengdows.flatfile has no custom exception hierarchy at all — it throws plain
    //     BCL exceptions (FormatException, InvalidOperationException, IOException, ...) with no
    //     provider error codes or SQLSTATEs to pattern-match. There is nothing real to build a
    //     dedicated IDbExceptionTranslator against yet; revisit once/if flatfile grows typed
    //     exceptions for constraint/format/IO failures.
    private static readonly HashSet<SupportedDatabase> IntentionalFallbackDatabases = new()
    {
        SupportedDatabase.Unknown,
        SupportedDatabase.FlatFile
    };

    [Fact]
    public void Registry_Routes_Snowflake_To_SnowflakeExceptionTranslator()
    {
        // Snowflake parses UNIQUE/PRIMARY KEY/FOREIGN KEY/CHECK constraint DDL but never enforces
        // any of them at runtime (SnowflakeDialect.EnforcesConstraints etc. = false) — but NOT
        // NULL *is* enforced (error 100072, SQLSTATE 23502), so it has a real, narrow,
        // provider-specific mapping worth maintaining, unlike the rest of the constraint family.
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<SnowflakeExceptionTranslator>(registry.Get(SupportedDatabase.Snowflake));
    }

    [Fact]
    public void SnowflakeTranslator_NotNullSqlState_Returns_NotNullViolationException()
    {
        var translator = new SnowflakeExceptionTranslator();
        var inner = new SqlStateDbException("23502", "NULL result in a non-nullable column");

        var result = translator.Translate(SupportedDatabase.Snowflake, inner, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void SnowflakeTranslator_GenericError_Returns_DatabaseOperationException()
    {
        // Unique/FK/check constraints are declarative-only for Snowflake, so an exception that
        // isn't NOT NULL falls to the same generic fallback behavior it always had.
        var translator = new SnowflakeExceptionTranslator();
        var inner = new InvalidOperationException("SQL compilation error: syntax error");

        var result = translator.Translate(SupportedDatabase.Snowflake, inner, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
    }

    [Fact]
    public void Registry_HasExplicitRoutingDecision_ForEveryDatabase()
    {
        var registry = new DbExceptionTranslatorRegistry();

        foreach (var database in Enum.GetValues<SupportedDatabase>())
        {
            var translator = registry.Get(database);

            if (IntentionalFallbackDatabases.Contains(database))
            {
                Assert.IsType<FallbackExceptionTranslator>(translator);
            }
            else
            {
                Assert.False(translator is FallbackExceptionTranslator,
                    $"{database} routes to FallbackExceptionTranslator but is not on the " +
                    $"documented intentional-fallback allowlist. Either add a dedicated " +
                    $"IDbExceptionTranslator for {database} in DbExceptionTranslatorRegistry, " +
                    $"or add it to {nameof(IntentionalFallbackDatabases)} with a documented reason.");
            }
        }
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
