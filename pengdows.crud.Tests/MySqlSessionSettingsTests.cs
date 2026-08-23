using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class MySqlSessionSettingsTests
{
    [Fact]
    public void SessionSettingsAppliedOnceInSingleConnectionMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.MySql}",
            ProviderName = SupportedDatabase.MySql.ToString(),
            DbMode = DbMode.SingleConnection
        };

        using var ctx = new DatabaseContext(config, factory);
        var count = factory.CreatedConnections
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .Count(c => c.StartsWith("SET SESSION sql_mode"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void SessionSettingsAppliedWhenStandardConnectionOpens()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.MySql}",
            ProviderName = SupportedDatabase.MySql.ToString(),
            DbMode = DbMode.Standard
        };

        using var ctx = new DatabaseContext(config, factory);
        using var connection = ctx.GetConnection(ExecutionType.Read);
        connection.Open();

        var count = factory.CreatedConnections
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .Count(c => c.StartsWith("SET SESSION sql_mode"));
        Assert.True(count >= 1, "Session settings should be applied when the first standard connection opens");
    }
}