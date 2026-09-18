#region

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
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
    [InlineData(DbMode.Standard, DbMode.SingleWriter)]
    public void CoerceConnectionMode_CoercesToSingleWriter(DbMode requested, DbMode expected)
    {
        // Access is a file-based embedded engine (architecturally in SQLite's category, not
        // Sybase/Db2's) — coerced the same way SqliteDialect/DuckDbDialect are.
        var (mode, _) = CreateDialect().CoerceConnectionMode(requested, "Provider=Microsoft.ACE.OLEDB.16.0;Data Source=test.accdb;", isLocalDb: false);
        Assert.Equal(expected, mode);
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
