#region

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.isolation;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Locks down <see cref="InformixDialect"/>'s capability overrides (CLAUDE.md's "Adding a New
/// Database" checklist item 7). Every fact asserted here mirrors the sourced/verified claims
/// already documented on the dialect members themselves (IBM Informix docs and community
/// references cited inline there); see InformixDialect.cs's file-level AI SUMMARY for what has
/// and has not been confirmed against a live server. Isolation-level data lives in
/// IsolationResolver.cs's central switch on this branch, not a dialect-owned override - see
/// IsolationResolver_* tests below instead of a dialect-level GetSupportedIsolationLevels call.
/// </summary>
public class InformixDialectTests
{
    private static InformixDialect CreateDialect()
    {
        return new InformixDialect(new fakeDbFactory(SupportedDatabase.Informix), NullLogger.Instance);
    }

    private static IDatabaseContext CreateContext()
    {
        return new DatabaseContext("Data Source=test;EmulatedProduct=Informix", new fakeDbFactory(SupportedDatabase.Informix));
    }

    [Fact]
    public void ReadOnlyPoolDiscriminatorSettingName_IsLeaveTrailingSpaces()
    {
        // CONFIRMED LIVE (2026-09-19, real icr.io/informix/informix-developer-database
        // container) — a follow-up to the earlier pass this session that investigated Optofc/
        // DelimIdent and correctly declined both (neither was confirmed behaviorally inert).
        // Systematically re-tested every property on the real live-connected
        // IfxConnectionStringBuilder for one whose CHOSEN VALUE equals the driver's own already-
        // implicit compiled-in default (the strongest inertness guarantee established this
        // session, matching InterBaseDialect's "fetch size=200" fix) — LeaveTrailingSpaces
        // defaults to False, so setting it explicitly to False is guaranteed to change nothing.
        // Confirmed live: a connection with "LeaveTrailingSpaces=False" explicitly set opens
        // and queries successfully. Deliberately NOT MaxPoolSize=100 (also default-matching and
        // also confirmed to connect) — ApplyPoolDiscriminator skips setting the discriminator key
        // if the caller's own connection string already contains it (see
        // ConnectionPoolingConfiguration.ApplyPoolDiscriminator's "don't override" guard), and
        // MaxPoolSize is exactly the kind of property a real caller is likely to have already
        // configured themselves — silently defeating pool separation in precisely the case where
        // a caller has customized their own pooling. LeaveTrailingSpaces is obscure enough that
        // no real caller is expected to ever set it themselves.
        Assert.Equal("LeaveTrailingSpaces", CreateDialect().ReadOnlyPoolDiscriminatorSettingName);
    }

    [Fact]
    public void ReadOnlyPoolDiscriminatorSettingValue_MatchesLeaveTrailingSpacesOwnDefault()
    {
        Assert.Equal("False", CreateDialect().ReadOnlyPoolDiscriminatorSettingValue);
    }

    [Fact]
    public void DatabaseType_IsInformix()
    {
        Assert.Equal(SupportedDatabase.Informix, CreateDialect().DatabaseType);
    }

    [Fact]
    public void TryEnterReadOnlyTransaction_ExecutesSetTransactionReadOnly()
    {
        // CONFIRMED LIVE (2026-09-18) against a real icr.io/informix/informix-developer-database
        // container: "SET TRANSACTION READ ONLY" inside an active transaction genuinely enforces
        // read-only — a subsequent write fails with "Invalid operation for a READ-ONLY
        // transaction.", confirmed both via raw BEGIN WORK/SQL text and via a real ADO.NET
        // conn.BeginTransaction(). Same mechanism OracleDialect uses.
        var dialect = CreateDialect();
        var container = new Mock<ISqlContainer>(MockBehavior.Strict);
        container.Setup(c => c.ExecuteNonQueryAsync(CommandType.Text)).ReturnsAsync(0).Verifiable();
        container.Setup(c => c.Dispose()).Verifiable();

        var transaction = new Mock<ITransactionContext>(MockBehavior.Strict);
        transaction
            .Setup(t => t.CreateSqlContainer("SET TRANSACTION READ ONLY"))
            .Returns(container.Object)
            .Verifiable();

        dialect.TryEnterReadOnlyTransaction(transaction.Object);

        container.Verify();
        transaction.Verify();
    }

