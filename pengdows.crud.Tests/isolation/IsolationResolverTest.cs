#region

using System;
using System.Data;
using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.isolation;
using Xunit;

#endregion

namespace pengdows.crud.Tests.isolation;

public class IsolationResolverTests
{
    [Fact]
    public void Constructor_NullDialect_Throws()
    {
        // The enum-validation this used to cover ((SupportedDatabase)999 -> NotSupportedException)
        // no longer applies: IsolationResolver takes a real SqlDialect instance now, not a raw
        // enum, and an out-of-range SupportedDatabase value resolves via SqlDialectFactory's own
        // pre-existing silent fallback to Sql92Dialect (see
        // Constructor_UnrecognizedDatabase_FallsBackToGenericAnsiIsolationSet below) rather than
        // ever reaching this constructor with something invalid. What this constructor can
        // actually reject is a null dialect.
        Assert.Throws<ArgumentNullException>(() => new IsolationResolver(null!, false, false));
    }

    [Fact]
    public void Constructor_UnrecognizedDatabase_FallsBackToGenericAnsiIsolationSet()
    {
        // SqlDialectFactory.CreateDialectForType falls back to Sql92Dialect for any
        // SupportedDatabase value it doesn't recognize — an existing, intentional pattern, not
        // something this refactor introduces. Sql92Dialect inherits SqlDialect's base
        // GetSupportedIsolationLevels/GetIsolationProfileMapping unchanged, i.e. the same generic
        // ANSI fallback (ReadCommitted/RepeatableRead/Serializable) the old switch's own
        // catch-all `_ =>` branch used to return directly.
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create((SupportedDatabase)999), false, false);

