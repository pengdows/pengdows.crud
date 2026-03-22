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
}