    [Fact]
    public async Task TryEnterReadOnlyTransactionAsync_ExecutesSetTransactionReadOnly()
    {
        var dialect = CreateDialect();
        var container = new Mock<ISqlContainer>(MockBehavior.Strict);
        container
            .Setup(c => c.ExecuteNonQueryAsync(CommandType.Text, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .Verifiable();
        container.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        var transaction = new Mock<ITransactionContext>(MockBehavior.Strict);
        transaction
            .Setup(t => t.CreateSqlContainer("SET TRANSACTION READ ONLY"))
            .Returns(container.Object)
            .Verifiable();

        await dialect.TryEnterReadOnlyTransactionAsync(transaction.Object, CancellationToken.None);

        container.Verify();
        transaction.Verify();
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
    public void SupportsNamespaces_IsTrue()
    {
        Assert.True(CreateDialect().SupportsNamespaces);
    }

    // CONFIRMED live (Informix 15.0.1.0.3): MERGE INTO t USING (SELECT ... FROM sysmaster:sysdual) s
    // ON t.k = s.k WHEN MATCHED THEN UPDATE SET ... WHEN NOT MATCHED THEN INSERT ... works.
    [Fact]
    public void SupportsMerge_IsTrue()
    {
        Assert.True(CreateDialect().SupportsMerge);
    }

    // Every mapping below was round-tripped live through a MERGE (insert, then update) against
    // Informix 15.0.1.0.3 with Informix.Net.Core. Booleans are bound as Int16 on this positional
    // dialect, so they are cast to SMALLINT (the column type the testbed uses for booleans).
    [Theory]
    [InlineData(DbType.Int64, "BIGINT")]
    [InlineData(DbType.Int32, "INT")]
    [InlineData(DbType.Int16, "SMALLINT")]
    [InlineData(DbType.Boolean, "SMALLINT")]
    [InlineData(DbType.String, "LVARCHAR(32739)")]
    [InlineData(DbType.AnsiString, "LVARCHAR(32739)")]
    [InlineData(DbType.Guid, "LVARCHAR(32739)")]
    [InlineData(DbType.Decimal, "DECIMAL(32)")]
    [InlineData(DbType.Double, "FLOAT")]
    [InlineData(DbType.Single, "SMALLFLOAT")]
    [InlineData(DbType.DateTime, "DATETIME YEAR TO FRACTION(5)")]
    [InlineData(DbType.DateTimeOffset, "DATETIME YEAR TO FRACTION(5)")]
    [InlineData(DbType.Date, "DATE")]
    public void GetMergeSourceCastType_MapsVerifiedTypes(DbType dbType, string expected)
    {
        Assert.Equal(expected, InformixDialect.GetMergeSourceCastType(dbType));
    }

    [Fact]
    public void GetMergeSourceCastType_UnverifiedType_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => InformixDialect.GetMergeSourceCastType(DbType.Binary));
    }

    [Fact]
    public void UpsertIncomingColumn_ReferencesMergeSourceAlias()
    {
        Assert.Equal("s.\"name\"", CreateDialect().UpsertIncomingColumn("name"));
    }

    [Fact]
    public void BuildUpsert_UsesMergeFromSysdual()
    {
        var gateway = new TableGateway<UpsertEntity, int>(CreateContext());

        var sql = gateway.BuildUpsert(new UpsertEntity { Id = 1, Name = "a" }).Query.ToString();

        Assert.StartsWith("MERGE INTO ", sql, StringComparison.Ordinal);
        Assert.Contains(" FROM sysmaster:sysdual) s ON ", sql, StringComparison.Ordinal);
        Assert.Contains(" WHEN MATCHED THEN UPDATE SET ", sql, StringComparison.Ordinal);
        Assert.Contains(" WHEN NOT MATCHED THEN INSERT (", sql, StringComparison.Ordinal);
    }

    // CONFIRMED live: Informix MERGE has no conditional matched clause at all - both
    // "WHEN MATCHED AND cond THEN UPDATE" and "UPDATE SET ... WHERE cond" are syntax errors - so the
    // optimistic-concurrency version check cannot be expressed. Putting it in ON would send a
    // stale row down WHEN NOT MATCHED (primary-key violation), and dropping it would silently
    // overwrite, so upsert of a [Version] entity is refused.
    [Fact]
    public void BuildUpsert_VersionedEntity_ThrowsNotSupported()
    {
        var gateway = new TableGateway<VersionedUpsertEntity, int>(CreateContext());

        var ex = Assert.Throws<NotSupportedException>(() =>
            gateway.BuildUpsert(new VersionedUpsertEntity { Id = 1, Name = "a", Version = 1 }));
        Assert.Contains("Version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryKeyGateway_BuildUpsert_VersionedEntity_ThrowsNotSupported()
    {
        var gateway = new PrimaryKeyTableGateway<VersionedPkEntity>(CreateContext());

        Assert.Throws<NotSupportedException>(() =>
            gateway.BuildUpsert(new VersionedPkEntity { Code = "a", Name = "n", Version = 1 }));
    }

    [pengdows.crud.attributes.Table("upsert_entity")]
    private class UpsertEntity
    {
        [pengdows.crud.attributes.Id]
        [pengdows.crud.attributes.Column("id", DbType.Int32)]
        public int Id { get; set; }

        [pengdows.crud.attributes.Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [pengdows.crud.attributes.Table("versioned_upsert_entity")]
    private class VersionedUpsertEntity
    {
        [pengdows.crud.attributes.Id]
        [pengdows.crud.attributes.Column("id", DbType.Int32)]
        public int Id { get; set; }

        [pengdows.crud.attributes.Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [pengdows.crud.attributes.Version]
        [pengdows.crud.attributes.Column("version", DbType.Int32)]
        public int Version { get; set; }
    }

    [pengdows.crud.attributes.Table("versioned_pk_entity")]
    private class VersionedPkEntity
    {
        [pengdows.crud.attributes.PrimaryKey(1)]
        [pengdows.crud.attributes.Column("code", DbType.String)]
        public string Code { get; set; } = string.Empty;

        [pengdows.crud.attributes.Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [pengdows.crud.attributes.Version]
        [pengdows.crud.attributes.Column("version", DbType.Int32)]
        public int Version { get; set; }
    }

    // CONFIRMED live (Informix 15.0.1.0.3): both OFFSET/FETCH and LIMIT m OFFSET n are syntax
    // errors, so neither syntax flag is claimed; paging uses the native SKIP/FIRST instead.
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

    // CONFIRMED live (testbed): Informix.Net.Core has no DbType.DateTimeOffset mapping at all
    // ("No mapping exists from DbType DateTimeOffset to a known IfxType", thrown by
    // IfxParameter.set_DbType), and Informix has no offset-aware temporal type. The dialect stores
    // the UTC instant as a plain DateTime, matching Db2/Sybase/Firebird/InterBase.
    [Fact]
    public void CreateDbParameter_NonNullDateTimeOffset_CoercesToUnspecifiedUtcDateTime()
    {
        var dto = new DateTimeOffset(2026, 2, 21, 12, 34, 56, TimeSpan.FromHours(-5));
        var param = CreateDialect().CreateDbParameter<DateTimeOffset?>("p", DbType.DateTimeOffset, dto);

        Assert.Equal(DbType.DateTime, param.DbType);
        var stored = Assert.IsType<DateTime>(param.Value);
        Assert.Equal(DateTimeKind.Unspecified, stored.Kind);
        Assert.Equal(dto.UtcDateTime, DateTime.SpecifyKind(stored, DateTimeKind.Utc));
    }

    [Fact]
    public void CreateDbParameter_NullDateTimeOffset_CoercesToDbTypeDateTimeWithDbNull()
    {
        var param = CreateDialect().CreateDbParameter<DateTimeOffset?>("p", DbType.DateTimeOffset, null);

        Assert.Equal(DbType.DateTime, param.DbType);
        Assert.Equal(DBNull.Value, param.Value);
    }

    // CONFIRMED live: the server stores trailing blanks (OCTET_LENGTH counts them, and
    // v || '|' returns them), but Informix.Net.Core trims them from every VARCHAR/NVARCHAR/LVARCHAR
    // value it returns, whatever LeaveTrailingSpaces is set to. IBM APAR IC63704: the .NET provider
    // has no option to disable this.
    [Fact]
    public void PreservesTrailingWhitespace_IsFalse()
    {
        Assert.False(CreateDialect().PreservesTrailingWhitespace);
    }

    [Fact]
    public void SupportsPaging_IsTrue()
    {
        Assert.True(CreateDialect().SupportsPaging);
    }

    // CONFIRMED live: "SELECT SKIP n FIRST m ..." (and "SELECT FIRST m ...") must follow SELECT
    // directly, ahead of the select list (and ahead of DISTINCT).
    [Theory]
    [InlineData("SELECT \"a\".\"id\" FROM \"t\" \"a\" ORDER BY \"id\"", 5, 5,
        "SELECT SKIP 5 FIRST 5 \"a\".\"id\" FROM \"t\" \"a\" ORDER BY \"id\"")]
    [InlineData("SELECT \"a\".\"id\" FROM \"t\" \"a\" ORDER BY \"id\"", 0, 5,
        "SELECT FIRST 5 \"a\".\"id\" FROM \"t\" \"a\" ORDER BY \"id\"")]
    [InlineData("\n  select DISTINCT \"v\" FROM \"t\"", 2, 3, "\n  select SKIP 2 FIRST 3 DISTINCT \"v\" FROM \"t\"")]
    public void AppendPaging_InsertsSkipFirstAfterSelect(string sql, int offset, int limit, string expected)
    {
        var context = CreateContext();
        var sc = context.CreateSqlContainer(sql);

        CreateDialect().AppendPaging(sc.Query, offset, limit);

        Assert.Equal(expected, sc.Query.ToString());
    }

    [Fact]
    public void AppendPaging_QueryNotStartingWithSelect_ThrowsNotSupported()
    {
        var sc = CreateContext().CreateSqlContainer("WITH x AS (SELECT 1 FROM t) SELECT * FROM x");

        Assert.Throws<NotSupportedException>(() => CreateDialect().AppendPaging(sc.Query, 0, 5));
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, 0)]
    public void AppendPaging_InvalidArguments_Throw(int offset, int limit)
    {
        var sc = CreateContext().CreateSqlContainer("SELECT 1 FROM t");

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDialect().AppendPaging(sc.Query, offset, limit));
    }

    // CONFIRMED live (Informix 15.0.1.0.3, logged database): SAVEPOINT "sp1",
    // ROLLBACK TO SAVEPOINT "sp1" and RELEASE SAVEPOINT "sp1" all work, i.e. the base SQL.
    [Fact]
    public void Savepoints_AreFullySupported()
    {
        var dialect = CreateDialect();

        Assert.True(dialect.SupportsSavepoints);
        Assert.Equal(
            SavepointCapabilities.Create | SavepointCapabilities.Rollback | SavepointCapabilities.Release,
            dialect.SavepointCapabilities);
        Assert.Equal("SAVEPOINT \"sp1\"", dialect.GetSavepointSql("sp1"));
        Assert.Equal("ROLLBACK TO SAVEPOINT \"sp1\"", dialect.GetRollbackToSavepointSql("sp1"));
        Assert.Equal("RELEASE SAVEPOINT \"sp1\"", dialect.GetReleaseSavepointSql("sp1"));
    }

    [Fact]
    public void SupportsBatchInsert_IsFalse()
    {
        Assert.False(CreateDialect().SupportsBatchInsert);
    }

    [Fact]
    public void ProcWrappingStyle_IsInformix()
    {
        Assert.Equal(ProcWrappingStyle.Informix, CreateDialect().ProcWrappingStyle);
    }

    [Fact]
    public void GuidFormat_IsString()
    {
        var d = CreateDialect();
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var param = d.CreateDbParameter("p", DbType.Guid, guid);
        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", param.Value?.ToString());
    }

    [Fact]
    public void GetVersionQuery_QueriesDbinfoVersionFull()
    {
        Assert.Contains("DBINFO", CreateDialect().GetVersionQuery(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsolationResolver_SupportsAllFourAnsiLevels()
    {
        var resolver = new IsolationResolver(SupportedDatabase.Informix, false, false);
        var levels = resolver.GetSupportedLevels();
        Assert.Contains(IsolationLevel.ReadUncommitted, levels);
        Assert.Contains(IsolationLevel.ReadCommitted, levels);
        Assert.Contains(IsolationLevel.RepeatableRead, levels);
        Assert.Contains(IsolationLevel.Serializable, levels);
    }

    [Fact]
    public void IsolationResolver_MapsSafeAndStrictAndFast()
    {
        var resolver = new IsolationResolver(SupportedDatabase.Informix, false, false);
        Assert.Equal(IsolationLevel.ReadCommitted, resolver.Resolve(IsolationProfile.SafeNonBlockingReads));
        Assert.Equal(IsolationLevel.Serializable, resolver.Resolve(IsolationProfile.StrictConsistency));
        Assert.Equal(IsolationLevel.ReadUncommitted, resolver.Resolve(IsolationProfile.FastWithRisks));
    }

    // ── Exception classification ────────────────────────────────────────────
    // TryGetProviderErrorCode's reflection probe checks "Number" -> "SqliteErrorCode" ->
    // "NativeError" in that order; NumberedDbException below exercises that same "Number" path
    // the shared helper already uses for other drivers with no distinct property name.

    [Fact]
    public void IsUniqueViolation_SqlState23000_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23000", "duplicate value for a column with unique constraint");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_ErrorCodeMinus268_LoggedDatabase_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(-268, "duplicate value for a column with unique constraint");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_ErrorCodeMinus239_UnloggedDatabase_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(-239, "duplicate value for a column with unique constraint");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_ErrorCodeMinus691_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(-691, "Missing key for referential constraint");
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_ErrorCodeMinus692_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(-692, "Key value for constraint is still being referenced");
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_ErrorCodeMinus391_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(-391, "Column has a NOT NULL constraint");
        Assert.True(ctx.GetDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_ErrorCodeMinus530_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(-530, "Check constraint violated");
        Assert.True(ctx.GetDialect().IsCheckConstraintViolation(ex));
    }

    [Fact]
    public void AnalyzeException_Deadlock143_ClassifiesAsDeadlock()
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(-143, "ISAM error: deadlock detected");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.Deadlock, info.Category);
    }

    [Fact]
    public void AnalyzeException_PhysicalOrderReadConflict244_ClassifiesAsSerializationFailure()
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(-244, "Could not do a physical-order read to fetch next row");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.SerializationFailure, info.Category);
    }

    // -908/-27001/-27002 are documented connection/communication failure codes, but
    // TryClassifyProviderException deliberately assigns them DbErrorCategory.Unknown rather than
    // a specific category (see InformixDialect.cs's own comment) — connection-failure handling
    // for Informix instead happens one layer up, in InformixExceptionTranslator's own SQLSTATE
    // "08" prefix check (see InformixTranslatorTests.cs), not via this classifier.
    [Theory]
    [InlineData(-908)]
    [InlineData(-27001)]
    [InlineData(-27002)]
    public void AnalyzeException_CommunicationFailureCodes_ClassifyAsUnknown(int code)
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(code, "communication failure");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.Unknown, info.Category);
    }

    [Fact]
    public void AnalyzeException_SqlState23Prefix_WithoutSpecificErrorCode_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23xyz", "some other constraint-class condition");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
    }

    [Fact]
    public void AnalyzeException_UnrecognizedError_ClassifiesAsUnknown()
    {
        using var ctx = CreateContext();
        var ex = new NumberedDbException(99999, "some unrecognized Informix failure");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.Unknown, info.Category);
    }

    private sealed class NumberedDbException : DbException
    {
        public int Number { get; }

        public NumberedDbException(int number, string message) : base(message)
        {
            Number = number;
        }
    }

    private sealed class SqlStateDbException : DbException
    {
        public SqlStateDbException(string sqlState, string message) : base(message)
        {
            SqlState = sqlState;
        }

        public override string? SqlState { get; }
    }
}
