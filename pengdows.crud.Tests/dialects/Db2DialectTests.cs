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

public class Db2DialectTests
{
    private static Db2Dialect CreateDialect()
    {
        return new Db2Dialect(new fakeDbFactory(SupportedDatabase.Db2), NullLogger<Db2Dialect>.Instance);
    }

    private static IDatabaseContext CreateContext()
    {
        return new DatabaseContext("Data Source=test;EmulatedProduct=Db2", new fakeDbFactory(SupportedDatabase.Db2));
    }

    [Fact]
    public void DatabaseType_IsDb2()
    {
        Assert.Equal(SupportedDatabase.Db2, CreateDialect().DatabaseType);
    }

    [Fact]
    public void ParameterMarker_IsAtSign()
    {
        Assert.Equal("@", CreateDialect().ParameterMarker);
    }

    [Fact]
    public void SupportsNamedParameters_IsTrue()
    {
        Assert.True(CreateDialect().SupportsNamedParameters);
    }

    [Fact]
    public void ProcWrappingStyle_IsCall()
    {
        // Db2 stored procedures are invoked via SQL-standard CALL syntax, same as MySQL/MariaDB —
        // CallProcWrappingStrategy's own doc comment already names Db2 as an intended consumer.
        Assert.Equal(ProcWrappingStyle.Call, CreateDialect().ProcWrappingStyle);
    }

    [Fact]
    public void WrapForStoredProc_UsesCallSyntax()
    {
        using var ctx = CreateContext();
        using var sc = (SqlContainer)ctx.CreateSqlContainer("SP_TEST");
        sc.AddParameterWithValue("p0", DbType.Int32, 1);

        var wrapped = sc.WrapForStoredProc(ExecutionType.Write);

        Assert.Equal("CALL \"SP_TEST\"(@p0)", wrapped);
    }

