#region

using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EmbeddedModeEnforcementTests
{
    [Theory]
    [InlineData(SupportedDatabase.Sqlite, ":memory:", DbMode.Standard, DbMode.SingleConnection)]
    [InlineData(SupportedDatabase.Sqlite, ":memory:", DbMode.KeepAlive, DbMode.SingleConnection)]
    [InlineData(SupportedDatabase.Sqlite, ":memory:", DbMode.SingleWriter, DbMode.SingleConnection)]
    [InlineData(SupportedDatabase.Sqlite, "file.db", DbMode.Standard, DbMode.SingleWriter)]
    [InlineData(SupportedDatabase.Sqlite, "file.db", DbMode.SingleConnection, DbMode.SingleConnection)]
    [InlineData(SupportedDatabase.DuckDB, ":memory:", DbMode.Standard, DbMode.SingleConnection)]
    [InlineData(SupportedDatabase.DuckDB, ":memory:", DbMode.KeepAlive, DbMode.SingleConnection)]
    [InlineData(SupportedDatabase.DuckDB, "file.db", DbMode.SingleConnection, DbMode.SingleConnection)]
    // Embedded Firebird: only Best is auto-selected to PreventDatabaseUnload (protects against
    // RDB$LINGER=0's default immediate cache discard on last-attachment-close). Every other
    // explicit choice is genuinely SAFE for embedded Firebird (it supports multiple simultaneous
    // attachments) and is honored, not coerced — see DbModeCoercionLoggingTests for the
    // "honored, no warning" cases.
    [InlineData(SupportedDatabase.Firebird, "test.fdb", DbMode.Best, DbMode.PreventDatabaseUnload)]
    [InlineData(SupportedDatabase.Firebird, "test.fdb", DbMode.Standard, DbMode.Standard)]
    [InlineData(SupportedDatabase.Firebird, "test.fdb", DbMode.SingleWriter, DbMode.SingleWriter)]
    [InlineData(SupportedDatabase.Firebird, "test.fdb", DbMode.SingleConnection, DbMode.SingleConnection)]
    public void EmbeddedProviders_ForceConnectionMode(
        SupportedDatabase product,
        string dataSource,
        DbMode requested,
        DbMode expected)
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = product == SupportedDatabase.Firebird
                ? $"Database={dataSource};ServerType=Embedded;EmulatedProduct={product}"
                : $"Data Source={dataSource};EmulatedProduct={product}",
            DbMode = requested,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(product));
        Assert.Equal(expected, ctx.ConnectionMode);
    }
}
