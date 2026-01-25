#region

using pengdows.crud.configuration;
using pengdows.crud.enums;
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