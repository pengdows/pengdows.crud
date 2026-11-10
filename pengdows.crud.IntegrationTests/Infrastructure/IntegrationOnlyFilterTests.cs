using System;
using System.Collections.Generic;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.IntegrationTests.Infrastructure;

// INTEGRATION_ONLY names a database the run did not enable (Aurora is opt-in: INCLUDE_AURORA). The error must say so, not
// claim the name is not a SupportedDatabase value (it is one).
public sealed class IntegrationOnlyFilterTests
{
    [Fact]
    public void ANamedDatabaseThatIsNotEnabledSaysHowToEnableIt()
    {
        var enabled = new List<SupportedDatabase> { SupportedDatabase.Sqlite };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            IntegrationTestConfiguration.FilterIntegrationOnly(enabled, "AuroraMySql"));

        Assert.Contains("INCLUDE_AURORA", ex.Message);
        Assert.DoesNotContain("did not match any SupportedDatabase", ex.Message);
    }

    [Fact]
    public void AnEnabledDatabaseIsSelected()
    {
        var enabled = new List<SupportedDatabase> { SupportedDatabase.Sqlite, SupportedDatabase.MySql };

        var selected = IntegrationTestConfiguration.FilterIntegrationOnly(enabled, "MySql");

        Assert.Equal(new[] { SupportedDatabase.MySql }, selected);
    }

    [Fact]
    public void ASuiteThatDoesNotIncludeTheNamedDatabaseGetsNoProvidersAndDoesNotThrow()
    {
        // A test class that does not include INTEGRATION_ONLY's database runs nothing; it is not an error (the run's
        // own filter is what refuses a name nothing enables).
        var enabled = new List<SupportedDatabase> { SupportedDatabase.Sqlite };

        var selected = IntegrationTestConfiguration.SelectIntegrationOnly(enabled, "MySql");

        Assert.Empty(selected);
    }
}
