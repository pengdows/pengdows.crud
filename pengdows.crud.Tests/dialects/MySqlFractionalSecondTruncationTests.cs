using System;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Found 2026-09-29 by the adversarial type-mapping tests: MySQL rounds fractional seconds that a
/// column can't hold, so TimeOnly 23:59:59.9999999 was stored in TIME as 24:00:00 and a DATETIME
/// crossed into the next day. MySQL 8.0.8+ truncates instead with sql_mode TIME_TRUNCATE_FRACTIONAL
/// (confirmed live), as MariaDB, PostgreSQL, Firebird and SQL Server already do, so the session sets
/// it there. TiDB rejects the mode (ERROR 1231, confirmed live on v7.5.1) and keeps rounding, a
/// declared limitation; MariaDB and SingleStore truncate natively and lack the mode.
/// </summary>
public sealed class MySqlFractionalSecondTruncationTests
{
    [Theory]
    [InlineData("MySql", "8.0.36", true, false)]
    [InlineData("MySql", "8.0.8", true, false)]
    [InlineData("MySql", "8.0.7", false, true)]
    [InlineData("MySql", "5.7.44", false, true)]
    [InlineData("TiDb", "8.0.11-TiDB-v7.5.1", false, true)]
    [InlineData("MariaDb", "10.11.6-MariaDB", false, false)]
    [InlineData("SingleStore", "8.9.3", false, false)]
    public void SessionTruncatesFractionalSecondsWhereTheEngineSupportsIt(string product, string version,
        bool expectMode, bool expectRounding)
    {
        var database = Enum.Parse<SupportedDatabase>(product);
        var factory = new fakeDbFactory(database) { ServerVersion = version };
        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Server=x;Database=y;EmulatedProduct={product}",
            DbMode = DbMode.Standard
        }, factory);
        Assert.Equal(database, context.Product);

        var settings = ((SqlDialect)context.Dialect).GetFinalSessionSettings(false);

        Assert.Equal(expectMode, settings.Contains("TIME_TRUNCATE_FRACTIONAL", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectRounding, ((SqlDialect)context.Dialect).RoundsFractionalSecondsOnWrite);
    }
}
