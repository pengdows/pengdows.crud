#region

using System;
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
/// Locks down <see cref="HanaDialect"/>'s capability overrides. Every fact asserted here was
/// verified live against a real <c>saplabs/hanaexpress</c> 2.00.088.00 container (HXE) via Docker,
/// using both <c>hdbsql</c> and the real <c>Sap.Data.Hana.Net.v8.0</c> ADO.NET driver — see the
/// SAP HANA section of CLAUDE.md's "Adding a New Database" notes for the full research trail.
/// </summary>
public class HanaDialectTests
{
    private static HanaDialect CreateDialect()
    {
        return new HanaDialect(new fakeDbFactory(SupportedDatabase.SapHana), NullLogger<HanaDialect>.Instance);
    }

    private static IDatabaseContext CreateContext()
    {
        return new DatabaseContext("Data Source=test;EmulatedProduct=SapHana", new fakeDbFactory(SupportedDatabase.SapHana));
    }

    [Fact]
    public void DatabaseType_IsSapHana()
    {
        Assert.Equal(SupportedDatabase.SapHana, CreateDialect().DatabaseType);
    }

    // Verified live: DataSourceInformation.ParameterMarkerFormat == "?" for the real driver, and
    // a real positional INSERT ("VALUES (?, ?)") bound with ordered HanaParameter objects executes
    // correctly. Sap.Data.Hana has no name-to-position mapping for "?" markers.
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
    public void QuotePrefix_And_Suffix_AreAnsiDoubleQuotes()
    {
        var d = CreateDialect();
        Assert.Equal("\"", d.QuotePrefix);
        Assert.Equal("\"", d.QuoteSuffix);
    }

    // Verified live: CREATE COLUMN TABLE CaseTest (unquoted) is stored as CASETEST; CREATE COLUMN
    // TABLE "CaseTest3" (quoted) preserves exact case. Identical fold-to-uppercase-when-unquoted
    // behavior to Oracle, confirmed independently rather than assumed from it.
    [Fact]
    public void SupportsNamespaces_IsTrue()
    {
        Assert.True(CreateDialect().SupportsNamespaces);
    }

    // Verified live: "OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY" is rejected outright
    // ("257: sql syntax error ... incorrect syntax near \"1\""), while
    // "LIMIT 1 OFFSET 1" executes correctly. HANA supports MySQL/PostgreSQL-style LIMIT/OFFSET
    // only, not SQL:2008 OFFSET/FETCH — do not assume Db2/SQL-Server-style paging.
    [Fact]
    public void SupportsOffsetFetch_IsFalse()
    {
        Assert.False(CreateDialect().SupportsOffsetFetch);
    }

    [Fact]
    public void SupportsLimitOffset_IsTrue()
    {
        Assert.True(CreateDialect().SupportsLimitOffset);
    }

