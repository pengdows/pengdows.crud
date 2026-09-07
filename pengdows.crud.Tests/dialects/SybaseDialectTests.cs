using System;
using System.Data;
using System.Reflection;
using AdoNetCore.AseClient;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class SybaseDialectTests
{
    private static SybaseDialect Dialect() =>
        new(new fakeDbFactory(SupportedDatabase.Sybase), NullLogger<SybaseDialect>.Instance);

    [Fact]
    public void CreateDialectForType_Sybase_ReturnsSybaseDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sybase);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Sybase,
            factory,
            NullLogger<SqlDialect>.Instance);

        Assert.IsType<SybaseDialect>(dialect);
        Assert.Equal(SupportedDatabase.Sybase, dialect.DatabaseType);
    }

    [Fact]
    public void ParameterMarker_IsAt()
        => Assert.Equal("@", Dialect().ParameterMarker);

    [Fact]
    public void SupportsSavepoints_IsTrue()
        => Assert.True(Dialect().SupportsSavepoints);

    [Fact]
    public void SavepointSql_UsesSaveTransaction()
        => Assert.Equal("SAVE TRANSACTION \"sp1\"", Dialect().GetSavepointSql("sp1"));

    [Fact]
    public void RollbackToSavepointSql_UsesRollbackTransaction()
        => Assert.Equal("ROLLBACK TRANSACTION \"sp1\"", Dialect().GetRollbackToSavepointSql("sp1"));

    [Fact]
    public void SupportsMerge_IsTrue()
        => Assert.True(Dialect().SupportsMerge);

    [Fact]
    public void RequiresMergeStatementTerminator_IsFalse()
        => Assert.False(Dialect().RequiresMergeStatementTerminator);

    [Fact]
    public void BuildBatchUpdateSql_NoVersionColumn_OmitsVersionClauses()
    {
        using var query = new SqlQueryBuilder();
        Dialect().BuildBatchUpdateSql(
            "\"t\"",
            new[] { "\"col\"" },
            new[] { "\"id\"" },
            1,
            query,
            (row, col) => 42);

        var sql = query.ToString();
        Assert.Contains("MERGE INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(';', sql);
    }

    [Fact]
    public void BuildBatchUpdateSql_NonOpaqueVersionColumn_AddsOnPredicateAndIncrementsInSet()
    {
        using var query = new SqlQueryBuilder();
        Dialect().BuildBatchUpdateSql(
            "\"t\"",
            new[] { "\"col\"" },
            new[] { "\"id\"" },
            1,
            query,
            (row, col) => 1,
            versionColumnName: "\"version\"",
            versionColumnIsOpaque: false);

        var sql = query.ToString();
        Assert.Contains("t.\"version\" = s.\"version\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"version\" = t.\"version\" + 1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(';', sql);
    }

    [Fact]
    public void BuildBatchUpdateSql_OpaqueVersionColumn_AddsOnPredicateButNoIncrement()
    {
        using var query = new SqlQueryBuilder();
        Dialect().BuildBatchUpdateSql(
            "\"t\"",
            new[] { "\"col\"" },
            new[] { "\"id\"" },
            1,
            query,
            (row, col) => 1,
            versionColumnName: "\"version\"",
            versionColumnIsOpaque: true);

        var sql = query.ToString();
        Assert.Contains("t.\"version\" = s.\"version\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("+ 1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportsInsertReturning_IsFalse()
        => Assert.False(Dialect().SupportsInsertReturning);

    [Fact]
    public void GeneratedKeyPlan_IsCompoundStatement()
        => Assert.Equal(GeneratedKeyPlan.CompoundStatement, Dialect().GetGeneratedKeyPlan());

    [Fact]
    public void CompoundInsertIdSuffix_UsesIdentityWithoutSemicolon()
        => Assert.Equal(" SELECT @@IDENTITY", Dialect().GetCompoundInsertIdSuffix());

    [Fact]
    public void LastInsertedIdQuery_SelectsIdentity()
        => Assert.Equal("SELECT @@IDENTITY", Dialect().GetLastInsertedIdQuery());

    [Fact]
    public void VersionQuery_SelectsAtAtVersion()
        => Assert.Equal("SELECT @@version", Dialect().GetVersionQuery());

    [Fact]
    public void ExtractProductNameFromVersion_RecognizesAseBanner()
    {
        const string banner =
            "Adaptive Server Enterprise/16.0 SP02 PL02/EBF 25320 SMP/P/x86_64/Enterprise Linux/ase160sp02plx/2492/64-bit/FBO/Sat Nov 21 04:05:39 2015";

        Assert.Equal("Sybase Adaptive Server Enterprise", Dialect().ExtractProductNameFromVersion(banner));
    }

    [Fact]
    public void SupportsOffsetFetch_IsFalse()
        => Assert.False(Dialect().SupportsOffsetFetch);

    [Fact]
    public void SupportsLimitOffset_IsFalse()
        => Assert.False(Dialect().SupportsLimitOffset);

    [Fact]
    public void AppendPaging_ThrowsNotSupported()
    {
        var dialect = Dialect();
        var ctx = new DatabaseContext("fake", new fakeDbFactory(SupportedDatabase.Sybase));
        using var sc = ctx.CreateSqlContainer();

        Assert.Throws<NotSupportedException>(() => dialect.AppendPaging(sc.Query, 0, 10));
    }

    [Fact]
    public void SupportsWindowFunctions_IsFalse()
        => Assert.False(Dialect().SupportsWindowFunctions);

    [Fact]
    public void SupportsCommonTableExpressions_IsFalse()
        => Assert.False(Dialect().SupportsCommonTableExpressions);

    [Fact]
    public void BuildUpsert_UsesMergeSql_WithNoTrailingSemicolon()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sybase);
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sybase", factory, typeMap);
        context.RegisterEntity<SybaseMergeEntity>();

        var helper = new TableGateway<SybaseMergeEntity, int>(context);
        var entity = new SybaseMergeEntity { Id = 1, Name = "sa", Counter = 5 };

        using var container = helper.BuildUpsert(entity);
        var sql = container.Query.ToString();

        Assert.Contains("MERGE INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ON DUPLICATE KEY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.False(sql.TrimEnd().EndsWith(";", StringComparison.Ordinal),
            "ASE rejects a trailing semicolon after MERGE.");
    }

    // -------------------------------------------------------------------------
    // Exception analysis — AseException does not derive from DbException, so these
    // exercise the SybaseDialect overrides of the Exception-typed entry points.
    // -------------------------------------------------------------------------

    private static AseException Ase(int messageNumber, string message)
    {
        var error = new AseError();
        SetProperty(error, nameof(AseError.MessageNumber), messageNumber);
        SetProperty(error, nameof(AseError.Message), message);
        return new AseException(new[] { error });
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        var setter = property!.GetSetMethod(nonPublic: true);
        setter!.Invoke(target, new[] { value });
    }

    [Fact]
    public void IsUniqueViolation_AseError2601_ReturnsTrue()
        => Assert.True(Dialect().IsUniqueViolation(Ase(2601, "duplicate key")));

    [Fact]
    public void IsUniqueViolation_AseError546_ReturnsFalse()
        => Assert.False(Dialect().IsUniqueViolation(Ase(546, "foreign key")));

    [Theory]
    [InlineData(2601)]
    [InlineData(546)]
    [InlineData(548)]
    [InlineData(233)]
    public void ClassifyException_KnownConstraintCodes_ReturnConstraintViolation(int number)
        => Assert.Equal(DbErrorCategory.ConstraintViolation, Dialect().ClassifyException(Ase(number, "boom")));

    [Fact]
    public void ClassifyException_1205_ReturnsDeadlock()
        => Assert.Equal(DbErrorCategory.Deadlock, Dialect().ClassifyException(Ase(1205, "deadlock victim")));

    [Fact]
    public void AnalyzeException_2601_ReturnsUniqueConstraintKind()
    {
        var info = Dialect().AnalyzeException(Ase(2601, "duplicate key"));

        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(DbConstraintKind.Unique, info.ConstraintKind);
        Assert.Equal(2601, info.ProviderErrorCode);
        Assert.False(info.IsTransient);
    }

    [Fact]
    public void AnalyzeException_546_ReturnsForeignKeyConstraintKind()
    {
        var info = Dialect().AnalyzeException(Ase(546, "fk violation"));
        Assert.Equal(DbConstraintKind.ForeignKey, info.ConstraintKind);
    }

    [Fact]
    public void AnalyzeException_548_ReturnsCheckConstraintKind()
    {
        var info = Dialect().AnalyzeException(Ase(548, "check violation"));
        Assert.Equal(DbConstraintKind.Check, info.ConstraintKind);
    }

    [Fact]
    public void AnalyzeException_233_ReturnsNotNullConstraintKind()
    {
        var info = Dialect().AnalyzeException(Ase(233, "not null violation"));
        Assert.Equal(DbConstraintKind.NotNull, info.ConstraintKind);
    }

    [Fact]
    public void AnalyzeException_1205_IsTransientAndRetryable()
    {
        var info = Dialect().AnalyzeException(Ase(1205, "deadlock victim"));
        Assert.Equal(DbErrorCategory.Deadlock, info.Category);
        Assert.True(info.IsTransient);
        Assert.True(info.IsRetryable);
    }

    [Fact]
    public void ClassifyException_NonAseException_FallsBackToBaseHeuristic()
    {
        var ex = new InvalidOperationException("some other failure");
        Assert.Equal(DbErrorCategory.Unknown, Dialect().ClassifyException(ex));
    }

    [Table("sybase_merge")]
    private class SybaseMergeEntity
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Column("counter", DbType.Int32)] public int Counter { get; set; }
    }
}
