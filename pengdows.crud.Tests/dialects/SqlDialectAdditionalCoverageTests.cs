using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.isolation;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.dialects;

// Targeted coverage for branches in SqlDialect.cs that existing SqlDialect*Tests.cs files don't
// reach: base-class defaults only exercised when a dialect doesn't override them, an env-var-gated
// diagnostic branch, and a couple of SQLite IsUniqueViolation sub-conditions.
public class SqlDialectAdditionalCoverageTests
{
    private static SqlDialect CreateBaseDialect(SupportedDatabase db = SupportedDatabase.SqlServer)
    {
        var factory = new fakeDbFactory(db);
        return (SqlDialect)SqlDialectFactory.CreateDialectForType(db, factory, NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
    }

    [Fact]
    public void ProductInfo_BeforeDetection_Throws()
    {
        var dialect = CreateBaseDialect();
        Assert.False(dialect.IsInitialized);
        Assert.Throws<InvalidOperationException>(() => dialect.ProductInfo);
    }

    [Fact]
    public void ConfigureSetValuedParameter_BaseDefault_IsNoOp()
    {
        // SqlServerDialect does not override ConfigureSetValuedParameter; the base no-op body
        // (SqlDialect.cs) should run without throwing.
        var dialect = CreateBaseDialect(SupportedDatabase.SqlServer);
        var param = dialect.CreateDbParameter("p", DbType.Object, (object?)null);
        dialect.ConfigureSetValuedParameter(param, new object[] { 1, 2, 3 });
    }

    [Fact]
    public void WrapSimpleName_NameContainingSuffixChar_EscapesViaSlowPath()
    {
        var dialect = CreateBaseDialect();
        // Base QuotePrefix/QuoteSuffix are both '"'. A name containing an embedded '"' forces the
        // AppendWithEscaping slow path instead of the fast concat path.
        var wrapped = dialect.WrapSimpleName("foo\"bar");
        Assert.Equal("\"foo\"\"bar\"", wrapped);
    }

    [Fact]
    public void WrapObjectName_AlreadyWrappedSegment_IsLeftAlone()
    {
        var dialect = CreateBaseDialect();
        // Each dot-separated segment already wrapped in the dialect's own quote chars must be
        // passed through unescaped rather than double-wrapped.
        var wrapped = dialect.WrapObjectName("\"schema\".\"table\"");
        Assert.Equal("\"schema\".\"table\"", wrapped);
    }

    [Fact]
    public void WrapObjectName_LongIdentifier_ExceedsDefaultStackCapacity()
    {
        var dialect = CreateBaseDialect();
        var longName = new string('a', 512);
        var wrapped = dialect.WrapObjectName(longName);
        Assert.Equal($"\"{longName}\"", wrapped);
    }

    [Fact]
    public void WrapObjectName_NoConfiguredSeparator_TreatsWholeNameAsOneSegment()
    {
        // Every shipped dialect uses "." as CompositeIdentifierSeparator; this double confirms the
        // hasSeparator=false branch (an empty separator) is handled correctly for a future dialect
        // that might configure one.
        var dialect = new NoSeparatorTestDialect();
        var wrapped = dialect.WrapObjectName("schema.table");
        Assert.Equal("\"schema.table\"", wrapped);
    }

    [Fact]
    public void GetIsolationGuarantees_SnapshotLevel_ReturnsNonBlockingReadGuarantees()
    {
        // SqlServerDialect does not override GetIsolationGuarantees; only PostgreSqlDialect does.
        var dialect = CreateBaseDialect(SupportedDatabase.SqlServer);
        var guarantees = dialect.GetIsolationGuarantees(IsolationLevel.Snapshot);
        Assert.True(guarantees.HasFlag(IsolationGuarantees.NonBlockingReads));
    }

    [Fact]
    public void GetIsolationGuarantees_UnmappedLevel_FallsBackToNone()
    {
        var dialect = CreateBaseDialect(SupportedDatabase.SqlServer);
        var guarantees = dialect.GetIsolationGuarantees((IsolationLevel)(-1));
        Assert.Equal(IsolationGuarantees.None, guarantees);
    }

    [Fact]
    public void CreateDbParameter_WithParamTimingEnvVarEnabled_LogsTimingWithoutThrowing()
    {
        var previous = Environment.GetEnvironmentVariable("PENGDOWS_PARAM_TIMING");
        try
        {
            Environment.SetEnvironmentVariable("PENGDOWS_PARAM_TIMING", "1");
            var dialect = CreateBaseDialect();
            var param = dialect.CreateDbParameter("p", DbType.Int32, 42);
            Assert.NotNull(param);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PENGDOWS_PARAM_TIMING", previous);
        }
    }

    [Theory]
    [InlineData(19, "UNIQUE constraint failed", true)]
    [InlineData(0, "UNIQUE constraint failed", true)]
    [InlineData(0, "PRIMARY KEY constraint failed", true)]
    [InlineData(0, "some other failure", false)]
    public void Sqlite_IsUniqueViolation_CoversMessageAndErrorCodeCombinations(int errorCode, string message, bool expected)
    {
        var dialect = CreateBaseDialect(SupportedDatabase.Sqlite);
        Assert.Equal(expected, dialect.IsUniqueViolation(new SqliteHResultDbException(errorCode, message)));
    }

    [Fact]
    public void IsUniqueViolation_UnrecognizedDatabaseType_FallsBackToMessageHeuristic()
    {
        var dialect = CreateBaseDialect(SupportedDatabase.Unknown);
        Assert.True(dialect.IsUniqueViolation(new SqliteHResultDbException(0, "duplicate key value violates unique constraint")));
        Assert.False(dialect.IsUniqueViolation(new SqliteHResultDbException(0, "some unrelated failure")));
    }

    private sealed class SqliteHResultDbException : DbException
    {
        public SqliteHResultDbException(int errorCode, string message) : base(message)
        {
            HResult = errorCode;
        }
    }

    private sealed class NoSeparatorTestDialect : SqlDialect
    {
        public NoSeparatorTestDialect() : base(new fakeDbFactory(SupportedDatabase.SqlServer), NullLoggerFactory.Instance.CreateLogger<SqlDialect>())
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.SqlServer;
        public override string ParameterMarker => "@";
        public override string CompositeIdentifierSeparator => string.Empty;
    }
}
