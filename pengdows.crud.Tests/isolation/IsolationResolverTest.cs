#region

using System;
using System.Data;
using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.isolation;
using Xunit;

#endregion

namespace pengdows.crud.Tests.isolation;

public class IsolationResolverTests
{
    [Fact]
    public void Constructor_UnsupportedDatabase_Throws()
    {
        Assert.Throws<NotSupportedException>(() => new IsolationResolver((SupportedDatabase)999, false, false));
    }

    [Fact]
    public void GetSupportedLevels_SqlServer_WithSnapshotIsolation()
    {
        var resolver = new IsolationResolver(SupportedDatabase.SqlServer, true, true);

        var levels = resolver.GetSupportedLevels().OrderBy(level => level).ToArray();
        var expected = new[]
        {
            IsolationLevel.ReadCommitted,
            IsolationLevel.ReadUncommitted,
            IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable,
            IsolationLevel.Snapshot
        }.OrderBy(level => level).ToArray();

        Assert.Equal(expected, levels);
    }

    [Fact]
    public void GetSupportedLevels_SqlServer_WithoutSnapshotIsolation()
    {
        var resolver = new IsolationResolver(SupportedDatabase.SqlServer, false, false);

        var levels = resolver.GetSupportedLevels();

        Assert.DoesNotContain(IsolationLevel.Snapshot, levels);
        Assert.Contains(IsolationLevel.ReadCommitted, levels);
    }

    [Fact]
    public void ResolveWithDetail_SqlServer_DegradesWhenSnapshotAndRcsiDisabled()
    {
        var resolver = new IsolationResolver(SupportedDatabase.SqlServer, false, false);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.ReadCommitted, resolution.Level);
        Assert.True(resolution.Degraded);
        Assert.Throws<InvalidOperationException>(() => resolver.Validate(IsolationLevel.Snapshot));
    }

    [Fact]
    public void ResolveWithDetail_SqlServer_RcsiEnabledSignalsSnapshotFallback()
    {
        var resolver = new IsolationResolver(SupportedDatabase.SqlServer, true, false);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.ReadCommitted, resolution.Level);
        Assert.True(resolution.Degraded);
    }

    [Fact]
    public void ResolveWithDetail_SqlServer_SnapshotIsolationAllowed()
    {
        var resolver = new IsolationResolver(SupportedDatabase.SqlServer, false, true);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.Snapshot, resolution.Level);
        Assert.False(resolution.Degraded);
    }

    [Fact]
    public void Resolve_PostgreSql_Mappings()
    {
        var resolver = new IsolationResolver(SupportedDatabase.PostgreSql, false, false);

        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
        Assert.Throws<InvalidOperationException>(() => resolver.Validate(IsolationLevel.ReadUncommitted));
    }

    [Fact]
    public void Resolve_CockroachDb_UnsupportedProfile()
    {
        var resolver = new IsolationResolver(SupportedDatabase.CockroachDb, true, false);

        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Throws<NotSupportedException>(() => resolver.Resolve(IsolationProfile.FastWithRisks));
        Assert.Throws<InvalidOperationException>(() => resolver.Validate(IsolationLevel.ReadCommitted));
    }

    [Fact]
    public void GetSupportedLevels_Firebird()
    {
        var resolver = new IsolationResolver(SupportedDatabase.Firebird, false, false);

        var levels = resolver.GetSupportedLevels().OrderBy(level => level).ToArray();
        var expected = new[]
        {
            IsolationLevel.ReadCommitted,
            IsolationLevel.Serializable,
            IsolationLevel.Snapshot
        }.OrderBy(level => level).ToArray();

        Assert.Equal(expected, levels);
    }

    [Fact]
    public void Resolve_MySql_Mappings()
    {
        var resolver = new IsolationResolver(SupportedDatabase.MySql, false, false);

        Assert.Equal(IsolationLevel.RepeatableRead, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadUncommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void Resolve_MariaDb_Mappings()
    {
        var resolver = new IsolationResolver(SupportedDatabase.MariaDb, false, false);

        Assert.Equal(IsolationLevel.RepeatableRead, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadUncommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void Resolve_Oracle_Mappings()
    {
        var resolver = new IsolationResolver(SupportedDatabase.Oracle, false, false);

        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Throws<NotSupportedException>(() => resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void GetSupportedLevels_DuckDb()
    {
        var resolver = new IsolationResolver(SupportedDatabase.DuckDB, false, false);

        var levels = resolver.GetSupportedLevels().OrderBy(level => level).ToArray();
        Assert.Equal(new[] { IsolationLevel.Serializable }, levels);
    }

    [Fact]
    public void Resolve_DuckDb_Mappings()
    {
        var resolver = new IsolationResolver(SupportedDatabase.DuckDB, false, false);

        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Throws<NotSupportedException>(() => resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void GetSupportedLevels_Sqlite()
    {
        var resolver = new IsolationResolver(SupportedDatabase.Sqlite, false, false);

        var levels = resolver.GetSupportedLevels().OrderBy(level => level).ToArray();
        var expected = new[] { IsolationLevel.ReadCommitted, IsolationLevel.Serializable }
            .OrderBy(level => level)
            .ToArray();

        Assert.Equal(expected, levels);
    }

    [Fact]
    public void Resolve_Sqlite_Mappings()
    {
        var resolver = new IsolationResolver(SupportedDatabase.Sqlite, false, false);

        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Throws<NotSupportedException>(() => resolver.Resolve(IsolationProfile.FastWithRisks));
    }
}