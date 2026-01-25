using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.wrappers;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectAdditionalCoverageTests
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
    public void CreateDbParameter_StringValue_SetsSize()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var parameter = dialect.CreateDbParameter("p", DbType.String, "abc");
        Assert.Equal(3, parameter.Size);
    }

    [Fact]
    public void CreateDbParameter_DecimalValue_SetsPrecisionAndScale()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new TestableDialect(factory, NullLoggerFactory.Instance.CreateLogger<TestableDialect>());
        var parameter = dialect.CreateDbParameter("p", DbType.Decimal, 123.4500m);
        Assert.Equal(5, parameter.Precision);
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
            dialect.SupportsIntegrityConstraints,
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
            dialect.SupportsSavepoints
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