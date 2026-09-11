using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Addresses the P1 finding "Separate provider registration identity, provider invariant
/// identity, and database product identity" from
/// docs/planning/3.0-architectural-review-backlog.md — redirected after user feedback: the
/// user's own mental model is classic .NET Framework/ADO.NET dynamic provider loading (a
/// connection string's <c>providerName</c> is one name, resolved reliably — see
/// <c>System.Data.Common.DbProviderFactories</c>/machine.config's historical
/// <c>&lt;DbProviderFactories&gt;</c> section), not two separate concepts the caller must keep
/// straight. The real defect was never that "registration key" and "invariant name" are
/// distinct ideas — it's that <see cref="DbProviderLoader"/> only ever registered its keyed DI
/// service under the <c>DatabaseProviders</c> configuration section's own dictionary key, never
/// under the provider's own <see cref="DatabaseProviderConfig.ProviderName"/> value — so a
/// caller who (reasonably, matching classic ADO.NET convention) set
/// <see cref="IDatabaseContextConfiguration.ProviderName"/> to the provider's real invariant
/// name got a confusing failure unless it happened to also match the section key.
/// <see cref="TenantContextRegistry"/>'s own <c>ResolveProviderFactory</c> comment already
/// acknowledged this as a real usability gap without closing it. Fixed by registering under
/// both the section key and the configured <c>ProviderName</c> (when they differ), so either
/// spelling resolves — matching the "one reliable name" model, not a caller-visible split.
/// </summary>
public class DbProviderLoaderInvariantNameResolutionTests
{
    [Fact]
    public void LoadAndRegisterProviders_ResolvesByInvariantProviderName_NotJustSectionKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Section key ("mypostgres") deliberately differs from ProviderName
                // ("My.Invariant.ProviderName") — the exact classic-ADO.NET usage pattern: a
                // caller should be able to resolve by the invariant name regardless of what
                // arbitrary section key an operator chose in configuration.
                ["DatabaseProviders:mypostgres:ProviderName"] = "My.Invariant.ProviderName",
                ["DatabaseProviders:mypostgres:AssemblyName"] = "pengdows.crud.fakeDb",
                ["DatabaseProviders:mypostgres:FactoryType"] = "pengdows.crud.fakeDb.fakeDbFactory"
            })
            .Build();

        var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);
        var services = new ServiceCollection();
        loader.LoadAndRegisterProviders(services);
        using var provider = services.BuildServiceProvider();

        var byInvariantName = provider.GetKeyedService<DbProviderFactory>("My.Invariant.ProviderName");
        var bySectionKey = provider.GetKeyedService<DbProviderFactory>("mypostgres");

        Assert.NotNull(byInvariantName);
        Assert.NotNull(bySectionKey);
        Assert.Same(bySectionKey, byInvariantName);
    }

    [Fact]
    public void LoadAndRegisterProviders_SectionKeyEqualsProviderName_RegistersOnce()
    {
        // When the operator's section key already IS the invariant name (the common case),
        // there must not be a duplicate/conflicting keyed registration attempt.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:My.Invariant.ProviderName:ProviderName"] = "My.Invariant.ProviderName",
                ["DatabaseProviders:My.Invariant.ProviderName:AssemblyName"] = "pengdows.crud.fakeDb",
                ["DatabaseProviders:My.Invariant.ProviderName:FactoryType"] = "pengdows.crud.fakeDb.fakeDbFactory"
            })
            .Build();

        var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);
        var services = new ServiceCollection();
        var exception = Record.Exception(() => loader.LoadAndRegisterProviders(services));
        Assert.Null(exception);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetKeyedService<DbProviderFactory>("My.Invariant.ProviderName"));
    }
}