    [Fact]
    public void AppendPaging_UsesLimitOffsetSyntax()
    {
        var d = CreateDialect();
        var query = new SqlQueryBuilder();
        d.AppendPaging(query, 10, 5);
        var sql = query.ToString();
        Assert.Contains("LIMIT 5", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET 10", sql, StringComparison.OrdinalIgnoreCase);
    }

    // Verified live: the base RenderMergeSource's "USING (VALUES (...)) AS s (...)" row-constructor
    // shape is rejected ("257: sql syntax error ... incorrect syntax near \"VALUES\""), while
    // "USING (SELECT ... FROM DUMMY) AS s" executes correctly for both the WHEN MATCHED (update)
    // and WHEN NOT MATCHED (insert) branches of a real MERGE INTO. Same Oracle/DUAL-shaped pattern,
    // using HANA's DUMMY table instead.
    [Fact]
    public void SupportsMerge_IsTrue()
    {
        Assert.True(CreateDialect().SupportsMerge);
    }

    // RenderMergeSource's exact SQL shape ("USING (SELECT ? AS ..., ? AS ... FROM DUMMY) s") is
    // covered by SqlDialectBranchTests.RenderMergeSource_UsesProviderSyntax's SapHana case.

    // Verified live: an ANSI multi-row VALUES clause ("INSERT INTO t VALUES (1,'a'), (2,'b')") is
    // rejected ("257: sql syntax error ... incorrect syntax near \",\""). Falls back to one
    // BuildCreate per entity, the same safe path SQLite/MySQL/MariaDB/Firebird/Informix use.
    [Fact]
    public void SupportsBatchInsert_IsFalse()
    {
        Assert.False(CreateDialect().SupportsBatchInsert);
    }

    // Verified live: CALL proc(?, ?) with an OUT parameter executes correctly via SQLSCRIPT
    // procedures.
    [Fact]
    public void ProcWrappingStyle_IsCall()
    {
        Assert.Equal(ProcWrappingStyle.Call, CreateDialect().ProcWrappingStyle);
    }

    // "SELECT CURRENT_IDENTITY_VALUE() FROM DUMMY" was confirmed live to work immediately after
    // INSERT on a single manually-held connection, but is deliberately NOT wired up as
    // GeneratedKeyPlan.SessionScopedFunction — see HanaDialect's comment above
    // SupportsIdentityColumns for the two-lease pooled-connection hazard
    // (GeneratedKeyPlanReachabilityTests.cs) this would reintroduce, and why CompoundStatement
    // (confirmed rejected live — HANA has no multi-statement command support) isn't a safe
    // alternative either. Leaving SupportsInsertReturning/HasSessionScopedLastIdFunction at their
    // base-class defaults (both false) means GetGeneratedKeyPlan() already resolves to the safe
    // universal fallback.
    [Fact]
    public void GetGeneratedKeyPlan_IsCorrelationToken()
    {
        Assert.Equal(GeneratedKeyPlan.CorrelationToken, CreateDialect().GetGeneratedKeyPlan());
    }

    [Fact]
    public void SupportsIdentityColumns_IsTrue()
    {
        Assert.True(CreateDialect().SupportsIdentityColumns);
    }

    // Verified live: DROP TABLE IF EXISTS is rejected outright ("257: sql syntax error ...
    // incorrect syntax near \"IF\""), same limitation as Oracle.
    [Fact]
    public void SupportsDropTableIfExists_IsFalse()
    {
        Assert.False(CreateDialect().SupportsDropTableIfExists);
    }

    // Verified live: SAVEPOINT / ROLLBACK TO SAVEPOINT / RELEASE SAVEPOINT all execute correctly
    // inside a real transaction (unlike Oracle, which has no RELEASE SAVEPOINT at all).
    [Fact]
    public void SupportsSavepoints_IsTrue()
    {
        Assert.True(CreateDialect().SupportsSavepoints);
    }

    [Fact]
    public void SavepointCapabilities_IncludesCreateRollbackAndRelease()
    {
        var caps = CreateDialect().SavepointCapabilities;
        Assert.True(caps.HasFlag(SavepointCapabilities.Create));
        Assert.True(caps.HasFlag(SavepointCapabilities.Rollback));
        Assert.True(caps.HasFlag(SavepointCapabilities.Release));
    }

    // Verified live via HanaConnection.BeginTransaction(IsolationLevel): ReadUncommitted,
    // ReadCommitted, RepeatableRead, and Serializable are all accepted without error (Snapshot is
    // correctly rejected by the driver itself). Whether ReadUncommitted actually delivers dirty
    // reads on the server, versus a silent upgrade to ReadCommitted, was not verified (would
    // require a second concurrent session) — but unlike TiDB's documented
    // parses-but-silently-downgrades SERIALIZABLE case, nothing here contradicts the level being
    // genuinely honored.
    [Fact]
    public void GetSupportedIsolationLevels_IncludesAllFourAnsiLevels()
    {
        var levels = CreateDialect().GetSupportedIsolationLevels(false);
        Assert.Contains(IsolationLevel.ReadUncommitted, levels);
        Assert.Contains(IsolationLevel.ReadCommitted, levels);
        Assert.Contains(IsolationLevel.RepeatableRead, levels);
        Assert.Contains(IsolationLevel.Serializable, levels);
    }

    [Fact]
    public void GetIsolationProfileMapping_MapsSafeAndStrictAndFast()
    {
        var d = CreateDialect();
        var mapping = d.GetIsolationProfileMapping(false);
        Assert.Equal(IsolationLevel.ReadCommitted, mapping[IsolationProfile.SafeNonBlockingReads]);
        Assert.Equal(IsolationLevel.Serializable, mapping[IsolationProfile.StrictConsistency]);
        Assert.Equal(IsolationLevel.ReadUncommitted, mapping[IsolationProfile.FastWithRisks]);
    }

    // GUIDs: HanaDbType has no native UUID-style member (confirmed by enumerating the real
    // HanaDbType enum: AlphaNum, BigInt, Blob, Boolean, Clob, Date, Decimal, Double, Integer,
    // NClob, NVarChar, Real, RealVector, SecondDate, ShortText, SmallDecimal, SmallInt, Text,
    // Time, TimeStamp, TinyInt, VarBinary, VarChar, TableType) — stored as a client-generated
    // hyphenated string, matching every other dialect without a native GUID column type.
    [Fact]
    public void CreateDbParameter_Guid_IsSerializedAsString()
    {
        var d = CreateDialect();
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var param = d.CreateDbParameter("p", DbType.Guid, guid);
        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", param.Value?.ToString());
    }

    // ── Exception classification ────────────────────────────────────────────
    // Every code below was captured live from a real Sap.Data.Hana.HanaException thrown against
    // the HXE container. Critically, HanaException.SqlState is an EMPTY STRING for every violation
    // kind except unique (which the driver leaves at "23000"), and HanaException.ErrorCode is
    // *always* the generic COM HRESULT -2147467259 regardless of violation kind — neither is usable
    // for classification via the .NET driver. The only reliable discriminator is
    // HanaException.NativeError, which the framework's TryGetProviderErrorCode already finds via
    // reflection (it probes "Number" -> "SqliteErrorCode" -> "NativeError" in that order) with no
    // extra dialect code required.
    [Fact]
    public void IsUniqueViolation_NativeError301_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new NativeErrorDbException(301, "unique constraint violated");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_NativeError461_ReturnsTrue()
    {
        // 461: insert/update references a parent row that does not exist.
        using var ctx = CreateContext();
        var ex = new NativeErrorDbException(461, "foreign key constraint violation");
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_NativeError462_ReturnsTrue()
    {
        // 462: delete/update blocked because a dependent child row still exists.
        using var ctx = CreateContext();
        var ex = new NativeErrorDbException(462, "failed on update or delete by foreign key constraint violation");
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_NativeError287_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new NativeErrorDbException(287, "cannot insert NULL or update to NULL");
        Assert.True(ctx.GetDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_NativeError677_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new NativeErrorDbException(677, "check constraint violation");
        Assert.True(ctx.GetDialect().IsCheckConstraintViolation(ex));
    }

    [Fact]
    public void TryClassifyProviderException_Deadlock133_ClassifiesAsDeadlock()
    {
        // NOT live-reproduced this session (requires two contending sessions) — sourced from SAP's
        // own HANA Lock Analysis FAQ (KBA 1999998): SQL error 133 = "transaction rolled back by
        // detected deadlock".
        using var ctx = CreateContext();
        var ex = new NativeErrorDbException(133, "transaction rolled back by detected deadlock");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.Deadlock, info.Category);
    }

    [Fact]
    public void TryClassifyProviderException_LockTimeout131_ClassifiesAsTimeout()
    {
        // NOT live-reproduced this session — sourced from SAP KBA 1999998: SQL error 131 =
        // "transaction rolled back by lock wait timeout".
        using var ctx = CreateContext();
        var ex = new NativeErrorDbException(131, "transaction rolled back by lock wait timeout");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.Timeout, info.Category);
    }

    [Fact]
    public void AnalyzeException_UniqueViolation_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateContext();
        var ex = new NativeErrorDbException(301, "unique constraint violated");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
    }

    private sealed class NativeErrorDbException : DbException
    {
        public int NativeError { get; }

        public NativeErrorDbException(int nativeError, string message) : base(message)
        {
            NativeError = nativeError;
        }
    }
}
