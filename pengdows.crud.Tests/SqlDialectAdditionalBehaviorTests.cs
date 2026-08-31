using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectAdditionalBehaviorTests
{
    [Fact]
    public void WrapObjectName_TrimsWhitespaceAndSkipsEmptySegments()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        var wrapped = context.WrapObjectName("  schema..table  ");
        Assert.Equal("\"schema\".\"table\"", wrapped);
    }

    [Fact]
    public void GetConnectionSessionSettings_ReadOnlyAppendsReadOnlySettings()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var settings = dialect.GetConnectionSessionSettings(context, true);
        Assert.Equal("SET BASE SETTINGS\nSET READONLY MODE", settings);
    }

    [Fact]
    public void GetReadOnlyConnectionString_AppendsReadOnlyParameter()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var readOnly = dialect.CallGetReadOnlyConnectionString("Data Source=test");
        Assert.Equal("Data Source=test;Mode=ReadOnly", readOnly);
    }

    [Fact]
    public void CreateDbParameter_InvalidName_ThrowsArgumentException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        Assert.Throws<ArgumentException>(() => dialect.CreateDbParameter("bad-name!", DbType.Int32, 1));
    }

    [Fact]
    public void AnalyzeException_MessageFallback_AllDigitSqlState_IsExtracted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var ex = new TestDbException("CLI0114E Datetime field overflow. SQLSTATE=22008");

        var info = dialect.AnalyzeException(ex);

        Assert.Equal("22008", info.SqlState);
    }

    [Fact]
    public void AnalyzeException_MessageFallback_AlphanumericSqlState_TrailingFormat_IsExtracted()
    {
        // Regression: the fallback regex previously required all-digit \d{5}, but real
        // SQLSTATEs are alphanumeric (e.g. "42S02" - table not found).
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var ex = new TestDbException("Some driver error. SQLSTATE=42S02");

        var info = dialect.AnalyzeException(ex);

        Assert.Equal("42S02", info.SqlState);
    }

    [Fact]
    public void AnalyzeException_MessageFallback_AlphanumericSqlState_LeadingErrorBracketFormat_IsExtracted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var ex = new TestDbException("ERROR [HY000] General driver error occurred");

        var info = dialect.AnalyzeException(ex);

        Assert.Equal("HY000", info.SqlState);
    }

    private sealed class TestDbException : DbException
    {
        public TestDbException(string message) : base(message)
        {
        }
    }

    [Fact]
    public void CreateDbParameter_StringValue_SetsSize()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var parameter = dialect.CreateDbParameter("p", DbType.String, "abc");
        Assert.Equal(3, parameter.Size);
    }

    [Fact]
    public void CreateDbParameter_DecimalValue_SetsPrecisionToAtLeast18AndExactScale()
    {
        // Precision is set to max(inferred, 18), the industry-convention DECIMAL(18,x) shape
        // (see SqlDialect.CreateDbParameter's comment for why — not a confirmed SqlClient
        // version-specific requirement, despite how this used to be worded here). Scale is the
        // value's natural scale. 123.4500m trims trailing fractional zeros → scale=2; inferred
        // precision=5; final Precision = max(5, 18) = 18.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var parameter = dialect.CreateDbParameter("p", DbType.Decimal, 123.4500m);
        Assert.Equal(18, parameter.Precision);
        Assert.Equal(2, parameter.Scale);
    }

    [Fact]
    public void InitializeUnknownProductInfo_SetsFallbackAndWarns()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        Assert.False(dialect.IsInitialized);
        dialect.InitializeUnknownProductInfo();
        Assert.True(dialect.IsInitialized);
        Assert.Equal("Unknown", dialect.ProductInfo.ProductName);
        Assert.Equal(SupportedDatabase.Unknown, dialect.ProductInfo.DatabaseType);
        Assert.Equal(SqlStandardLevel.Sql92, dialect.ProductInfo.StandardCompliance);
        Assert.Contains("SQL-92", dialect.GetCompatibilityWarning(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InferDatabaseTypeFromInfo_DetectsKnownProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var inferred = dialect.CallInferDatabaseType("CockroachDB", "CockroachDB 23.1");
        Assert.Equal(SupportedDatabase.CockroachDb, inferred);
    }

    [Fact]
    public void FeatureProperties_AreCallable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var features = new[]
        {
            dialect.SupportsJoins,
            dialect.SupportsOuterJoins,
            dialect.SupportsSubqueries,
            dialect.SupportsUnion,
            dialect.SupportsUserDefinedTypes,
            dialect.SupportsArrayTypes,
            dialect.SupportsRegularExpressions,
            dialect.SupportsMerge,
            dialect.SupportsXmlTypes,
            dialect.SupportsWindowFunctions,
            dialect.SupportsCommonTableExpressions,
            dialect.SupportsInsteadOfTriggers,
            dialect.SupportsTruncateTable,
            dialect.SupportsTemporalData,
            dialect.SupportsEnhancedWindowFunctions,
            dialect.SupportsJsonTypes,
            dialect.SupportsRowPatternMatching,
            dialect.SupportsMultidimensionalArrays,
            dialect.SupportsPropertyGraphQueries,
            dialect.SupportsSqlJsonConstructors,
            dialect.SupportsJsonTable,
            dialect.SupportsMergeReturning,
            dialect.SupportsInsertOnConflict,
            dialect.SupportsOnDuplicateKey,
            dialect.SupportsSavepoints,
            dialect.SupportsDropTableIfExists
        };

        Assert.Equal(26, features.Length);
        Assert.Contains("SAVEPOINT", dialect.GetSavepointSql("sp"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ROLLBACK", dialect.GetRollbackToSavepointSql("sp"), StringComparison.OrdinalIgnoreCase);
        var numeric = new[]
        {
            dialect.MaxParameterLimit,
            dialect.MaxOutputParameters,
            dialect.ParameterNameMaxLength
        };
        Assert.All(numeric, value => Assert.True(value >= 0));
        Assert.False(dialect.RequiresStoredProcParameterNameMatch);
        Assert.False(dialect.SupportsNamespaces);
    }

    [Fact]
    public void ParameterHelpers_AreInvokable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());

        Assert.Equal(":", dialect.ParameterMarker);
        Assert.Equal(":", dialect.ParameterMarkerAt(10));
        Assert.Equal(":p", dialect.RenderJsonArgument(":p", null!));
        Assert.Throws<ArgumentNullException>(() => dialect.TryMarkJsonParameter(null!, null!));
    }

    [Fact]
    public void WrapObjectName_WhitespaceOnlyReturnsEmpty()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        Assert.Equal(string.Empty, context.WrapObjectName("     "));
    }

    [Fact]
    public void BuildWrappedObjectName_TrimsAndQuotesSegments()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var result = dialect.CallBuildWrappedObjectName("  schema.table  ");

        Assert.Equal("\"schema\".\"table\"", result);
        Assert.Equal(string.Empty, dialect.CallBuildWrappedObjectName("   "));
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_FallsBackWhenVersionFails()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new ThrowingDialect(factory, NullLoggerFactory.Instance.CreateLogger<ThrowingDialect>());
        using var tracked = CreateTrackedConnection(factory,
            DataSourceInformation.BuildEmptySchema("Test", "1.0", "?", "?", 64, "\\w+", "\\w+", true));

        var info = await dialect.CallDetectDatabaseInfoAsync(tracked);

        Assert.Equal("Unknown", info.ProductName);
        Assert.Equal(SqlStandardLevel.Sql92, info.StandardCompliance);
    }

    [Fact]
    public async Task GetProductNameAsync_UsesSchemaName()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var schema = DataSourceInformation.BuildEmptySchema("Driver", "1.0", "?", "%", 64, "\\w+", "\\w+", true);
        using var tracked = CreateTrackedConnection(factory, schema);

        var result = await dialect.GetProductNameAsync(tracked);

        Assert.Equal("Driver", result);
    }

    [Fact]
    public void ParseVersion_HandlesCommonFormats()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());

        Assert.Equal(3, dialect.ParseVersion("PostgreSQL 3.2.1")?.Major);
        Assert.Null(dialect.ParseVersion("invalid version"));
    }

    // Found via a live Oracle 23ai/26ai container during a FEAT-008 driver-matrix investigation:
    // System.Version.TryParse supports at most 4 dot-separated components (Major.Minor.Build.
    // Revision), but Oracle's version scheme is a 5-part "23.26.2.0.0". The main regex match
    // ("23.26.2.0.0") failed Version.TryParse outright, silently falling through to the
    // single-digit fallback regex -- which matched "26" from the unrelated "26ai" marketing
    // branding token earlier in the banner ("Oracle AI Database 26ai Free Release
    // 23.26.2.0.0 - Develop, Learn, and Run for Free"), not the real major version (23). This
    // produced a *wrong*, not just missing, ParsedVersion.Major whenever a product's
    // marketing/branding number diverges from its actual release-train number -- every
    // ParsedVersion-gated capability flag (e.g. OracleDialect.SupportsJsonTypes) would have
    // silently evaluated against the wrong major version.
    [Fact]
    public void ParseVersion_FivePartVersionString_TruncatesToFourComponentsInsteadOfFailing()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());

        var parsed = dialect.ParseVersion(
            "Oracle AI Database 26ai Free Release 23.26.2.0.0 - Develop, Learn, and Run for Free");

        Assert.NotNull(parsed);
        Assert.Equal(23, parsed!.Major);
        Assert.Equal(26, parsed.Minor);
        Assert.Equal(2, parsed.Build);
        Assert.Equal(0, parsed.Revision);
    }

    [Fact]
    public void ParseVersion_FivePartVersionString_NoMisleadingBrandingPrefix_StillParsesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());

        // Same 5-part shape, no earlier stray digit in the string to confirm the fix is the
        // truncate-to-4-components path, not an accidental fallback-regex coincidence.
        var parsed = dialect.ParseVersion("Oracle Database Release 21.3.0.0.0");

        Assert.NotNull(parsed);
        Assert.Equal(21, parsed!.Major);
        Assert.Equal(3, parsed.Minor);
        Assert.Equal(0, parsed.Build);
        Assert.Equal(0, parsed.Revision);
    }

    [Fact]
    public void GenerateRandomName_TruncatesToMax()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());

        var name = dialect.GenerateRandomName(10, 3);
        Assert.True(name.Length <= 3);
    }

    [Fact]
    public void DetermineStandardCompliance_UsesMapping()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var mapping = new Dictionary<int, SqlStandardLevel> { [2] = SqlStandardLevel.Sql2011 };
        var dialect = new MappingDialect(factory, NullLoggerFactory.Instance.CreateLogger<MappingDialect>(), mapping);

        var level = dialect.DetermineStandardCompliance(new Version(3, 0, 0));
        Assert.Equal(SqlStandardLevel.Sql2011, level);
        Assert.Equal(SqlStandardLevel.Sql92, dialect.DetermineStandardCompliance(null));
    }

    private static FakeTrackedConnection CreateTrackedConnection(
        fakeDbFactory factory,
        DataTable schema,
        Dictionary<string, object>? scalars = null)
    {
        var connection = (DbConnection)factory.CreateConnection();
        return new FakeTrackedConnection(connection, schema, scalars ?? new Dictionary<string, object>());
    }

    private class TestableDialect : SqlDialect
    {
        public TestableDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger)
        {
        }

        public string CallBuildWrappedObjectName(string identifier)
        {
            var method = typeof(SqlDialect).GetMethod(
                             "BuildWrappedObjectName",
                             BindingFlags.NonPublic | BindingFlags.Instance)
                         ?? throw new InvalidOperationException("Missing BuildWrappedObjectName method");

            return (string)method.Invoke(this, new object[] { identifier })!;
        }

        public Task<IDatabaseProductInfo> CallDetectDatabaseInfoAsync(ITrackedConnection connection)
        {
            return DetectDatabaseInfoAsync(connection);
        }

        public string CallGetReadOnlyConnectionString(string connectionString)
        {
            return GetReadOnlyConnectionString(connectionString);
        }

        public SupportedDatabase CallInferDatabaseType(string productName, string version)
        {
            return InferDatabaseTypeFromInfo(productName, version);
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;

        public override string ParameterMarker => ":";

        public override int ParameterNameMaxLength => 64;

        public override int MaxParameterLimit => 256;

        public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

        public override string GetReadOnlySessionSettings()
        {
            return "SET READONLY MODE";
        }

        public override string GetBaseSessionSettings()
        {
            return "SET BASE SETTINGS";
        }

        public override string GetReadOnlyConnectionParameter()
        {
            return "Mode=ReadOnly";
        }

        public override string GetVersionQuery()
        {
            return "SELECT version()";
        }
    }

    private sealed class ThrowingDialect : TestableDialect
    {
        public ThrowingDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger)
        {
        }

        public override Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class MappingDialect : TestableDialect
    {
        private readonly Dictionary<int, SqlStandardLevel> _mapping;

        public MappingDialect(DbProviderFactory factory, ILogger logger, Dictionary<int, SqlStandardLevel> mapping)
            : base(factory, logger)
        {
            _mapping = mapping;
        }

        public override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
        {
            return _mapping;
        }
    }
}