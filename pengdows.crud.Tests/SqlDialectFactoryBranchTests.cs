using System;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectFactoryBranchTests
{
    [Theory]
    [InlineData(typeof(SqlServerFactory), SupportedDatabase.SqlServer)]
    [InlineData(typeof(NpgsqlFactory), SupportedDatabase.PostgreSql)]
    [InlineData(typeof(MySqlFactory), SupportedDatabase.MySql)]
    [InlineData(typeof(SqliteFactory), SupportedDatabase.Sqlite)]
    [InlineData(typeof(OracleFactory), SupportedDatabase.Oracle)]
    [InlineData(typeof(FirebirdFactory), SupportedDatabase.Firebird)]
    [InlineData(typeof(DuckDbFactory), SupportedDatabase.DuckDB)]
    [InlineData(typeof(UnknownFactory), SupportedDatabase.Unknown)]
    public void InferDatabaseTypeFromProvider_UsesTypeName(Type factoryType, SupportedDatabase expected)
    {
        var method = typeof(SqlDialectFactory).GetMethod(
            "InferDatabaseTypeFromProvider",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var factory = (DbProviderFactory)Activator.CreateInstance(factoryType)!;

        var result = (SupportedDatabase)method!.Invoke(null, new object[] { factory })!;

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SQL Server", SupportedDatabase.SqlServer)]
    [InlineData("MariaDB 10", SupportedDatabase.MariaDb)]
    [InlineData("MySQL 8", SupportedDatabase.MySql)]
    [InlineData("TiDB", SupportedDatabase.TiDb)]
    [InlineData("CockroachDB", SupportedDatabase.CockroachDb)]
    [InlineData("Yugabyte", SupportedDatabase.YugabyteDb)]
    [InlineData("Npgsql", SupportedDatabase.PostgreSql)]
    [InlineData("Postgres", SupportedDatabase.PostgreSql)]
    [InlineData("Oracle", SupportedDatabase.Oracle)]
    [InlineData("SQLite", SupportedDatabase.Sqlite)]
    [InlineData("Firebird", SupportedDatabase.Firebird)]
    [InlineData("Duck DB", SupportedDatabase.DuckDB)]
    [InlineData("Snowflake", SupportedDatabase.Snowflake)]
    [InlineData("Unknown", SupportedDatabase.Unknown)]
    public void InferDatabaseTypeFromName_UsesTokens(string name, SupportedDatabase expected)
    {
        var method = typeof(SqlDialectFactory).GetMethod(
            "InferDatabaseTypeFromName",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = (SupportedDatabase)method!.Invoke(null, new object[] { name })!;

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, typeof(SqlServerDialect))]
    [InlineData(SupportedDatabase.PostgreSql, typeof(PostgreSqlDialect))]
    [InlineData(SupportedDatabase.CockroachDb, typeof(CockroachDbDialect))]
    [InlineData(SupportedDatabase.YugabyteDb, typeof(YugabyteDbDialect))]
    [InlineData(SupportedDatabase.TiDb, typeof(TiDbDialect))]
    [InlineData(SupportedDatabase.MySql, typeof(MySqlDialect))]
    [InlineData(SupportedDatabase.MariaDb, typeof(MariaDbDialect))]
    [InlineData(SupportedDatabase.Sqlite, typeof(SqliteDialect))]
    [InlineData(SupportedDatabase.Oracle, typeof(OracleDialect))]
    [InlineData(SupportedDatabase.Firebird, typeof(FirebirdDialect))]
    [InlineData(SupportedDatabase.DuckDB, typeof(DuckDbDialect))]
    [InlineData(SupportedDatabase.Snowflake, typeof(SnowflakeDialect))]
    [InlineData(SupportedDatabase.Unknown, typeof(Sql92Dialect))]
    public void CreateDialectForType_ReturnsExpectedDialect(SupportedDatabase type, Type expectedDialect)
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("SqlDialect");
        var factory = new UnknownFactory();

        var dialect = SqlDialectFactory.CreateDialectForType(type, factory, logger);

        Assert.IsType(expectedDialect, dialect);
    }

    private sealed class SqlServerFactory : DbProviderFactory
    {
    }

    private sealed class NpgsqlFactory : DbProviderFactory
    {
    }

    private sealed class MySqlFactory : DbProviderFactory
    {
    }

    private sealed class SqliteFactory : DbProviderFactory
    {
    }

    private sealed class OracleFactory : DbProviderFactory
    {
    }

    private sealed class FirebirdFactory : DbProviderFactory
    {
    }

    private sealed class DuckDbFactory : DbProviderFactory
    {
    }

    private sealed class UnknownFactory : DbProviderFactory
    {
    }
}