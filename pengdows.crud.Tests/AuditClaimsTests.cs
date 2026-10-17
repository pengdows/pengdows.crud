#pragma warning disable CS0618 // TypeCoercionOptions etc. are internal plumbing here
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.isolation;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Each test proves or disproves one assertion of the external audit of 2.0.6 (pengdows-additions,
/// llms-verification-map.md), by ID. A test that passes confirms the claim about today's behaviour; the
/// ones that confirmed a defect were fixed in their own commits (G-01, G-04) and live elsewhere.
/// </summary>
public class AuditClaimsTests
{
    // ---- G-02: the default PoolAcquireTimeout is 5 s --------------------------------------------------------------
    [Fact]
    public void G02_DefaultPoolAcquireTimeout_IsFiveSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(5), new DatabaseContextConfiguration().PoolAcquireTimeout);

    // ---- I1: RCSI is passed to the resolver but never decides anything --------------------------------------------
    private static IsolationResolver SqlServerResolver(bool rcsi, bool snapshot)
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.SqlServer,
            new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);
        return new IsolationResolver(dialect, rcsi, snapshot);
    }

    [Fact]
    public void I1_SqlServerWithNeitherSnapshotIsolationNorRcsi_RefusesSafeNonBlockingReads()
    {
        var resolver = SqlServerResolver(rcsi: false, snapshot: false);

        var detail = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.True(detail.Degraded);
        Assert.Throws<TransactionModeNotSupportedException>(
            () => resolver.ResolveForTransaction(IsolationProfile.SafeNonBlockingReads));
    }

    // RCSI makes READ COMMITTED read row versions: readers do not block on writers and never see dirty data. That is the same
    // statement-level MVCC the profile already accepts as ReadCommitted on Oracle and MySQL, so it satisfies the profile.
    [Fact]
    public void I1_SqlServerWithRcsi_AcceptsSafeNonBlockingReads_AsReadCommitted()
    {
        var resolver = SqlServerResolver(rcsi: true, snapshot: false);

        var detail = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.False(detail.Degraded);
        Assert.Equal(IsolationLevel.ReadCommitted, detail.Level);
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.ResolveForTransaction(IsolationProfile.SafeNonBlockingReads));
    }

    [Fact]
    public void I1_RcsiChangesOnlySafeNonBlockingReads()
    {
        var withRcsi = SqlServerResolver(rcsi: true, snapshot: false);
        var withoutRcsi = SqlServerResolver(rcsi: false, snapshot: false);

        foreach (var profile in Enum.GetValues<IsolationProfile>().Where(p => p != IsolationProfile.SafeNonBlockingReads))
        {
            Assert.Equal(withoutRcsi.ResolveWithDetail(profile), withRcsi.ResolveWithDetail(profile));
        }

        Assert.Equal(withoutRcsi.GetSupportedLevels(), withRcsi.GetSupportedLevels());
    }

    [Fact]
    public void I1_SqlServerWithSnapshotIsolation_AcceptsSafeNonBlockingReads()
    {
        var resolver = SqlServerResolver(rcsi: false, snapshot: true);

        Assert.Equal(IsolationLevel.Snapshot, resolver.ResolveForTransaction(IsolationProfile.SafeNonBlockingReads));
    }

    // ---- I2: "cannot satisfy" surfaces as three unrelated exception types -----------------------------------------
    // Pinned as 2.x behaviour: unifying them would break callers that catch one of them (a 3.0 change).
    [Fact]
    public void I2_ADegradedProfile_ThrowsTransactionModeNotSupportedException()
    {
        var resolver = SqlServerResolver(rcsi: false, snapshot: false);

        var ex = Assert.Throws<TransactionModeNotSupportedException>(
            () => resolver.ResolveForTransaction(IsolationProfile.SafeNonBlockingReads));

        Assert.IsAssignableFrom<NotSupportedException>(ex);
    }

    [Fact]
    public void I2_ALevelNothingSatisfies_ThrowsInvalidOperationException()
    {
        var tidb = (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.TiDb,
            new fakeDbFactory(SupportedDatabase.TiDb), NullLogger.Instance);
        var resolver = new IsolationResolver(tidb, false, false);

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveAtLeast(IsolationLevel.Serializable));
        Assert.Throws<InvalidOperationException>(() => resolver.Validate(IsolationLevel.Serializable));
    }

    [Fact]
    public void I2_AnUnmappedProfile_ThrowsTransactionModeNotSupported_ANotSupportedException()
    {
        var resolver = SqlServerResolver(rcsi: false, snapshot: true);

        var ex = Assert.Throws<TransactionModeNotSupportedException>(() => resolver.ResolveWithDetail((IsolationProfile)999));
        Assert.IsAssignableFrom<NotSupportedException>(ex);
    }

    // ---- G-05 / G-10: which upserts can reveal a stale [Version] --------------------------------------------------
    [Table("versioned_things")]
    private sealed class Versioned
    {
        [Id] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Version] [Column("version", DbType.Int32)] public int Version { get; set; } = 1;
    }

    [Table("opaque_things")]
    private sealed class OpaqueVersioned
    {
        [Id] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Version] [Column("version", DbType.Binary)] public byte[] Version { get; set; } = Array.Empty<byte>();
    }

    private static (DatabaseContext Context, fakeDbConnection Connection) Create(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var connection = new fakeDbConnection
        {
            EmulatedProduct = db,
            ConnectionString = $"Data Source=test;EmulatedProduct={db}"
        };
        factory.Connections.Add(connection);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            DbMode = DbMode.SingleConnection
        }, factory), connection);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Sqlite)]
    public async Task G05_AStaleVersion_IsDetected_WhereTheUpsertCarriesAGuard(SupportedDatabase db)
    {
        var (context, connection) = Create(db);
        await using var _ = context;
        var gateway = new TableGateway<Versioned, long>(context);
        connection.EnqueueNonQueryResult(0); // the guarded statement skipped the stale row

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            async () => await gateway.UpsertAsync(new Versioned { Id = 1, Name = "n", Version = 3 }));
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task G05_AStaleVersion_CannotBeDetected_OnMySqlFamilyAndFirebird(SupportedDatabase db)
    {
        var (context, connection) = Create(db);
        await using var _ = context;
        var gateway = new TableGateway<Versioned, long>(context);
        connection.EnqueueNonQueryResult(0);

        // Declared, not a defect: rows affected cannot reveal the skip, so no exception is raised.
        var rows = await gateway.UpsertAsync(new Versioned { Id = 1, Name = "n", Version = 3 });

        Assert.Equal(0, rows);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public async Task G10_AnOpaqueByteArrayVersion_IsNeverGuardedOnSingleRowUpsert(SupportedDatabase db)
    {
        var (context, connection) = Create(db);
        await using var _ = context;
        var gateway = new TableGateway<OpaqueVersioned, long>(context);
        connection.EnqueueNonQueryResult(0);

        // No exception even though the dialect can guard an integer version: an opaque (rowversion) value is DB-generated.
        var rows = await gateway.UpsertAsync(new OpaqueVersioned { Id = 1, Name = "n", Version = new byte[] { 1 } });

        Assert.Equal(0, rows);
    }
}
