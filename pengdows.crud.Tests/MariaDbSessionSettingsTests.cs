using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;
using System.Threading.Tasks;

namespace pengdows.crud.Tests;

public class MariaDbSessionSettingsTests
{
    private static ITrackedConnection BuildConnection(IEnumerable<Dictionary<string, object>> rows)
    {
        var inner = new fakeDbConnection
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.MariaDb}",
            EmulatedProduct = SupportedDatabase.MariaDb
        };
        inner.EnqueueReaderResult(rows);
        inner.Open();
        return new TrackedConnection(inner);
    }

    [Fact]
    public async Task GetConnectionSessionSettings_OptimalSettings_ReturnsEmpty()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { { "Variable_name", "sql_mode" }, { "Value", "ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_ZERO_DATE,NO_ZERO_IN_DATE,NO_ENGINE_SUBSTITUTION" } },
            new Dictionary<string, object> { { "Variable_name", "time_zone" }, { "Value", "+00:00" } },
            new Dictionary<string, object> { { "Variable_name", "character_set_client" }, { "Value", "utf8mb4" } },
            new Dictionary<string, object> { { "Variable_name", "collation_connection" }, { "Value", "utf8mb4_general_ci" } },
            new Dictionary<string, object> { { "Variable_name", "transaction_isolation" }, { "Value", "READ-COMMITTED" } },
            new Dictionary<string, object> { { "Variable_name", "sql_notes" }, { "Value", "0" } },
            new Dictionary<string, object> { { "Variable_name", "innodb_strict_mode" }, { "Value", "ON" } },
            new Dictionary<string, object> { { "Variable_name", "sql_safe_updates" }, { "Value", "0" } },
            new Dictionary<string, object> { { "Variable_name", "sql_auto_is_null" }, { "Value", "0" } },
            new Dictionary<string, object> { { "Variable_name", "group_concat_max_len" }, { "Value", "1048576" } }
        };

        await using var conn = BuildConnection(rows);
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=MariaDb", factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, false);
        Assert.Equal(string.Empty, settings);
    }

    [Fact]
    public async Task GetConnectionSessionSettings_MissingSettings_BuildsSnippet()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { { "Variable_name", "sql_mode" }, { "Value", "NO_ENGINE_SUBSTITUTION" } },
            new Dictionary<string, object> { { "Variable_name", "time_zone" }, { "Value", "SYSTEM" } }
        };

        await using var conn = BuildConnection(rows);
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=MariaDb", factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, false);
        Assert.Contains("SET SESSION sql_mode", settings);
        Assert.Contains("SET time_zone", settings);
    }
}
