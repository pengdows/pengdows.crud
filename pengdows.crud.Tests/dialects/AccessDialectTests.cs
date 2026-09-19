#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.@internal;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Locks down <see cref="AccessDialect"/>'s capability overrides. Every fact asserted here was
/// verified live this session against a real <c>.accdb</c> file via <c>System.Data.OleDb</c> and
/// the Microsoft Access Database Engine Redistributable (both ACE 12.0 and ACE 16.0, confirmed to
/// behave identically for everything covered here) — see AccessDialect.cs's file-level AI SUMMARY
/// for the full verification trail.
/// </summary>
public class AccessDialectTests
{
    private static AccessDialect CreateDialect()
    {
        return new AccessDialect(new fakeDbFactory(SupportedDatabase.Access), NullLogger.Instance);
    }

    [Fact]
    public void DatabaseType_IsAccess()
    {
        Assert.Equal(SupportedDatabase.Access, CreateDialect().DatabaseType);
    }

    [Fact]
    public void ParameterMarker_IsQuestionMark()
    {
        Assert.Equal("?", CreateDialect().ParameterMarker);
    }

    [Fact]
    public void SupportsNamedParameters_IsFalse()
    {
        Assert.False(CreateDialect().SupportsNamedParameters);
    }

    [Fact]
    public void MakeParameterName_CollapsesToBareQuestionMark()
    {
        // Confirms the base SqlDialect.MakeParameterName positional-parameter fallback (gated on
        // SupportsNamedParameters) applies correctly — Access needs no override of its own.
        Assert.Equal("?", CreateDialect().MakeParameterName("anything"));
    }

    [Fact]
    public void SupportsMerge_IsFalse()
    {
        Assert.False(CreateDialect().SupportsMerge);
    }

    [Fact]
    public void ProcWrappingStyle_IsNone()
    {
        Assert.Equal(ProcWrappingStyle.None, CreateDialect().ProcWrappingStyle);
    }

    [Fact]
    public void IsClientServerDatabase_IsFalse()
    {
        Assert.False(CreateDialect().IsClientServerDatabase);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("name space")]
    public void WrapObjectName_UsesBrackets(string name)
    {
        // Verified live: [brackets] and `backticks` both work against real ACE, but double
        // quotes (ANSI) fail outright — brackets chosen as the idiomatic Access convention.
        Assert.Equal($"[{name}]", CreateDialect().WrapObjectName(name));
    }

