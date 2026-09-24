#region

using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.isolation;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Locks down <see cref="AccessDialect"/>'s capability overrides. Every fact asserted here was
/// live-verified on pengdows.crud 3.0 against a real <c>.accdb</c> file using both
/// Microsoft.ACE.OLEDB.12.0 and Microsoft.ACE.OLEDB.16.0 — see AccessDialect.cs's file-level AI
/// SUMMARY for the full research trail this port carries over unchanged. This branch (2.0.6)
/// cannot re-run that live verification itself (no ACE-backed testbed run in this session — see
/// docs/connection/access-concurrency-verification.md for the original run), so these tests pin
/// down the ported *behavior*, not re-derive it.
/// </summary>
public class AccessDialectTests
{
    private static AccessDialect CreateDialect()
    {
        return new AccessDialect(new fakeDbFactory(SupportedDatabase.Access), NullLogger<AccessDialect>.Instance);
    }

    private static IDatabaseContext CreateContext()
    {
        return new DatabaseContext("Data Source=test;EmulatedProduct=Access", new fakeDbFactory(SupportedDatabase.Access));
    }

    [Fact]
    public void DatabaseType_IsAccess()
    {
        Assert.Equal(SupportedDatabase.Access, CreateDialect().DatabaseType);
    }

    // Confirmed live: ParameterMarkerFormat = "?" from GetSchema; no named-parameter support.
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

    // CONFIRMED live: ANSI double-quotes are rejected outright; brackets are the idiomatic Access
    // convention (backticks also work but brackets were chosen).
    [Fact]
    public void QuotePrefix_And_Suffix_AreBrackets()
    {
        var d = CreateDialect();
        Assert.Equal("[", d.QuotePrefix);
        Assert.Equal("]", d.QuoteSuffix);
    }

    [Fact]
    public void IsClientServerDatabase_IsFalse()
    {
        Assert.False(CreateDialect().IsClientServerDatabase);
    }

    [Fact]
    public void IsEmbeddedSingleWriterEngine_IsTrue()
    {
        Assert.True(CreateDialect().IsEmbeddedSingleWriterEngine);
    }

    [Fact]
    public void DetectInMemoryKind_AlwaysNone()
    {
        Assert.Equal(InMemoryKind.None, CreateDialect().DetectInMemoryKind("Provider=Microsoft.ACE.OLEDB.16.0;Data Source=C:\\db.accdb;"));
    }

    // CONFIRMED LIVE (re-verified on a real Windows machine): the SqlDialect base defaults
    // (SupportsExternalPooling => true, PoolingSettingName => "Pooling") caused every
    // DbMode.Standard connection to fail outright with "Could not find installable ISAM" — Jet/ACE
    // has no ADO.NET-style external pooling switch at all.
    [Fact]
    public void SupportsExternalPooling_IsFalse()
    {
        Assert.False(CreateDialect().SupportsExternalPooling);
    }

    [Fact]
    public void PoolingSettingName_IsNull()
    {
        Assert.Null(CreateDialect().PoolingSettingName);
    }

    // CONFIRMED live: "Jet OLEDB:Database Locking Mode"="1" both recognized and behaviorally
    // inert (1 = row-level locking, already ACE's own documented default) — differentiates the
    // reader/writer pool key without changing real behavior. Mirrors OracleDialect's identical fix.
    [Fact]
    public void ReadOnlyPoolDiscriminatorSettingName_IsJetLockingMode()
    {
        Assert.Equal("Jet OLEDB:Database Locking Mode", CreateDialect().ReadOnlyPoolDiscriminatorSettingName);
    }

    [Fact]
    public void ReadOnlyPoolDiscriminatorSettingValue_Is1()
    {
        Assert.Equal("1", CreateDialect().ReadOnlyPoolDiscriminatorSettingValue);
    }

    // CoerceConnectionMode: CONFIRMED LIVE end-to-end (20 concurrent writers holding open
    // transactions: 14-18/20 CommandTimeoutException failures under Standard, 0/20 under
    // SingleWriter) that SingleWriter is the only mode confirmed both correct and fully
    // concurrent — but an explicit Standard request is honored (allowStandard: true), unlike
    // SQLite's hard coercion.
    [Fact]
    public void CoerceConnectionMode_Best_ResolvesToSingleWriter()
    {
        var (mode, _) = CreateDialect().CoerceConnectionMode(DbMode.Best, null, false);
        Assert.Equal(DbMode.SingleWriter, mode);
    }