        var levels = resolver.GetSupportedLevels().OrderBy(level => level).ToArray();
        var expected = new[] { IsolationLevel.ReadCommitted, IsolationLevel.RepeatableRead, IsolationLevel.Serializable }
            .OrderBy(level => level)
            .ToArray();
        Assert.Equal(expected, levels);
    }

    [Fact]
    public void GetSupportedLevels_SqlServer_WithSnapshotIsolation()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.SqlServer), true, true);

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
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.SqlServer), false, false);

        var levels = resolver.GetSupportedLevels();

        Assert.DoesNotContain(IsolationLevel.Snapshot, levels);
        Assert.Contains(IsolationLevel.ReadCommitted, levels);
    }

    [Fact]
    public void ResolveWithDetail_SqlServer_DegradesWhenSnapshotAndRcsiDisabled()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.SqlServer), false, false);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.ReadCommitted, resolution.Level);
        Assert.True(resolution.Degraded);
        Assert.Throws<InvalidOperationException>(() => resolver.Validate(IsolationLevel.Snapshot));
    }

    [Fact]
    public void ResolveWithDetail_SqlServer_RcsiEnabledSignalsSnapshotFallback()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.SqlServer), true, false);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.ReadCommitted, resolution.Level);
        Assert.True(resolution.Degraded);
    }

    [Fact]
    public void ResolveWithDetail_SqlServer_SnapshotIsolationAllowed()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.SqlServer), false, true);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.Snapshot, resolution.Level);
        Assert.False(resolution.Degraded);
    }

    [Fact]
    public void Resolve_PostgreSql_Mappings()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.PostgreSql), false, false);

        // PostgreSQL's REPEATABLE READ is MVCC-snapshot-based (non-blocking) and, unlike the ANSI
        // baseline, also prevents phantom reads for the transaction's lifetime — see
        // https://www.postgresql.org/docs/current/transaction-iso.html#XACT-REPEATABLE-READ. It is
        // therefore the correct exact match for SafeNonBlockingReads, not ReadCommitted (which
        // re-snapshots every statement) and not Serializable (which trades non-blocking for
        // write-skew protection nobody asked for).
        Assert.Equal(IsolationLevel.RepeatableRead, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
        Assert.Throws<InvalidOperationException>(() => resolver.Validate(IsolationLevel.ReadUncommitted));
    }

    [Fact]
    public void Resolve_CockroachDb_UnsupportedProfile()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.CockroachDb), true, false);

        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.FastWithRisks));
        Assert.Throws<InvalidOperationException>(() => resolver.Validate(IsolationLevel.ReadCommitted));
    }

    [Fact]
    public void GetSupportedLevels_Firebird()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.Firebird), false, false);

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
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.MySql), false, false);

        Assert.Equal(IsolationLevel.RepeatableRead, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadUncommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void Resolve_MariaDb_Mappings()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.MariaDb), false, false);

        Assert.Equal(IsolationLevel.RepeatableRead, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadUncommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void Resolve_Oracle_Mappings()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.Oracle), false, false);

        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void Resolve_Db2_Mappings()
    {
        // Regression: IsolationResolver had NO case for SupportedDatabase.Db2 at all when Db2
        // support was added — it silently fell through to the generic default, giving Db2 no
        // ReadUncommitted (even though Db2's real "UR" isolation level is a standard, commonly
        // used feature) and mapping FastWithRisks to the same ReadCommitted as SafeNonBlockingReads
        // (a no-op profile). Db2's isolation levels map to standard ADO.NET IsolationLevel as:
        // UR -> ReadUncommitted, CS (default) -> ReadCommitted, RS -> RepeatableRead, RR -> Serializable.
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.Db2), false, false);

        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadUncommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void GetSupportedLevels_Db2()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.Db2), false, false);

        var levels = resolver.GetSupportedLevels().OrderBy(level => level).ToArray();
        var expected = new[]
        {
            IsolationLevel.ReadUncommitted,
            IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable
        }.OrderBy(level => level).ToArray();

        Assert.Equal(expected, levels);
    }

    [Fact]
    public void GetSupportedLevels_DuckDb()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.DuckDB), false, false);

        var levels = resolver.GetSupportedLevels().OrderBy(level => level).ToArray();
        Assert.Equal(new[] { IsolationLevel.Serializable }, levels);
    }

    [Fact]
    public void Resolve_DuckDb_Mappings()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.DuckDB), false, false);

        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void GetSupportedLevels_Sqlite()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.Sqlite), false, false);

        var levels = resolver.GetSupportedLevels().OrderBy(level => level).ToArray();
        var expected = new[] { IsolationLevel.ReadCommitted, IsolationLevel.Serializable }
            .OrderBy(level => level)
            .ToArray();

        Assert.Equal(expected, levels);
    }

    [Fact]
    public void Resolve_Sqlite_Mappings()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.Sqlite), false, false);

        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    [Theory]
    [InlineData(SupportedDatabase.TiDb, IsolationLevel.RepeatableRead)]
    [InlineData(SupportedDatabase.Snowflake, IsolationLevel.ReadCommitted)]
    public void ResolveWithDetail_StrictConsistency_FlagsDegradedWhenBelowSerializable(
        SupportedDatabase product, IsolationLevel expectedLevel)
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(product), false, false);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.StrictConsistency);

        Assert.Equal(expectedLevel, resolution.Level);
        Assert.True(resolution.Degraded);
    }

    [Fact]
    public void ResolveWithDetail_StrictConsistency_PostgreSql_NotDegraded()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.PostgreSql), false, false);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.StrictConsistency);

        Assert.Equal(IsolationLevel.Serializable, resolution.Level);
        Assert.False(resolution.Degraded);
    }

    // Regression: IsolationResolver used to hardcode a `_product is PostgreSql or YugabyteDb` check
    // in a transaction-only resolution path (ResolveForTransactionWithDetail) and throw
    // TransactionModeNotSupportedException for SafeNonBlockingReads, on the assumption that
    // PostgreSQL has no non-blocking-safe-reads equivalent. That premise was wrong: PostgreSQL's
    // REPEATABLE READ takes a transaction-start MVCC snapshot, is fully non-blocking, and (unlike
    // the ANSI baseline) also prevents phantom reads — see
    // https://www.postgresql.org/docs/current/transaction-iso.html#XACT-REPEATABLE-READ. There is
    // no separate "for transaction" resolution path or product switch anymore: PostgreSqlDialect
    // (inherited by YugabyteDb) now maps SafeNonBlockingReads to RepeatableRead directly, and
    // resolution flows through the same generic Resolve/ResolveWithDetail every other database uses.
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void Resolve_SafeNonBlockingReads_PostgresCompatibleDatabases_ResolvesToRepeatableRead(SupportedDatabase product)
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(product), false, false);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.RepeatableRead, resolution.Level);
        Assert.False(resolution.Degraded);
        Assert.Equal(IsolationResolutionKind.Exact, resolution.Kind);
    }

    [Fact]
    public void Resolve_SqlServer_SafeNonBlockingReads_ReturnsResolvedLevel()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.SqlServer), true, true);

        Assert.Equal(IsolationLevel.Snapshot,
            resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
    }

    [Fact]
    public void Resolve_StrictConsistency_NeverThrowsForPostgresCompatibleDatabases()
    {
        var resolver = new IsolationResolver(IsolationTestDialectFactory.Create(SupportedDatabase.PostgreSql), false, false);

        Assert.Equal(IsolationLevel.Serializable,
            resolver.Resolve(IsolationProfile.StrictConsistency));
    }
}