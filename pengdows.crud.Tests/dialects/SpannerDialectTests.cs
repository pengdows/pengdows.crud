using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.isolation;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Locks down <see cref="SpannerDialect"/>'s capability overrides — each one verified live
/// against a real Spanner Omni + PGAdapter instance during the investigation that added this
/// file (see CLAUDE.md's "Adding a New Database" checklist, items 15-18, for the detection and
/// type-mapping bugs that investigation also uncovered and fixed). Spanner's PostgreSQL interface
/// shares most of its SQL surface with real PostgreSQL (it extends <see cref="PostgreSqlDialect"/>),
/// but diverges on several specific capabilities this file exists to pin down.
/// </summary>
public class SpannerDialectTests
{
    private static SpannerDialect CreateDialect() =>
        new(new fakeDbFactory(SupportedDatabase.Spanner), NullLogger.Instance);

    [Fact]
    public void DatabaseType_IsSpanner()
    {
        Assert.Equal(SupportedDatabase.Spanner, CreateDialect().DatabaseType);
    }

    [Fact]
    public void SupportsMerge_IsFalse()
    {
        // Verified live: `MERGE INTO ...` against a real Spanner Omni + PGAdapter instance fails
        // with "ERROR: Unknown statement: MERGE INTO ..." — unimplemented, not version-gated.
        Assert.False(CreateDialect().SupportsMerge);
    }

    [Fact]
    public void SupportsBatchUpdate_IsFalse()
    {
        // Verified live: PostgreSqlDialect's optimized batch-update SQL
        // ("UPDATE t SET ... FROM (VALUES (@b0, ...)) AS s(...) WHERE t.pk = s.pk") fails against
        // real Spanner with "42883: operator does not exist: bigint = text" — Spanner's query
        // planner doesn't infer the VALUES-derived table's column types from the joined column
        // the way real PostgreSQL does, so the untyped VALUES parameter stays "text" and the
        // comparison to a typed key column is rejected outright. Falls back to one BuildUpdate
        // container per entity instead (the same safe fallback SQLite/MySQL/MariaDB/Firebird use).
        Assert.False(CreateDialect().SupportsBatchUpdate);
    }

    [Fact]
    public void SupportsInsertOnConflict_IsTrue()
    {
        // Verified live: `INSERT ... ON CONFLICT (id) DO UPDATE`/`DO NOTHING` both execute
        // correctly against real Spanner — inherited from PostgreSqlDialect, not overridden here,
        // because the inherited `true` is actually correct (unlike SupportsMerge above).
        Assert.True(CreateDialect().SupportsInsertOnConflict);
    }

    [Fact]
    public void SupportsSavepoints_IsFalse()
    {
        Assert.False(CreateDialect().SupportsSavepoints);
    }

    [Fact]
    public void SupportsOverridingSystemValue_IsFalse()
    {
        Assert.False(CreateDialect().SupportsOverridingSystemValue);
    }

    [Fact]
    public void SupportsSetValuedParameters_IsFalse()
    {
        Assert.False(CreateDialect().SupportsSetValuedParameters);
    }

    [Fact]
    public void ProcWrappingStyle_IsNone()
    {
        // Spanner has no stored-procedure support at all — StoredProcedureTests' SkippableFact
        // checks skip cleanly on ProcWrappingStyle.None rather than attempting CALL/EXEC syntax.
        Assert.Equal(ProcWrappingStyle.None, CreateDialect().ProcWrappingStyle);
    }

    [Fact]
    public void ReadCommittedCompatibleIsolationLevel_IsSerializable()
    {
        Assert.Equal(IsolationLevel.Serializable, CreateDialect().ReadCommittedCompatibleIsolationLevel);
    }

    [Fact]
    public void GetSupportedIsolationLevels_ExcludesReadCommitted()
    {
        // Spanner has no distinct READ COMMITTED level — its PostgreSQL interface only exposes
        // RepeatableRead and Serializable, unlike plain PostgreSqlDialect's three-level set.
        var levels = CreateDialect().GetSupportedIsolationLevels(allowSnapshotIsolation: true);

        Assert.Equal(new HashSet<IsolationLevel> { IsolationLevel.RepeatableRead, IsolationLevel.Serializable },
            levels);
    }

    [Fact]
    public void GetIsolationProfileMapping_MapsAllThreeProfiles()
    {
        var mapping = CreateDialect().GetIsolationProfileMapping(allowSnapshotIsolation: true);

        Assert.Equal(IsolationLevel.RepeatableRead, mapping[IsolationProfile.SafeNonBlockingReads]);
        Assert.Equal(IsolationLevel.Serializable, mapping[IsolationProfile.StrictConsistency]);
        Assert.Equal(IsolationLevel.RepeatableRead, mapping[IsolationProfile.FastWithRisks]);
    }

    [Fact]
    public void GetBaseSessionSettings_IsEmpty()
    {
        // PostgreSqlDialect's base session settings (standard_conforming_strings,
        // client_min_messages, default_transaction_read_only) are PGAdapter-incompatible SET
        // targets for Spanner, hence the override to an empty baseline rather than inheriting them.
        Assert.Equal(string.Empty, CreateDialect().GetBaseSessionSettings());
    }

    [Fact]
    public void MaxOutputParameters_InheritsPostgreSqlValue()
    {
        // Not overridden — Spanner has no stored procedures at all (ProcWrappingStyle.None
        // above), so this inherited PostgreSQL-family value is moot in practice but should stay
        // consistent with the rest of the Postgres-wire family rather than silently drift.
        Assert.Equal(100, CreateDialect().MaxOutputParameters);
    }

    // ── IsForeignKeyViolation ────────────────────────────────────────────────
    // Verified live against a real Spanner Omni + PGAdapter instance: an INSERT referencing a
    // nonexistent parent row uses the real ANSI SqlState "23503" (inherited PostgreSqlDialect
    // check already handles it), but a DELETE blocked by a still-referencing child row instead
    // returns the generic "P0001" code with a distinct message — without the message-pattern
    // override, that DELETE case surfaced as a generic DatabaseOperationException instead of
    // ForeignKeyViolationException.

    [Fact]
    public void IsForeignKeyViolation_Spanner_SqlState23503_ReturnsTrue()
    {
        // The INSERT-side shape — confirms the inherited PostgreSqlDialect SqlState check still
        // applies; this override must not narrow it.
        var ex = new SqlStateDbException("23503", "Foreign key constraint `fk_x` is violated on table `t`.");
        Assert.True(CreateDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_Spanner_DeleteBlockedMessage_ReturnsTrue()
    {
        // Real captured message (SqlState "P0001", not 23503).
        var ex = new PlainDbException(
            "P0001: Foreign key constraint violation when deleting or updating referenced row(s): " +
            "referencing row(s) found in table `test_related`.");
        Assert.True(CreateDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_Spanner_UnrelatedMessage_ReturnsFalse()
    {
        var ex = new PlainDbException("P0001: Check constraint `t`.`chk_x` is violated for key (1)");
        Assert.False(CreateDialect().IsForeignKeyViolation(ex));
    }

    private sealed class PlainDbException : System.Data.Common.DbException
    {
        public PlainDbException(string message) : base(message)
        {
        }
    }

    private sealed class SqlStateDbException : System.Data.Common.DbException
    {
        public SqlStateDbException(string sqlState, string message) : base(message)
        {
            SqlState = sqlState;
        }

        public override string? SqlState { get; }
    }
}