    [Fact]
    public void CoerceConnectionMode_ExplicitStandard_IsHonored()
    {
        var (mode, reason) = CreateDialect().CoerceConnectionMode(DbMode.Standard, null, false);
        Assert.Equal(DbMode.Standard, mode);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void CoerceConnectionMode_ExplicitSingleWriter_IsHonored()
    {
        var (mode, _) = CreateDialect().CoerceConnectionMode(DbMode.SingleWriter, null, false);
        Assert.Equal(DbMode.SingleWriter, mode);
    }

    [Fact]
    public void CoerceConnectionMode_ExplicitSingleConnection_IsHonored()
    {
        var (mode, _) = CreateDialect().CoerceConnectionMode(DbMode.SingleConnection, null, false);
        Assert.Equal(DbMode.SingleConnection, mode);
    }

    [Fact]
    public void CoerceConnectionMode_PreventDatabaseUnload_CoercesToSingleWriter()
    {
        var (mode, reason) = CreateDialect().CoerceConnectionMode(DbMode.PreventDatabaseUnload, null, false);
        Assert.Equal(DbMode.SingleWriter, mode);
        Assert.NotEqual(string.Empty, reason);
    }

    // CONFIRMED LIVE: unlike DuckDB (which has a genuine safe zone — disjoint-row writers proceed
    // cleanly), Access has no safe zone: even disjoint rows across two different tables failed
    // under real write concurrency.
    [Fact]
    public void DescribeStandardModeRisk_DescribesConfirmedLiveFailure()
    {
        var risk = CreateDialect().DescribeStandardModeRisk();
        Assert.Contains("CONFIRMED LIVE", risk);
        Assert.Contains("currently locked", risk);
    }

    // No MERGE/ON CONFLICT/ON DUPLICATE KEY of any kind — CONFIRMED live that BuildUpsert
    // correctly throws NotSupportedException as a result.
    [Fact]
    public void SupportsMerge_IsFalse()
    {
        Assert.False(CreateDialect().SupportsMerge);
    }

    // Jet/ACE SQL has no TRUNCATE TABLE statement.
    [Fact]
    public void SupportsTruncateTable_IsFalse()
    {
        Assert.False(CreateDialect().SupportsTruncateTable);
    }

    // CONFIRMED live: Jet/ACE rejects the ANSI multi-row VALUES clause outright ("Syntax error
    // (missing operator)") — matches Informix/SAP HANA/InterBase's identical limitation.
    [Fact]
    public void SupportsBatchInsert_IsFalse()
    {
        Assert.False(CreateDialect().SupportsBatchInsert);
    }

    // CONFIRMED live: Jet/ACE has no OFFSET/FETCH or LIMIT/OFFSET paging syntax at all — only
    // SELECT TOP n, with no "skip n rows" capability — matches SqlServer/Sybase/InterBase's
    // identical TOP-only limitation.
    [Fact]
    public void SupportsOffsetFetch_IsFalse()
    {
        Assert.False(CreateDialect().SupportsOffsetFetch);
    }

    [Fact]
    public void SupportsLimitOffset_IsFalse()
    {
        Assert.False(CreateDialect().SupportsLimitOffset);
    }

    // No ADO.NET-invocable stored procedures.
    [Fact]
    public void ProcWrappingStyle_IsNone()
    {
        Assert.Equal(ProcWrappingStyle.None, CreateDialect().ProcWrappingStyle);
    }

    // CONFIRMED live (this session, via a real .accdb): the generic positional-dialect
    // bool->Int16(1/0) conversion does NOT round-trip against Jet's YESNO type — True is stored
    // as -1 (classic Access convention), so "WHERE bool_val = ?" bound as Int16(1) matched zero
    // rows. AdvancedTypeRegistry.RegisterAccessMappings binds the native OleDbType.Boolean
    // instead (reflection-based, same mechanism as the DateTime->OleDbType.Date fix), which
    // round-trips correctly for both INSERT and equality comparison — CreateDbParameter itself
    // only proves no exception is thrown against fakeDb's generic DbParameter (which has no real
    // OleDbType property to reflect onto); the actual round-trip is proven live by the opt-in
    // testbed (AccessTestProvider, via TestProvider.TestParameterBinding's type matrix).
    [Fact]
    public void CreateDbParameter_Boolean_DoesNotThrow_AndKeepsBooleanDbType()
    {
        var d = CreateDialect();
        var param = d.CreateDbParameter("p", DbType.Boolean, true);
        Assert.Equal(DbType.Boolean, param.DbType);
    }

    // CONFIRMED live: a Guid parameter bound via the DbType.String reassignment round-trips
    // correctly — Access has no native UUID/GUID type.
    [Fact]
    public void GuidFormat_IsString()
    {
        var d = CreateDialect();
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var param = d.CreateDbParameter("p", DbType.Guid, guid);
        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", param.Value?.ToString());
    }

    // CONFIRMED live: "Mode=Read" is a real, recognized OLE DB/Jet property that genuinely
    // enforces read-only at the driver level.
    [Fact]
    public void GetReadOnlyConnectionParameter_ReturnsModeRead()
    {
        Assert.Equal("Mode=Read", CreateDialect().GetReadOnlyConnectionParameter());
    }

    // CONFIRMED live: SELECT @@IDENTITY works over OLE DB against a real .accdb COUNTER column.
    [Fact]
    public void GetLastInsertedIdQuery_ReturnsSelectAtAtIdentity()
    {
        Assert.Equal("SELECT @@IDENTITY", CreateDialect().GetLastInsertedIdQuery());
    }

    // No SQL-queryable version function in Jet SQL at all.
    [Fact]
    public void GetVersionQuery_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CreateDialect().GetVersionQuery());
    }

    [Fact]
    public void ExtractProductNameFromVersion_ReturnsMSJet()
    {
        Assert.Equal("MS Jet", CreateDialect().ExtractProductNameFromVersion("04.00.0000"));
    }

    // Natural-key lookup: CONFIRMED live that Access uses SELECT TOP n, not the base class's
    // generic LIMIT-based fallback — inlined as a SupportedDatabase.Access case in
    // SqlDialect.GetNaturalKeyLookupQuery on this branch (mirrors SqlServer/Sybase).
    [Fact]
    public void GetNaturalKeyLookupQuery_UsesSelectTopOne_NoLimitSuffix()
    {
        var d = CreateDialect();
        var sql = d.GetNaturalKeyLookupQuery("orders", "id", new[] { "order_code" }, new[] { "?" });
        Assert.StartsWith("SELECT TOP 1 [id] FROM [orders] WHERE", sql);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── Isolation ────────────────────────────────────────────────────────────
    // CONFIRMED live: only ReadUncommitted/ReadCommitted are accepted by
    // OleDbConnection.BeginTransaction; RepeatableRead/Serializable/Snapshot all throw. Data
    // lives in IsolationResolver.cs on this branch (2.0.6 predates 3.0's dialect-owned
    // GetSupportedIsolationLevels/GetIsolationProfileMapping hooks).
    [Fact]
    public void IsolationResolver_SupportsOnlyReadUncommittedAndReadCommitted()
    {
        var resolver = new IsolationResolver(SupportedDatabase.Access, false, false);
        var levels = resolver.GetSupportedLevels();
        Assert.Contains(IsolationLevel.ReadUncommitted, levels);
        Assert.Contains(IsolationLevel.ReadCommitted, levels);
        Assert.DoesNotContain(IsolationLevel.RepeatableRead, levels);
        Assert.DoesNotContain(IsolationLevel.Serializable, levels);
        Assert.DoesNotContain(IsolationLevel.Snapshot, levels);
    }

    [Fact]
    public void IsolationResolver_MapsSafeAndStrictToReadCommitted_FastToReadUncommitted()
    {
        var resolver = new IsolationResolver(SupportedDatabase.Access, false, false);
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadUncommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    // ── Exception classification ────────────────────────────────────────────
    // CONFIRMED live against a real .accdb: OleDbException.ErrorCode is always the identical
    // generic COM HRESULT (-2147467259) for every violation kind — no numeric discrimination is
    // possible, only English message-text substring matching.

    [Fact]
    public void IsUniqueViolation_DuplicateValuesMessage_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("The changes you requested to the table were not successful because they would create duplicate values in the index, primary key, or relationship.");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_MustEnterAValueMessage_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("You must enter a value in the 'orders.customer_id' field.");
        Assert.True(ctx.GetDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_ValidationRuleMessage_ReturnsTrue()
    {
        var ex = new PlainMessageDbException(
            "One or more values are prohibited by the validation rule 'chk_age' set for 'parent_t'. Enter a value that the expression for this field can accept.");
        Assert.True(CreateDialect().IsCheckConstraintViolation(ex));
    }

    // Covers both real message shapes confirmed live: INSERT blocked by a missing parent row and
    // DELETE blocked by an existing child row — "related record" is a substring of both.
    [Theory]
    [InlineData("The record cannot be deleted or changed because table 'order_items' includes related records.")]
    [InlineData("You cannot add or change a record because a related record is required in table 'customers'.")]
    public void IsForeignKeyViolation_EitherDirectionMessage_ReturnsTrue(string message)
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException(message);
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
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

    // CONFIRMED live: the exact message a real ACE connection opened with "Mode=Read" returns
    // when a write is attempted against it.
    [Fact]
    public void AnalyzeException_MustUseUpdateableQueryMessage_ClassifiesAsReadOnlyViolation()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("Operation must use an updateable query.");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ReadOnlyViolation, info.Category);
    }

    [Fact]
    public void AnalyzeException_UniqueViolation_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("The changes you requested to the table were not successful because they would create duplicate values in the index, primary key, or relationship.");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(DbConstraintKind.Unique, info.ConstraintKind);
    }

    private sealed class PlainDbException : DbException
    {
        public PlainDbException(string message) : base(message)
        {
        }
    }

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
    public void AnalyzeException_UpdateableQueryMessage_ClassifiesAsReadOnlyViolation()
    {
        var ex = new PlainMessageDbException("Operation must use an updateable query.");
        var info = CreateDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ReadOnlyViolation, info.Category);
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
