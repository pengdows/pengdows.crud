using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// The OpenTelemetry adapter (pengdows.crud.opentelemetry) derives a real
// db.client.operation.duration histogram from these Activities. To do that safely
// under concurrent test execution (many unrelated tests create Activities on the
// same process-wide ActivitySource("pengdows.crud")), the observer must be able to
// tell which Activities belong to which tracked IDatabaseContext. This tag is the
// mechanism: it lets a listener filter to only the contexts it is tracking.
public class SqlContainerActivityContextIdTests
{
    [Fact]
    public async Task ExecuteNonQuery_Activity_CarriesContextIdTag()
    {
        using var ctx = CreateContext(SupportedDatabase.Sqlite);
        string? seenContextId = null;
        var expectedContextId = ctx.RootId.ToString();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "pengdows.crud",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                var contextId = activity.GetTagItem("pengdows.context_id") as string;
                if (contextId == expectedContextId)
                {
                    seenContextId = contextId;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var container = ctx.CreateSqlContainer("SELECT 1");
        await container.ExecuteNonQueryAsync();

        Assert.Equal(expectedContextId, seenContextId);
    }

    [Fact]
    public async Task ExecuteReader_Activity_CarriesContextIdTag()
    {
        using var ctx = CreateContext(SupportedDatabase.Sqlite);
        string? seenContextId = null;
        var expectedContextId = ctx.RootId.ToString();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "pengdows.crud",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                var contextId = activity.GetTagItem("pengdows.context_id") as string;
                if (contextId == expectedContextId)
                {
                    seenContextId = contextId;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var container = ctx.CreateSqlContainer("SELECT 1");
        await using var reader = await container.ExecuteReaderAsync();

        Assert.Equal(expectedContextId, seenContextId);
    }

    private static DatabaseContext CreateContext(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            DbMode = DbMode.SingleConnection
        };

        return new DatabaseContext(cfg, factory, NullLoggerFactory.Instance);
    }
}
