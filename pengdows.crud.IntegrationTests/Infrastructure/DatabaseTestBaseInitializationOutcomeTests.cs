using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// A provider that is enabled for the run but fails to initialize (container did not start, could
/// not connect) is a failure, never a skip - otherwise a broken database silently turns into
/// "skipped" (all providers failed) or disappears from the run entirely (some failed). Only a
/// provider excluded by configuration (INTEGRATION_ONLY, an opt-in provider that is not enabled)
/// may lead to a skip.
/// </summary>
public class DatabaseTestBaseInitializationOutcomeTests
{
    [Fact]
    public void AllEnabledProvidersInitialized_DoesNotThrow()
    {
        DatabaseTestBase.EnsureProvidersInitialized("T",
            new[] { SupportedDatabase.Sqlite }, initializedCount: 1,
            exclusionReasons: Array.Empty<string>(), failureReasons: Array.Empty<string>());
    }

    [Fact]
    public void EveryEnabledProviderFailed_FailsInsteadOfSkipping()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatabaseTestBase.EnsureProvidersInitialized("T",
                new[] { SupportedDatabase.SqlServer }, initializedCount: 0,
                exclusionReasons: Array.Empty<string>(),
                failureReasons: new[] { "SqlServer: Could not connect after 60s." }));

        Assert.Contains("SqlServer: Could not connect after 60s.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SomeEnabledProvidersFailed_FailsInsteadOfSilentlyDroppingThem()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseTestBase.EnsureProvidersInitialized("T",
                new[] { SupportedDatabase.Sqlite, SupportedDatabase.SqlServer }, initializedCount: 1,
                exclusionReasons: Array.Empty<string>(),
                failureReasons: new[] { "SqlServer: Could not connect after 60s." }));
    }

    [Fact]
    public void EveryRequestedProviderExcludedByConfiguration_Skips()
    {
        Assert.Throws<Xunit.SkipException>(() =>
            DatabaseTestBase.EnsureProvidersInitialized("T",
                new[] { SupportedDatabase.Snowflake }, initializedCount: 0,
                exclusionReasons: new[] { "Snowflake: not enabled (INCLUDE_SNOWFLAKE)" },
                failureReasons: Array.Empty<string>()));
    }

    // HARN-006: a test that targets one provider (RunTestAgainstProviderAsync, Firebird Embedded)
    // must skip when configuration excluded that provider (INTEGRATION_ONLY), not fail - and still
    // fail when the provider is enabled but unavailable.
    [Fact]
    public void TargetedProvider_Available_DoesNotThrow()
    {
        DatabaseTestBase.EnsureTargetedProviderAvailable(SupportedDatabase.MariaDb,
            new[] { SupportedDatabase.MariaDb }, available: true);
    }

    [Fact]
    public void TargetedProvider_ExcludedByConfiguration_Skips()
    {
        var ex = Assert.Throws<Xunit.SkipException>(() =>
            DatabaseTestBase.EnsureTargetedProviderAvailable(SupportedDatabase.MariaDb,
                new[] { SupportedDatabase.PostgreSql }, available: false));

        Assert.Contains("MariaDb", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetedProvider_EnabledButUnavailable_Fails()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseTestBase.EnsureTargetedProviderAvailable(SupportedDatabase.MariaDb,
                new[] { SupportedDatabase.MariaDb }, available: false));
    }
}
