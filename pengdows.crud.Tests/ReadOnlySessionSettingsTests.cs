using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ReadOnlySessionSettingsTests
{
    public static TheoryData<SupportedDatabase, string> DialectData => new()
    {
        { SupportedDatabase.PostgreSql, "SET default_transaction_read_only = on" },
        { SupportedDatabase.MySql, "SET SESSION TRANSACTION READ ONLY;" },
        { SupportedDatabase.MariaDb, "SET SESSION TRANSACTION READ ONLY;" },
        { SupportedDatabase.Oracle, "ALTER SESSION SET READ ONLY;" },
        { SupportedDatabase.Sqlite, "PRAGMA query_only = ON;" },
        { SupportedDatabase.DuckDB, "PRAGMA read_only = 1;" }
    };

    [Theory]
    [MemberData(nameof(DialectData))]
    public void GetConnectionSessionSettings_ReadOnly_ReturnsSnippet(SupportedDatabase db, string expected)
    {
        var factory = new fakeDbFactory(db);
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", factory);
        var dialect = CreateDialect(db, factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, true);
        Assert.Contains(expected, settings);
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void GetConnectionSessionSettings_NotReadOnly_DoesNotContainSnippet(SupportedDatabase db, string expected)
    {
        var factory = new fakeDbFactory(db);
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", factory);
        var dialect = CreateDialect(db, factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, false);
        Assert.DoesNotContain(expected, settings);
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void SessionSettingsPreamble_ReadOnlyContext_ReturnsSnippet(SupportedDatabase db, string expected)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var factory = new fakeDbFactory(db);
        using var ctx = new DatabaseContext(config, factory);
        Assert.Contains(expected, ctx.SessionSettingsPreamble);
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void SessionSettingsPreamble_ReadWriteContext_DoesNotContainSnippet(SupportedDatabase db, string expected)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        var factory = new fakeDbFactory(db);
        using var ctx = new DatabaseContext(config, factory);
        Assert.DoesNotContain(expected, ctx.SessionSettingsPreamble);
    }

    private static SqlDialect CreateDialect(SupportedDatabase database, fakeDbFactory factory)
    {
        return database switch
        {
            SupportedDatabase.PostgreSql => new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance),
            SupportedDatabase.MySql => new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance),
            SupportedDatabase.MariaDb => new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance),
            SupportedDatabase.Oracle => new OracleDialect(factory, NullLogger<OracleDialect>.Instance),
            SupportedDatabase.Sqlite => new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance),
            SupportedDatabase.DuckDB => new DuckDbDialect(factory, NullLogger<DuckDbDialect>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(database))
        };
    }
}