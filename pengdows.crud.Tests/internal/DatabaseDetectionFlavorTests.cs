using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests.@internal;

public class DatabaseDetectionFlavorTests
{
    [Fact]
    public void Debug_ExecuteScalar_Works()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.SetScalarResultForCommand("SELECT @@aurora_version", "5.7.12");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT @@aurora_version";
        var res = cmd.ExecuteScalar();

        Assert.Equal("5.7.12", res);
    }

    [Fact]
    public void DetectProduct_IdentifiesAuroraMySql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var connection = (fakeDbConnection)factory.CreateConnection();

        connection.EmulatedProduct = SupportedDatabase.Unknown;
        connection.SetScalarResultForCommand("SELECT @@aurora_version", "5.7.12");

        var detected = DatabaseDetectionService.DetectProduct(connection, factory);

        Assert.Equal(SupportedDatabase.AuroraMySql, detected);
    }

    [Fact]
    public void DetectProduct_IdentifiesAuroraPostgreSql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = (fakeDbConnection)factory.CreateConnection();

        connection.EmulatedProduct = SupportedDatabase.Unknown;
        connection.SetScalarResultForCommand("SELECT aurora_version()", "1.2.3");

        var detected = DatabaseDetectionService.DetectProduct(connection, factory);

        Assert.Equal(SupportedDatabase.AuroraPostgreSql, detected);
    }

    [Fact]
    public void DetectProduct_IdentifiesYugabyteDb_ViaPgSettingsProbe()
    {
        // Detection queries pg_settings for a YugabyteDB-only GUC. Using a plain SELECT (not
        // SHOW or a function call) means the probe never throws on standard PostgreSQL — it
        // simply returns null when the row doesn't exist. This probe also runs BEFORE
        // aurora_version() so a failed aurora probe cannot corrupt the YSQL connection state.
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = (fakeDbConnection)factory.CreateConnection();

        connection.EmulatedProduct = SupportedDatabase.Unknown;
        connection.SetScalarResultForCommand(
            "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1",
            "yb_enable_optimizer_statistics");

        var detected = DatabaseDetectionService.DetectProduct(connection, factory);

        Assert.Equal(SupportedDatabase.YugabyteDb, detected);
    }

    [Fact]
    public void PostgreSqlDialect_CockroachDb_DoesNotSupportMerge()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

        // Simulate detection as CockroachDb
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.EmulatedProduct = SupportedDatabase.PostgreSql;
        connection.SetScalarResultForCommand("SELECT version()", "CockroachDB CCL v20.2.0");

        // Directly test dialect detection
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance, SupportedDatabase.CockroachDb);

        using var tracked = new FakeTrackedConnection(connection, new DataTable(), new Dictionary<string, object>
        {
            ["SELECT version()"] = "CockroachDB CCL v20.2.0"
        });

        dialect.DetectDatabaseInfo(tracked);

        Assert.Equal(SupportedDatabase.CockroachDb, dialect.DatabaseType);
        Assert.False(dialect.SupportsMerge);
    }

    [Fact]
    public void PostgreSqlDialect_AuroraPostgreSql_SupportsMerge()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance, SupportedDatabase.AuroraPostgreSql);

        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.EmulatedProduct = SupportedDatabase.PostgreSql;
        connection.SetScalarResultForCommand("SELECT version()", "PostgreSQL 15.0");

        using var tracked = new FakeTrackedConnection(connection, new DataTable(), new Dictionary<string, object>
        {
            ["SELECT version()"] = "PostgreSQL 15.0"
        });

        dialect.DetectDatabaseInfo(tracked);

        Assert.Equal(SupportedDatabase.AuroraPostgreSql, dialect.DatabaseType);
        Assert.True(dialect.SupportsMerge);
    }
}
