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
    // Firebird (embedded or not) is NOT in this list — CoerceMode treats it as an ordinary full
    // server database (Best selects Standard, every explicit choice including
    // PreventDatabaseUnload is honored, nothing forced) — see
    // DatabaseContextModeBranchTests.CoerceMode_HandlesFirebirdAndLocalDb and
    // DbModeCoercionLoggingTests for that coverage and the full policy rationale.
    public void EmbeddedProviders_ForceConnectionMode(
        SupportedDatabase product,
        string dataSource,
        DbMode requested,
        DbMode expected)
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={dataSource};EmulatedProduct={product}",
            DbMode = requested,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(product));
        Assert.Equal(expected, ctx.ConnectionMode);
    }
}
