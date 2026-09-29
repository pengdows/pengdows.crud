using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// HARN-007: a statement that never returns (seen with Spanner Omni DDL: the server applied it, the
/// client waited 20+ minutes in NpgsqlDataReader.NextResult despite CommandTimeout=60) must fail the
/// affected provider with a clear message instead of hanging the whole integration run.
/// </summary>
public class DatabaseTestBaseTimeoutTests
{
    [Fact]
    public async Task RunWithTimeout_WorkThatNeverCompletes_FailsWithAClearMessage()
    {
        var never = new TaskCompletionSource();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            DatabaseTestBase.RunWithTimeoutAsync(() => never.Task, TimeSpan.FromMilliseconds(50),
                SupportedDatabase.Spanner, "setup"));

        Assert.Contains("Spanner", ex.Message);
        Assert.Contains("setup", ex.Message);
        Assert.Contains("HARN-007", ex.Message);
    }

    [Fact]
    public async Task RunWithTimeout_WorkThatCompletes_ReturnsNormally()
    {
        var ran = false;

        await DatabaseTestBase.RunWithTimeoutAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(5), SupportedDatabase.Sqlite, "setup");

        Assert.True(ran);
    }

    [Fact]
    public async Task RunWithTimeout_WorkThatFails_PropagatesItsOwnException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseTestBase.RunWithTimeoutAsync(() => throw new InvalidOperationException("boom"),
                TimeSpan.FromSeconds(5), SupportedDatabase.Sqlite, "test"));
    }

    [Theory]
    [InlineData(null, 300)]
    [InlineData("", 300)]
    [InlineData("not-a-number", 300)]
    [InlineData("0", 300)]
    [InlineData("45", 45)]
    public void ParseTimeout_UsesTheEnvironmentValueOrTheDefault(string? value, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds),
            DatabaseTestBase.ParseTimeout(value, TimeSpan.FromSeconds(300)));
    }
}
