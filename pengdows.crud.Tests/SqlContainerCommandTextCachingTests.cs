using System;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerCommandTextCachingTests
{
    [Fact]
    public async Task ExecuteNonQueryAsync_UsesUpdatedQueryText_WhenQueryIsMutated()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard
        };

        await using var context = new DatabaseContext(config, factory);

        var token1 = Guid.NewGuid().ToString("N");
        var token2 = Guid.NewGuid().ToString("N");
        var sql1 = $"SELECT 1 /* {token1} */";
        var sql2 = $"SELECT 2 /* {token2} */";

        await using var container = context.CreateSqlContainer(sql1);
        await container.ExecuteNonQueryAsync();

        container.Query.Clear();
        container.Query.Append(sql2);
        await container.ExecuteNonQueryAsync();

        var executed = factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryTexts).ToList();
        Assert.Contains(executed, cmd => cmd.Contains(token1, StringComparison.Ordinal));
        Assert.Contains(executed, cmd => cmd.Contains(token2, StringComparison.Ordinal));
    }
}
