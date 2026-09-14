#region

using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Locks down <see cref="InformixDialect"/>'s capability overrides — the dedicated test file
/// InformixDialect.cs's own class remarks flagged as not yet written (CLAUDE.md's "Adding a New
/// Database" checklist item 7). Every fact asserted here mirrors the sourced/verified claims
/// already documented on the dialect members themselves (IBM Informix docs and community
/// references cited inline there); see InformixDialect.cs's file-level AI SUMMARY for what has
/// and has not been confirmed against a live server.
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
    public void DatabaseType_IsInformix()
    {
        Assert.Equal(SupportedDatabase.Informix, CreateDialect().DatabaseType);
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

    [Fact]
    public void SupportsMerge_IsFalse()
    {
        Assert.False(CreateDialect().SupportsMerge);
    }

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

    [Fact]
    public void SupportsBatchInsert_IsFalse()
    {
        Assert.False(CreateDialect().SupportsBatchInsert);
    }

    [Fact]
    public void ProcWrappingStyle_IsNone()
    {
        Assert.Equal(ProcWrappingStyle.None, CreateDialect().ProcWrappingStyle);
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
        var mapping = CreateDialect().GetIsolationProfileMapping(false);
        Assert.Equal(IsolationLevel.ReadCommitted, mapping[IsolationProfile.SafeNonBlockingReads]);
        Assert.Equal(IsolationLevel.Serializable, mapping[IsolationProfile.StrictConsistency]);
        Assert.Equal(IsolationLevel.ReadUncommitted, mapping[IsolationProfile.FastWithRisks]);
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
