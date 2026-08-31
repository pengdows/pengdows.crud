using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerParameterNormalizationAdditionalTests
{
    [Fact]
    public void SetParameterValue_UsesAlternatePrefixWhenNamedParameters()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var container = ctx.CreateSqlContainer("SELECT 1");

        var param = container.AddParameterWithValue("p0", DbType.Int32, 1);
        container.SetParameterValue("w0", 5);

        Assert.Equal(5, param.Value);
    }

    [Fact]
    public void SetParameterValue_WithMarkerPrefix_ThrowsWhenDialectIllogical()
    {
        using var ctx = CreateContextWithDialect(new PositionalDialect(new fakeDbFactory(SupportedDatabase.Sqlite)));
        var container = ctx.CreateSqlContainer("SELECT 1");

        container.AddParameterWithValue("p0", DbType.Int32, 1);

        Assert.Throws<KeyNotFoundException>(() => container.SetParameterValue("@p0", 5));
    }

    [Fact]
    public void SetParameterValue_ShortName_ThrowsWhenNotFound()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var container = ctx.CreateSqlContainer("SELECT 1");

        Assert.Throws<KeyNotFoundException>(() => container.SetParameterValue("p", 1));
    }

    [Fact]
    public void SetParameterValue_ArrayValue_SetsDbTypeObject()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var container = ctx.CreateSqlContainer("SELECT 1");

        var param = container.AddParameterWithValue("p0", DbType.Int32, 1);
        container.SetParameterValue("p0", new[] { 1, 2 });

        Assert.Equal(DbType.Object, param.DbType);
    }

    [Fact]
    public void GenerateParameterName_TruncatesWhenMaxLengthIsOne()
    {
        var dialect = new TinyNameDialect(new fakeDbFactory(SupportedDatabase.Sqlite));
        using var ctx = CreateContextWithDialect(dialect);
        var container = SqlContainer.CreateForDialect(ctx, dialect);

        var param = container.AddParameterWithValue(DbType.Int32, 1);

        Assert.Equal("p", param.ParameterName);
    }

    [Fact]
    public void GenerateParameterName_CreatesSequentialNames()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var container = ctx.CreateSqlContainer("SELECT 1");

        var param1 = container.AddParameterWithValue(DbType.Int32, 1);
        var param2 = container.AddParameterWithValue(DbType.Int32, 2);
        var param3 = container.AddParameterWithValue(DbType.Int32, 3);

        // Should have unique sequential names
        Assert.NotEqual(param1.ParameterName, param2.ParameterName);
        Assert.NotEqual(param2.ParameterName, param3.ParameterName);
        Assert.NotEqual(param1.ParameterName, param3.ParameterName);

        // All should start with 'p'
        Assert.StartsWith("p", param1.ParameterName);
        Assert.StartsWith("p", param2.ParameterName);
        Assert.StartsWith("p", param3.ParameterName);
    }

    [Fact]
    public void GenerateParameterName_WithPadding_PadsCorrectly()
    {
        var dialect = new MediumNameDialect(new fakeDbFactory(SupportedDatabase.Sqlite));
        using var ctx = CreateContextWithDialect(dialect);
        var container = SqlContainer.CreateForDialect(ctx, dialect);

        var param = container.AddParameterWithValue(DbType.Int32, 1);

        // Max length is 5, prefix is "p" (1 char), so suffix should be 4 chars
        Assert.Equal(5, param.ParameterName.Length);
        Assert.StartsWith("p", param.ParameterName);
    }

    [Fact]
    public void GenerateParameterName_WithLongSuffix_TruncatesCorrectly()
    {
        var dialect = new ShortNameDialect(new fakeDbFactory(SupportedDatabase.Sqlite));
        using var ctx = CreateContextWithDialect(dialect);
        var container = SqlContainer.CreateForDialect(ctx, dialect);

        // Generate a few parameters to test truncation
        var param1 = container.AddParameterWithValue(DbType.Int32, 1);
        var param2 = container.AddParameterWithValue(DbType.Int32, 2);

        // Max length is 3, so total name should be 3 chars
        Assert.Equal(3, param1.ParameterName.Length);
        Assert.Equal(3, param2.ParameterName.Length);
        Assert.StartsWith("p", param1.ParameterName);
        Assert.StartsWith("p", param2.ParameterName);
        // Names should be different
        Assert.NotEqual(param1.ParameterName, param2.ParameterName);
    }

    [Fact]
    public void SetParameterValue_EmptyName_ThrowsKeyNotFound()
    {
        // Covers StripParameterPrefix("") (Length == 0 -> string.Empty) and
        // TryBuildAlternateParameterName's normalized.Length < 2 short-circuit, reached via
        // SetParameterValue's alternate-prefix lookup fallback when the direct lookup misses.
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var container = ctx.CreateSqlContainer("SELECT 1");
        container.AddParameterWithValue("p0", DbType.Int32, 1);

        Assert.Throws<KeyNotFoundException>(() => container.SetParameterValue(string.Empty, 5));
    }

    [Fact]
    public void NormalizeParameterName_ViaReflection_EmptyString_ReturnsUnchanged()
    {
        // NormalizeParameterName's own null/empty guard is unreachable through its one real call
        // site (SqlContainer.AddParameter already checks IsNullOrEmpty before calling it) - this
        // exercises the guard directly, following the same private-method-via-reflection pattern
        // already used by GenerateUniqueParameterName_RetriesWhenFirstCandidateAlreadyUsed below.
        var normalize = typeof(SqlContainer).GetMethod("NormalizeParameterName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(normalize);

        var result = normalize!.Invoke(null, new object?[] { string.Empty });

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void MakeDuplicateParameterName_ViaReflection_EmptyBaseName_FallsBackToP()
    {
        // The real call site (RenderParamsDeduplicating) can never pass an empty base name - the
        // {P}NAME scanner requires at least one identifier char - so the "trimmed becomes empty"
        // branch is only reachable by invoking the private method directly.
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var container = ctx.CreateSqlContainer("SELECT 1");

        var method = typeof(SqlContainer).GetMethod("MakeDuplicateParameterName",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var result = (string)method!.Invoke(container, new object[] { string.Empty, 2, new HashSet<string>() })!;

        Assert.StartsWith("p", result);
    }

    [Fact]
    public void RenderParams_OracleDuplicatePlaceholder_TinyMaxLength_FallsBackToGenerateUniqueName()
    {
        // Oracle is the one dialect combo (SupportsNamedParameters && !SupportsRepeatedNamedParameters)
        // that goes through RenderParamsDeduplicating/MakeDuplicateParameterName at all. With a
        // tiny ParameterNameMaxLength, even the shortest numeric suffix ("_2") can't fit, forcing
        // MakeDuplicateParameterName's "suffix too long" branch to fall back to
        // GenerateUniqueParameterName instead of a trimmed+suffix candidate.
        var dialect = new TinyMaxLengthOracleDialect(new fakeDbFactory(SupportedDatabase.Oracle));
        using var ctx = CreateContextWithDialect(dialect);
        var container = SqlContainer.CreateForDialect(ctx, dialect);
        container.AddParameterWithValue("x", DbType.Int32, 1);
        container.Query.Append("SELECT 1 FROM dual WHERE a = {P}x OR b = {P}x");

        using var tracked = new TrackedConnection(new fakeDbConnection());
        var ex = Record.Exception(() => container.CreateCommand(tracked));

        Assert.Null(ex);
    }

    [Fact]
    public void RenderParams_CalledTwiceOnSameContainer_ClearsPreviouslyRenderedMap()
    {
        // First render populates _renderedParameterMap (duplicate {P}x needs renaming to x_2);
        // a second render on the same container instance (e.g. re-executing a reused container)
        // must clear the stale map before re-populating it.
        using var ctx = CreateContext(SupportedDatabase.Oracle);
        var container = ctx.CreateSqlContainer("SELECT 1 FROM dual WHERE a = {P}x OR b = {P}x");
        container.AddParameterWithValue("x", DbType.Int32, 1);

        using var tracked1 = new TrackedConnection(new fakeDbConnection());
        using var tracked2 = new TrackedConnection(new fakeDbConnection());
        var firstRender = container.CreateCommand(tracked1);
        var secondRender = container.CreateCommand(tracked2);

        Assert.Equal(firstRender.CommandText, secondRender.CommandText);
    }

    [Fact]
    public void WrapForStoredProc_PositionalProvider_BuildsQuestionMarkPlaceholders()
    {
        // BuildProcedureArguments' positional path (non-named-parameter provider) - distinct from
        // the named-parameter path exercised by every other WrapForStoredProc test.
        var dialect = new PositionalDialect(new fakeDbFactory(SupportedDatabase.Sqlite));
        using var ctx = CreateContextWithDialect(dialect);
        // DatabaseContext caches ProcWrappingStyle from DataSourceInfo at construction time -
        // CreateContextWithDialect's reflection swap replaces the dialect/DataSourceInfo fields
        // after that point, so the cached style must be set explicitly too.
        ctx.ProcWrappingStyle = ProcWrappingStyle.Call;
        var container = SqlContainer.CreateForDialect(ctx, dialect, "my_proc");
        container.AddParameterWithValue("a", DbType.Int32, 1);
        container.AddParameterWithValue("b", DbType.Int32, 2);

        var wrapped = container.WrapForStoredProc(ExecutionType.Write);

        Assert.Contains("?, ?", wrapped);
    }

    [Fact]
    public void WrapForStoredProc_PositionalProvider_NoParameters_ReturnsEmptyArguments()
    {
        // BuildProcedureArguments' positional path has its own _parameters.Count == 0 guard,
        // separate from (and after) the named-parameter path's identical check.
        var dialect = new PositionalDialect(new fakeDbFactory(SupportedDatabase.Sqlite));
        using var ctx = CreateContextWithDialect(dialect);
        ctx.ProcWrappingStyle = ProcWrappingStyle.Call;
        var container = SqlContainer.CreateForDialect(ctx, dialect, "my_proc");

        var wrapped = container.WrapForStoredProc(ExecutionType.Write);

        Assert.Equal("CALL \"my_proc\"()", wrapped);
    }

    [Fact]
    public void GenerateParameterName_HexCounterExactFitAndTruncation_BothOccur()
    {
        // With ParameterNameMaxLength=2 ("p" prefix + 1 hex digit available): ids 0-15 fit exactly
        // in one hex digit; id 16 (hex "10") needs 2 digits and must be truncated from the left.
        // Invoked via reflection (rather than AddParameterWithValue) so the truncation wraparound
        // at id 16 producing the same rendered name as id 0 doesn't collide in the real parameter
        // dictionary - that collision is real but an artifact of this deliberately tiny test-only
        // max length, not something to change production behavior for.
        var dialect = new VeryShortNameDialect(new fakeDbFactory(SupportedDatabase.Sqlite));
        using var ctx = CreateContextWithDialect(dialect);
        var container = SqlContainer.CreateForDialect(ctx, dialect);
        var generateName = typeof(SqlContainer).GetMethod("GenerateParameterName",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(generateName);

        string? last = null;
        for (var i = 0; i <= 16; i++)
        {
            last = Assert.IsType<string>(generateName!.Invoke(container, null));
            Assert.Equal(2, last.Length);
            Assert.StartsWith("p", last);
        }

        // id 16 (hex "10") truncated from the left to 1 char is "0" - same rendered name as id 0.
        Assert.Equal("p0", last);
    }

    [Fact]
    public void GenerateUniqueParameterName_RetriesWhenFirstCandidateAlreadyUsed()
    {
        var dialect = new ShortNameDialect(new fakeDbFactory(SupportedDatabase.Sqlite));
        using var ctx = CreateContextWithDialect(dialect);
        var container = SqlContainer.CreateForDialect(ctx, dialect);

        var generateName = typeof(SqlContainer).GetMethod("GenerateParameterName",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var generateUnique = typeof(SqlContainer).GetMethod("GenerateUniqueParameterName",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var nextIdField = typeof(SqlContainer).GetField("_nextParameterId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(generateName);
        Assert.NotNull(generateUnique);
        Assert.NotNull(nextIdField);

        var firstCandidate = Assert.IsType<string>(generateName!.Invoke(container, null));
        nextIdField!.SetValue(container, 0);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { firstCandidate };
        var unique = Assert.IsType<string>(generateUnique!.Invoke(container, new object[] { used }));

        Assert.NotEqual(firstCandidate, unique);
    }

    private static DatabaseContext CreateContext(SupportedDatabase database)
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={database}",
            DbMode = DbMode.SingleConnection
        };
        return new DatabaseContext(cfg, new fakeDbFactory(database), NullLoggerFactory.Instance);
    }

    private static DatabaseContext CreateContextWithDialect(SqlDialect dialect)
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };
        var context = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance);

        var dialectField = typeof(DatabaseContext).GetField("_dialect", BindingFlags.Instance | BindingFlags.NonPublic);
        var dataSourceInfoField =
            typeof(DatabaseContext).GetField("_dataSourceInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        dialectField!.SetValue(context, dialect);
        dataSourceInfoField!.SetValue(context, new DataSourceInformation(dialect));

        return context;
    }

    private static ISqlDialect GetDialect(DatabaseContext context)
    {
        var dialectField = typeof(DatabaseContext).GetField("_dialect", BindingFlags.Instance | BindingFlags.NonPublic);
        return (ISqlDialect)dialectField!.GetValue(context)!;
    }

    private sealed class TinyNameDialect : SqliteDialect
    {
        internal TinyNameDialect(DbProviderFactory factory)
            : base(factory, NullLogger<SqlDialect>.Instance)
        {
        }

        public override int ParameterNameMaxLength => 1;
    }

    private sealed class MediumNameDialect : SqliteDialect
    {
        internal MediumNameDialect(DbProviderFactory factory)
            : base(factory, NullLogger<SqlDialect>.Instance)
        {
        }

        public override int ParameterNameMaxLength => 5;
    }

    private sealed class ShortNameDialect : SqliteDialect
    {
        internal ShortNameDialect(DbProviderFactory factory)
            : base(factory, NullLogger<SqlDialect>.Instance)
        {
        }

        public override int ParameterNameMaxLength => 3;
    }

    private sealed class PositionalDialect : SqliteDialect
    {
        internal PositionalDialect(DbProviderFactory factory)
            : base(factory, NullLogger<SqlDialect>.Instance)
        {
        }

        public override bool SupportsNamedParameters => false;
        public override string ParameterMarker => "?";
        public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;
    }

    private sealed class VeryShortNameDialect : SqliteDialect
    {
        internal VeryShortNameDialect(DbProviderFactory factory)
            : base(factory, NullLogger<SqlDialect>.Instance)
        {
        }

        public override int ParameterNameMaxLength => 2;
    }

    private sealed class TinyMaxLengthOracleDialect : OracleDialect
    {
        internal TinyMaxLengthOracleDialect(DbProviderFactory factory)
            : base(factory, NullLogger<SqlDialect>.Instance)
        {
        }

        public override int ParameterNameMaxLength => 2;
    }
}
