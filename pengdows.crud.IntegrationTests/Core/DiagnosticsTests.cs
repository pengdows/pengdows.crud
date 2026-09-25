using System.Diagnostics;
using System.Linq;
using System.Threading;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Verifies that the framework correctly surfaces database-specific errors
/// as framework exceptions with provider details preserved as inner exceptions.
/// </summary>
[Collection("IntegrationTests")]
public class DiagnosticsTests : DatabaseTestBase
{
    public DiagnosticsTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [SkippableFact]
    public async Task SyntaxError_SurfacesAsDatabaseException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Act & Assert
            // Intentional syntax error: MISSING FROM or invalid keyword
            var sql = "SELECT * FROM";
            if (provider == SupportedDatabase.Oracle)
            {
                sql = "SELECT * FROM-INVALID";
            }

            await using var container = context.CreateSqlContainer(sql);

            await Assert.ThrowsAnyAsync<DatabaseException>(async () => await container.ExecuteNonQueryAsync());
        });
    }

    [SkippableFact]
    public async Task UniqueConstraintViolation_SurfacesAsDatabaseException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider == SupportedDatabase.Snowflake)
            {
                Output.WriteLine("Skipping unique constraint test for Snowflake (constraints are not enforced)");
                return;
            }

            // Arrange
            var auditResolver = GetAuditResolver();
            var helper = new TableGateway<TestTable, long>(context, auditResolver);
            var id = DateTime.UtcNow.Ticks;
            var entity = new TestTable { Id = id, Name = NameEnum.Test, Value = 1 };
            await helper.CreateAsync(entity, context);

            // Act & Assert: Insert duplicate ID
            var duplicate = new TestTable { Id = id, Name = NameEnum.Test2, Value = 2 };

            await Assert.ThrowsAnyAsync<DatabaseException>(async () => await helper.CreateAsync(duplicate, context));
        });
    }

    // ---- Ported from 3.0 (backport audit, 2026-09-25) ----

    private static long _nextDiagnosticsId = DateTime.UtcNow.Ticks;

    /// <summary>
    /// Verifies real span emission on the <c>ActivitySource("pengdows.crud")</c> documented in
    /// docs/tracing.md — previously only generic exception surfacing was checked in this file,
    /// never that a span is actually recorded with the documented tags during a live operation.
    /// Uses a real <see cref="ActivityListener"/> (the standard .NET way to observe
    /// <see cref="Activity"/> emission) rather than asserting anything about the tracing
    /// infrastructure's internals.
    /// </summary>
    [SkippableFact]
    public async Task LiveOperation_EmitsActivitySpanWithDocumentedTags()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var captured = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "pengdows.crud",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => captured.Add(activity)
            };
            ActivitySource.AddActivityListener(listener);

            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var id = Interlocked.Increment(ref _nextDiagnosticsId);

            try
            {
                await helper.CreateAsync(
                    new TestTable { Id = id, Name = NameEnum.Test, Description = "trace-probe", Value = 1 },
                    context);

                var createSpan = captured.LastOrDefault(a => a.OperationName == "ExecuteNonQuery");
                Assert.NotNull(createSpan);
                Assert.Equal(ActivityKind.Client, createSpan!.Kind);
                Assert.Equal(provider.ToString().ToLowerInvariant(), createSpan.GetTagItem("db.system"));
                Assert.Equal("ExecuteNonQuery", createSpan.GetTagItem("db.operation"));
                var statement = Assert.IsType<string>(createSpan.GetTagItem("db.statement"));
                Assert.Contains("test_table", statement, StringComparison.OrdinalIgnoreCase);
                Assert.NotNull(createSpan.GetTagItem("pengdows.context_id"));
            }
            finally
            {
                await helper.DeleteAsync(id, context);
            }
        });
    }

    /// <summary>
    /// Verifies <see cref="IDatabaseContext.GetPoolStatisticsSnapshot"/> reflects a REAL change
    /// after a live operation — previously only ever asserted as a static/zero baseline elsewhere
    /// in this suite, never that the counters genuinely move in response to real work.
    /// </summary>
    [SkippableFact]
    public async Task LiveOperation_IncrementsRealPoolStatistics()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var before = context.GetPoolStatisticsSnapshot(PoolLabel.Writer);

            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var id = Interlocked.Increment(ref _nextDiagnosticsId);

            try
            {
                await helper.CreateAsync(
                    new TestTable { Id = id, Name = NameEnum.Test, Description = "metrics-probe", Value = 1 },
                    context);

                var after = context.GetPoolStatisticsSnapshot(PoolLabel.Writer);
                Assert.True(after.TotalAcquired > before.TotalAcquired,
                    $"{provider}: expected TotalAcquired to increase after a real write " +
                    $"(before={before.TotalAcquired}, after={after.TotalAcquired})");
            }
            finally
            {
                await helper.DeleteAsync(id, context);
            }
        });
    }
}
