using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Confirms the "database product identity" leg of
/// docs/planning/3.0-architectural-review-backlog.md's provider-identity P1 item ("Separate
/// provider registration identity, provider invariant identity, and database product
/// identity") is already independent of the other two. `IDatabaseContextConfiguration.ProviderName`
/// serves two genuinely different roles depending on how the context is constructed — an
/// ADO.NET invariant name (the plain-constructor/DbProviderFactories.GetFactory path) or a
/// DbProviderLoader/ITenantContextRegistry keyed-DI registration key (see
/// DbProviderLoader.cs's CORE-006 comment and IDatabaseContextConfiguration.ProviderName's own
/// remarks) — but neither spelling has any bearing on dialect/product detection, which is
/// driven entirely by the live DbProviderFactory/connection, never by this string. Splitting
/// ProviderName into two distinct, clearly-named properties is real new public API surface and
/// was deliberately not done unilaterally in this pass — see the backlog doc.
/// </summary>
public class ProviderIdentityIndependenceTests
{
    [Fact]
    public void Product_IsDetectedFromFactory_NotFromProviderNameString()
    {
        // A deliberately nonsensical ProviderName — looks like neither a real ADO.NET invariant
        // name nor a real DatabaseProviders section key — proves detection never consults it.
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql",
            ProviderName = "totally-unrelated-string-xyz-123"
        };

        using var context = new DatabaseContext(config, factory);

        Assert.Equal(SupportedDatabase.PostgreSql, context.Product);
    }

    [Fact]
    public void Product_IsDetectedFromFactory_EvenWhenProviderNameLooksLikeADifferentDatabase()
    {
        // The most adversarial case: ProviderName spells a DIFFERENT real database's name than
        // the actual factory — if detection ever fell back to parsing this string, this would
        // silently misdetect as MySql instead of PostgreSql.
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql",
            ProviderName = "MySql.Data.MySqlClient"
        };

        using var context = new DatabaseContext(config, factory);

        Assert.Equal(SupportedDatabase.PostgreSql, context.Product);
    }
}
