using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class ReadOnlySessionSettingsTests
{
    public static TheoryData<SupportedDatabase, string> DialectData => new()
    {
        { SupportedDatabase.PostgreSql, "SET default_transaction_read_only = on;" },
        { SupportedDatabase.MySql, "SET SESSION transaction_read_only = 1;" },
        { SupportedDatabase.MariaDb, "SET SESSION transaction_read_only = 1;" },
        { SupportedDatabase.Oracle, "" },
        // SQLite, DuckDB, and Snowflake use connection string enforcement — no session SQL
        { SupportedDatabase.Sqlite, "" },
        { SupportedDatabase.DuckDB, "" },
        { SupportedDatabase.Snowflake, "" }
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
        if (string.IsNullOrEmpty(expected))
        {
            return;
        }
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
        Assert.Contains(expected, ctx.GetSessionSettingsPreamble());
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void SessionSettingsPreamble_ReadWriteContext_DoesNotContainSnippet(SupportedDatabase db, string expected)
    {
        if (string.IsNullOrEmpty(expected))
        {
            return;
        }
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        var factory = new fakeDbFactory(db);
        using var ctx = new DatabaseContext(config, factory);
        Assert.DoesNotContain(expected, ctx.GetSessionSettingsPreamble());
    }

    // Regression guard: these dialects must NOT produce session SQL for read-only enforcement —
    // they rely on connection string parameters (access_mode, Mode=ReadOnly) or server permissions.
    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "PRAGMA query_only")]
    [InlineData(SupportedDatabase.DuckDB, "SET access_mode")]
    [InlineData(SupportedDatabase.Snowflake, "TRANSACTION_READ_ONLY")]
    public void GetFinalSessionSettings_ReadOnly_DoesNotContainRemovedSql(
        SupportedDatabase db, string forbidden)
    {
        var factory = new fakeDbFactory(db);
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", factory);
        var dialect = CreateDialect(db, factory);
        var settings = dialect.GetFinalSessionSettings(true);
        Assert.DoesNotContain(forbidden, settings, StringComparison.OrdinalIgnoreCase);
    }

    // ── SQLite WAL-specific guards ────────────────────────────────────────────

    [Fact]
    public void SqliteDialect_GetConnectionSessionSettings_ReadOnly_ExcludesWalPragma()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var ctx = new DatabaseContext("Data Source=test.db", factory);

        var settings = dialect.GetConnectionSessionSettings(ctx, readOnly: true);

        Assert.DoesNotContain("journal_mode", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqliteDialect_GetConnectionSessionSettings_ReadOnly_RetainsForeignKeysPragma()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var ctx = new DatabaseContext("Data Source=test.db", factory);

        var settings = dialect.GetConnectionSessionSettings(ctx, readOnly: true);

        Assert.Contains("foreign_keys", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqliteDialect_GetConnectionSessionSettings_ReadWrite_IncludesWalPragma()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var ctx = new DatabaseContext("Data Source=test.db", factory);

        var settings = dialect.GetConnectionSessionSettings(ctx, readOnly: false);

        Assert.Contains("journal_mode", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WAL", settings, StringComparison.OrdinalIgnoreCase);
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
            SupportedDatabase.Snowflake => new SnowflakeDialect(factory, NullLogger<SnowflakeDialect>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(database))
        };
    }
}