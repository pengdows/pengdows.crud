#region

using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.fakeDb;
using pengdows.crud.Tests.Logging;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DatabaseContextLoggingTests
{
    [Fact]
    public void ApplySessionSettings_Logs_Info_And_Error_OnFailure()
    {
        var provider = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { provider });

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=file.db;EmulatedProduct={SupportedDatabase.Sqlite}",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Best
        };

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(cfg, factory, loggerFactory);

        // Should log applying when called explicitly
        // var good = new fakeDb.fakeDbConnection();
        var good = factory.CreateConnection();
        ctx.ApplyPersistentConnectionSessionSettings(good);

        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Information &&
                 e.Message.Contains("Applying persistent connection session settings"));

        // Now simulate failure on command to hit error path
        var badFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var bad = (fakeDbConnection)badFactory.CreateConnection();
        ConnectionFailureHelper.ConfigureConnectionFailure(bad, ConnectionFailureMode.FailOnCommand);

        ctx.ApplyPersistentConnectionSessionSettings(bad);

        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Error && e.Message.Contains("Error setting session settings"));
    }
}