    [Theory]
    [InlineData(DbMode.Best, DbMode.SingleWriter)]
    [InlineData(DbMode.PreventDatabaseUnload, DbMode.SingleWriter)]
    public void CoerceConnectionMode_CoercesToSingleWriter(DbMode requested, DbMode expected)
    {
        // Access is a file-based embedded engine (architecturally in SQLite's category, not
        // Sybase/Db2's) — coerced the same way SqliteDialect/DuckDbDialect are.
        var (mode, _) = CreateDialect().CoerceConnectionMode(requested, "Provider=Microsoft.ACE.OLEDB.16.0;Data Source=test.accdb;", isLocalDb: false);
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void CoerceConnectionMode_ExplicitStandard_IsHonored()
    {
        // Access documents support for multiple concurrent connections, so an explicit Standard
        // request is honored (Best still defaults to SingleWriter) — but this was CONFIRMED LIVE
        // to be unsafe under real write concurrency; see DescribeStandardModeRisk below and
        // AccessDialect.cs's file-level AI SUMMARY for the reproduced OleDbException.
        var (mode, reason) = CreateDialect().CoerceConnectionMode(DbMode.Standard,
            "Provider=Microsoft.ACE.OLEDB.16.0;Data Source=test.accdb;", isLocalDb: false);
        Assert.Equal(DbMode.Standard, mode);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void SupportsExternalPooling_IsFalse()
    {
        // CONFIRMED LIVE on a real Windows machine against a real .accdb: the SqlDialect base
        // default (SupportsExternalPooling => true, PoolingSettingName => "Pooling") is WRONG
        // for Access. ConnectionPoolingConfiguration.ApplyPoolingDefaults only runs for
        // Standard/PreventDatabaseUnload/SingleWriter modes, and SingleWriter's own
        // StripPoolingSetting call was masking this — every single-operation, zero-concurrency
        // DbMode.Standard call (CoerceConnectionMode_ExplicitStandard_IsHonored above) reproducibly
        // threw "OleDbException: Could not find installable ISAM" on connection open, because
        // ApplyPoolingDefaults injected an ADO.NET-style "Pooling=True" keyword into the OLE DB
        // connection string that Jet/ACE's provider does not recognize — Jet/ACE surfaces ANY
        // unrecognized connection property through this exact generic ISAM-selection error,
        // regardless of the property's real meaning. Access has no ADO.NET-style pooling
        // keyword at all (architecturally identical to DuckDbDialect's SupportsExternalPooling
        // => false, "in-process" — not a client-server provider concept), so this must be false.
        Assert.False(CreateDialect().SupportsExternalPooling);
    }

    [Fact]
    public void PoolingSettingName_IsNull()
    {
        Assert.Null(CreateDialect().PoolingSettingName);
    }

    [Fact]
    public void ApplyPoolingDefaults_UnderStandardMode_DoesNotInjectPoolingKeyword()
    {
        // The actual regression this locks down: with the dialect's own SupportsExternalPooling
        // and PoolingSettingName values fed through the real call path DatabaseContext uses
        // (DatabaseContext.Initialization.cs's SetupConnectionStringsAndSessionSettings), the
        // connection string that reaches OleDbConnection must come back byte-for-byte unchanged
        // — any injected "Pooling=..." keyword is what broke a real Windows/ACE connection.
        var dialect = CreateDialect();
        const string original = "Provider=Microsoft.ACE.OLEDB.16.0;Data Source=test.accdb;";

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            original,
            SupportedDatabase.Access,
            DbMode.Standard,
            dialect.SupportsExternalPooling,
            dialect.PoolingSettingName);

        Assert.Equal(original, result);
    }

    [Fact]
    public void ReadOnlyPoolDiscriminator_UsesLockingModeDefaultValue()
    {
        // Access has no ApplicationNameSettingName (confirmed live: "Application Name" is
        // equally unrecognized by ACE as "Pooling" was — any unrecognized keyword throws the
        // same generic "Could not find installable ISAM"). Without a discriminator override,
        // BuildReaderConnectionString's fallback (DatabaseContext.Initialization.cs) never fires
        // for Access, so the reader and writer connection strings end up byte-for-byte identical
        // and System.Data.OleDb's own connection pool (keyed by exact connection string) collapses
        // them into ONE shared physical pool — unlike Oracle, which solved the identical problem
        // via ReadOnlyPoolDiscriminatorSettingName => "Metadata Pooling"/"false".
        // "Jet OLEDB:Database Locking Mode=1" is CONFIRMED live (both recognized by ACE, and
        // behaviorally inert against 4 runs each of the same 20-writer contention pattern: ~13-14
        // /20 failures with or without it) to be ACE's own documented default value, so setting
        // it explicitly on the reader string only differentiates the connection-string TEXT
        // without changing real locking behavior — the same trick Oracle's dialect already uses.
        var dialect = CreateDialect();
        Assert.Equal("Jet OLEDB:Database Locking Mode", dialect.ReadOnlyPoolDiscriminatorSettingName);
        Assert.Equal("1", dialect.ReadOnlyPoolDiscriminatorSettingValue);
    }

    [Fact]
    public void DescribeStandardModeRisk_NamesConfirmedLiveFailure()
    {
        // Re-verified end-to-end through pengdows.crud's own transaction API this session (see
        // AccessDialect.cs's file-level AI SUMMARY): the failure surfaces as CommandTimeoutException
        // once pengdows.crud's own exception translation runs, not the raw OleDbException directly.
        var risk = CreateDialect().DescribeStandardModeRisk();
        Assert.Contains("CommandTimeoutException", risk);
        Assert.Contains("currently locked", risk);
        Assert.Contains("SingleWriter", risk);
    }

    [Fact]
    public void DescribeStandardModeRisk_HasNoSafeZoneUnlikeDuckDb()
    {
        // Unlike DuckDbDialect's DescribeStandardModeRisk (which correctly tells a caller "avoid
        // concurrent writers to the same row and Standard is fine" — a genuine, confirmed-safe
        // escape hatch, since DuckDB's optimistic concurrency lets disjoint-row writers through
        // cleanly), Access has no equivalent safe zone: confirmed live that the conflict spans
        // disjoint rows in the same table AND disjoint rows across two different tables in the
        // same file. The warning text must say so explicitly — a caller skimming only the
        // DuckDB-shaped "just don't touch the same row" advice would otherwise wrongly assume
        // the same escape hatch applies here.
        var risk = CreateDialect().DescribeStandardModeRisk();
        Assert.Contains("no safe", risk, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("different tables", risk, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectInMemoryKind_IsAlwaysNone()
    {
        // Access has no in-memory mode at all, unlike SQLite's ":memory:".
        Assert.Equal(InMemoryKind.None, CreateDialect().DetectInMemoryKind("Provider=Microsoft.ACE.OLEDB.16.0;Data Source=test.accdb;"));
    }

    [Fact]
    public void GetSupportedIsolationLevels_OnlyReadUncommittedAndReadCommitted()
    {
        // Verified live: RepeatableRead/Serializable/Snapshot all throw
        // "Neither the isolation level nor a strengthening of it is supported."
        var levels = CreateDialect().GetSupportedIsolationLevels(allowSnapshotIsolation: false);
        Assert.Equal(new HashSet<IsolationLevel> { IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted }, levels);
    }

    [Fact]
    public void GetLastInsertedIdQuery_UsesAtAtIdentity()
    {
        // Verified live: SELECT @@IDENTITY works over OLE DB against a real .accdb COUNTER column.
        Assert.Equal("SELECT @@IDENTITY", CreateDialect().GetLastInsertedIdQuery());
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_UsesSelectTopOne()
    {
        var sql = CreateDialect().GetNaturalKeyLookupQuery(
            "my_table", "id", new[] { "name" }, new[] { "?" });

        Assert.Contains("SELECT TOP 1", sql);
        Assert.DoesNotContain("LIMIT", sql);
    }

    // ---- Constraint-kind classification: pure message-substring matching, since OleDbException
    // reports an identical generic ErrorCode (-2147467259) and an empty Errors collection for
    // every violation kind (confirmed live) — no numeric discrimination is possible at all.
    // Exact message text captured live against a real .accdb (identical on both ACE 12.0/16.0).

    [Fact]
    public void IsUniqueViolation_DuplicateIndexMessage_ReturnsTrue()
    {
        var ex = new PlainMessageDbException(
            "The changes you requested to the table were not successful because they would create duplicate values in the index, primary key, or relationship. Change the data in the field or fields that contain duplicate data, remove the index, or redefine the index to permit duplicate entries and try again.");
        Assert.True(CreateDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_MustEnterValueMessage_ReturnsTrue()
    {
        var ex = new PlainMessageDbException("You must enter a value in the 'parent_t.name' field.");
        Assert.True(CreateDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_ValidationRuleMessage_ReturnsTrue()
    {
        var ex = new PlainMessageDbException(
            "One or more values are prohibited by the validation rule 'chk_age' set for 'parent_t'. Enter a value that the expression for this field can accept.");
        Assert.True(CreateDialect().IsCheckConstraintViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_InsertBlockedByMissingParent_ReturnsTrue()
    {
        var ex = new PlainMessageDbException(
            "You cannot add or change a record because a related record is required in table 'parent_t'.");
        Assert.True(CreateDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_DeleteBlockedByChildRow_ReturnsTrue()
    {
        // Verified live this session that Access's INSERT-blocked and DELETE-blocked FK messages
        // use completely different wording ("a related record is required" vs. "includes related
        // records") — the exact SQL Server pitfall CLAUDE.md's checklist item 22 warns about.
        // "related record" is a substring of both, deliberately chosen to cover both shapes.
        var ex = new PlainMessageDbException(
            "The record cannot be deleted or changed because table 'child_t' includes related records.");
        Assert.True(CreateDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void GetReadOnlyConnectionParameter_UsesModeRead()
    {
        // CONFIRMED live: "Mode=Read" is a real, recognized OLE DB/Jet connection-string
        // property (distinct from ADO.NET's own "Pooling"/"Application Name", which are NOT
        // recognized — confirmed live to fail identically to the pooling bug above) that
        // genuinely enforces read-only at the driver level: a write attempted against a
        // Mode=Read connection fails with "Operation must use an updateable query.", while a
        // Mode=ReadWrite control connection succeeds normally. Also confirmed live (3/3 trials)
        // that a held-open Mode=Read connection does NOT block a concurrent writer on the same
        // file — unlike DuckDbDialect.ReadOnlyConnectionsCanBlockConcurrentWriters (true), so
        // Access correctly stays at the SqlDialect base default (false) with no override needed.
        Assert.Equal("Mode=Read", CreateDialect().GetReadOnlyConnectionParameter());
    }

    [Fact]
    public void ReadOnlyConnectionsCanBlockConcurrentWriters_IsFalse()
    {
        Assert.False(CreateDialect().ReadOnlyConnectionsCanBlockConcurrentWriters);
    }

    [Fact]
    public void AnalyzeException_UpdateableQueryMessage_ClassifiesAsReadOnlyViolation()
    {
        var ex = new PlainMessageDbException("Operation must use an updateable query.");
        var info = CreateDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ReadOnlyViolation, info.Category);
    }

    [Fact]
    public void AnalyzeException_CurrentlyLockedMessage_ClassifiesAsTimeout()
    {
        // CONFIRMED live this session: two connections through the SAME AccessDialect-backed
        // DatabaseContext (before DbMode.SingleWriter's governor was in place, i.e. via raw
        // OleDb) reproduced a real lock-wait scenario — a second connection blocked while a
        // first held an open write transaction, then failed outright with this exact message
        // once contention resolved. This is a genuine lock-WAIT scenario (blocks, then gives up),
        // not a detected circular-wait deadlock — matches HanaDialect's own "lock wait timeout"
        // precedent (SAP error 131 -> Timeout), not its "detected deadlock" one (error 133 ->
        // Deadlock).
        var ex = new PlainMessageDbException("Could not update; currently locked.");
        var info = CreateDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.Timeout, info.Category);
    }

    [Fact]
    public void IsUniqueViolation_UnrelatedMessage_ReturnsFalse()
    {
        var ex = new PlainMessageDbException("Syntax error in query. Incomplete query clause.");
        Assert.False(CreateDialect().IsUniqueViolation(ex));
        Assert.False(CreateDialect().IsForeignKeyViolation(ex));
        Assert.False(CreateDialect().IsNotNullViolation(ex));
        Assert.False(CreateDialect().IsCheckConstraintViolation(ex));
    }
}