    [Fact]
    public void GetBaseSessionSettings_ResetsIsolationAndTemporalRegisters()
    {
        // Db2's CURRENT ISOLATION, CURRENT TEMPORAL SYSTEM_TIME, and CURRENT TEMPORAL
        // BUSINESS_TIME special registers are session-level state that survives transaction
        // rollback (their SET statements are not transaction-controlled) and can silently change
        // the meaning of subsequent SQL for whichever caller borrows a pooled connection next:
        // a non-null CURRENT ISOLATION overrides the package/dynamic-SQL isolation level, and a
        // non-null temporal register implicitly rewrites SELECT/UPDATE/DELETE against temporal
        // tables to an as-of-time view. Verified live against Db2 LUW 11.5.8.0 that all three
        // (and the batched multi-statement form) execute successfully via ExecuteNonQuery.
        var sql = CreateDialect().GetBaseSessionSettings();

        Assert.Contains("SET CURRENT ISOLATION RESET", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET CURRENT TEMPORAL SYSTEM_TIME = NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET CURRENT TEMPORAL BUSINESS_TIME = NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuotePrefix_And_Suffix_AreAnsiDoubleQuotes()
    {
        var d = CreateDialect();
        Assert.Equal("\"", d.QuotePrefix);
        Assert.Equal("\"", d.QuoteSuffix);
    }

    [Fact]
    public void SupportsMerge_IsTrue()
    {
        Assert.True(CreateDialect().SupportsMerge);
    }

    [Fact]
    public void SupportsSavepoints_IsTrue()
    {
        Assert.True(CreateDialect().SupportsSavepoints);
    }

    [Fact]
    public void GetSavepointSql_IncludesOnRollbackRetainCursors()
    {
        // Db2 LUW rejects a bare "SAVEPOINT name" with SQL0104N — confirmed against a live
        // ibmcom/db2 container during Phase 2 testbed validation.
        var sql = CreateDialect().GetSavepointSql("sp1");
        Assert.Equal("SAVEPOINT \"sp1\" ON ROLLBACK RETAIN CURSORS", sql);
    }

    [Fact]
    public void SupportsIdentityColumns_IsTrue()
    {
        Assert.True(CreateDialect().SupportsIdentityColumns);
    }

    [Fact]
    public void SupportsInsertReturning_IsTrue()
    {
        Assert.True(CreateDialect().SupportsInsertReturning);
    }

    [Fact]
    public void WrapsInsertStatementForReturning_IsTrue()
    {
        Assert.True(CreateDialect().WrapsInsertStatementForReturning);
    }

    [Fact]
    public void RenderInsertReturningPrefix_ProducesSelectFromFinalTable()
    {
        var d = CreateDialect();
        var prefix = d.RenderInsertReturningPrefix("\"Id\"");
        Assert.Equal("SELECT \"Id\" FROM FINAL TABLE (", prefix);
    }

    [Fact]
    public void GetGeneratedKeyPlan_IsReturning()
    {
        Assert.Equal(GeneratedKeyPlan.Returning, CreateDialect().GetGeneratedKeyPlan());
    }

    [Fact]
    public void AppendPaging_UsesOffsetFetchSyntax()
    {
        var d = CreateDialect();
        var query = new SqlQueryBuilder();
        d.AppendPaging(query, 10, 5);
        var sql = query.ToString();
        Assert.Contains("OFFSET 10 ROWS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FETCH NEXT 5 ROWS ONLY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpsertIncomingColumn_ReferencesSourceAlias()
    {
        var d = CreateDialect();
        var col = d.UpsertIncomingColumn("Name");
        Assert.Equal("\"s\".\"Name\"", col);
    }

    [Fact]
    public void CreateDbParameter_Guid_IsSerializedAsString()
    {
        var d = CreateDialect();
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var param = d.CreateDbParameter("p", System.Data.DbType.Guid, guid);
        Assert.Equal(System.Data.DbType.String, param.DbType);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", param.Value?.ToString());
    }

    // ── Upsert (MERGE) SQL generation via TableGateway ──────────────────────

    [Fact]
    public void BuildUpsert_UsesMerge_ForDb2()
    {
        var context = CreateContext();
        var helper = new TableGateway<TestEntity, int>(context);
        var entity = new TestEntity { Id = 1, Name = "foo" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.Contains("MERGE INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USING (VALUES (", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN MATCHED", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN NOT MATCHED", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── FINAL TABLE-wrapped insert-with-returning SQL generation ────────────

    [Fact]
    public void BuildCreateWithReturning_WrapsInsertInSelectFromFinalTable()
    {
        var context = CreateContext();
        var helper = new TableGateway<TestEntity, int>(context, new AuditValueResolver());
        var entity = new TestEntity { Name = "foo" };
        var sc = helper.BuildCreateWithReturning(entity, true);
        var sql = sc.Query.ToString();

        Assert.StartsWith("SELECT \"Id\" FROM FINAL TABLE (INSERT INTO", sql, StringComparison.Ordinal);
        Assert.EndsWith(")", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("{prefix}", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("{returning}", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("{output}", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCreate_FastPath_HasNoLeftoverPlaceholders()
    {
        // The cached fast-path template (BuildCreate, no returning) must have every
        // placeholder — including the new {prefix} token — stripped to empty.
        var context = CreateContext();
        var helper = new TableGateway<TestEntity, int>(context, new AuditValueResolver());
        var entity = new TestEntity { Id = 1, Name = "foo" };
        var sc = helper.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.DoesNotContain("{prefix}", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("{returning}", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("{output}", sql, StringComparison.Ordinal);
        Assert.StartsWith("INSERT INTO", sql, StringComparison.Ordinal);
    }

    // ── Exception classification ────────────────────────────────────────────

    [Fact]
    public void IsUniqueViolation_Db2_SqlState23505_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23505", "Unique constraint violation");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_Db2_SqlState23503_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23503", "Foreign key violation");
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_Db2_SqlState23504_ReturnsTrue()
    {
        // Regression: confirmed against a live ibmcom/db2 container — deleting a parent row
        // blocked by a RESTRICT foreign key reports SQLSTATE 23504, NOT 23503 (which is only
        // used for insert/update FK violations).
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23504", "Parent row cannot be deleted");
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_Db2_SqlState23502_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23502", "Not null violation");
        Assert.True(ctx.GetDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_Db2_SqlState23513_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23513", "Check constraint violation");
        Assert.True(ctx.GetDialect().IsCheckConstraintViolation(ex));
    }

    [Fact]
    public void AnalyzeException_Db2_SqlState23xxx_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23505", "Unique constraint violation");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
    }

    // ── Regression: IBM.Data.Db2's DB2Exception exposes "SQLState" (all-caps SQL), not the
    // idiomatic "SqlState" — a case-sensitive reflection lookup silently misses it. Confirmed
    // against a live ibmcom/db2 container during Phase 2 testbed validation: a real duplicate-key
    // insert produced a generic DatabaseOperationException instead of
    // UniqueConstraintViolationException until this was fixed. ──

    [Fact]
    public void IsUniqueViolation_Db2_AllCapsSQLStateProperty_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new AllCapsSqlStateDbException("23505", "IBM-style exception");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_Db2_SqlStateOnlyInMessageText_ReturnsTrue()
    {
        // No structured SqlState/SQLState property at all — only embedded in the message,
        // exactly like IBM.Data.Db2's DB2Exception.Message format.
        using var ctx = CreateContext();
        var ex = new PlainMessageDbException(
            "ERROR [23505] [IBM][DB2/LINUXX8664] SQL0803N  duplicate key.  SQLSTATE=23505");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_Db2_SqlStateOnlyInMessageText_LeadingBracketFormOnly_ReturnsTrue()
    {
        // Server-side errors use ONLY the leading "ERROR [nnnnn]" form — no trailing
        // "SQLSTATE=nnnnn" fragment, unlike client-side CLI driver errors. Confirmed against a
        // live ibmcom/db2 container: a real NOT NULL violation's message is exactly this shape.
        using var ctx = CreateContext();
        var ex = new PlainMessageDbException(
            "ERROR [23502] [IBM][DB2/LINUXX8664] SQL0407N  Assignment of a NULL value to a " +
            "NOT NULL column \"TBSPACEID=2, TABLEID=4, COLNO=1\" is not allowed.");
        Assert.True(ctx.GetDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_Db2_SqlStateOnlyInMessageText_TrailingEqualsFormOnly_ReturnsTrue()
    {
        // Client-side CLI driver errors use ONLY the trailing "SQLSTATE=nnnnn" form — this
        // message deliberately has no leading "ERROR [nnnnn]" bracket, proving the trailing-form
        // regex branch works standalone rather than only when both forms co-occur.
        using var ctx = CreateContext();
        var ex = new PlainMessageDbException(
            "CLI0125E  Some wrapping driver message with no bracket form. SQLSTATE=23503");
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    private sealed class SqlStateDbException : DbException
    {
        public new string SqlState { get; }

        public SqlStateDbException(string sqlState, string message) : base(message)
        {
            SqlState = sqlState;
        }
    }

    private sealed class AllCapsSqlStateDbException : DbException
    {
        public string SQLState { get; }

        public AllCapsSqlStateDbException(string sqlState, string message) : base(message)
        {
            SQLState = sqlState;
        }
    }

    private sealed class PlainMessageDbException : DbException
    {
        public PlainMessageDbException(string message) : base(message)
        {
        }
    }
}
