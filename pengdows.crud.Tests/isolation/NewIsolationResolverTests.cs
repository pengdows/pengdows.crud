using System.Data;
using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.isolation;
using Xunit;

namespace pengdows.crud.Tests.isolation;

public class NewIsolationResolverTests
{
    [Fact]
    public void Resolve_YugabyteDb_Mappings()
    {
        var resolver = new IsolationResolver(SupportedDatabase.YugabyteDb, false, false);

        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.FastWithRisks));

        var levels = resolver.GetSupportedLevels();
        Assert.Contains(IsolationLevel.ReadCommitted, levels);
        Assert.Contains(IsolationLevel.RepeatableRead, levels);
        Assert.Contains(IsolationLevel.Serializable, levels);
    }

    [Fact]
    public void Resolve_TiDb_Mappings()
    {
        var resolver = new IsolationResolver(SupportedDatabase.TiDb, false, false);

        // TiDB accepts SERIALIZABLE syntax but silently maps it to REPEATABLE READ.
        // StrictConsistency uses RepeatableRead (best available) rather than advertising a level that isn't enforced.
        Assert.Equal(IsolationLevel.RepeatableRead, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.RepeatableRead, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.FastWithRisks));

        var levels = resolver.GetSupportedLevels();
        Assert.Contains(IsolationLevel.ReadCommitted, levels);
        Assert.Contains(IsolationLevel.RepeatableRead, levels);
        Assert.DoesNotContain(IsolationLevel.Serializable, levels);
    }

    [Fact]
    public void Resolve_Snowflake_Mappings()
    {
        var resolver = new IsolationResolver(SupportedDatabase.Snowflake, false, false);

        // Snowflake only supports READ COMMITTED; all profiles map to it.
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.FastWithRisks));

        var levels = resolver.GetSupportedLevels();
        Assert.Contains(IsolationLevel.ReadCommitted, levels);
        Assert.DoesNotContain(IsolationLevel.Serializable, levels);
        Assert.DoesNotContain(IsolationLevel.ReadUncommitted, levels);
        Assert.DoesNotContain(IsolationLevel.RepeatableRead, levels);
    }

    [Fact]
    public void Resolve_Sybase_Mappings()
    {
        // Verified live against ASE 16.0 SP02 (session cited in SybaseAseDialect.cs): AseConnection
        // .BeginTransaction accepts all four standard IsolationLevel values, and a subsequent
        // "SELECT @@isolation" inside each transaction confirms the server genuinely applied it —
        // 0/1/2/3 map exactly to ReadUncommitted/ReadCommitted/RepeatableRead/Serializable, not
        // just a client-side no-op. Without an explicit entry here, this fell back to the generic
        // default ({ReadCommitted, RepeatableRead, Serializable}), which wrongly omitted
        // ReadUncommitted — a real, supported level on this engine.
        var resolver = new IsolationResolver(SupportedDatabase.SybaseASE, false, false);

        Assert.Equal(IsolationLevel.RepeatableRead, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadUncommitted, resolver.Resolve(IsolationProfile.FastWithRisks));

        var levels = resolver.GetSupportedLevels();
        Assert.Contains(IsolationLevel.ReadUncommitted, levels);
        Assert.Contains(IsolationLevel.ReadCommitted, levels);
        Assert.Contains(IsolationLevel.RepeatableRead, levels);
        Assert.Contains(IsolationLevel.Serializable, levels);
    }
}