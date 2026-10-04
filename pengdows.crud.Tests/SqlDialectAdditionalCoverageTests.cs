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

public class SqlDialectAdditionalCoverageTests
{

    [Fact]
    public void CreateDbParameter_DecimalValue_SetsPrecisionToAtLeast18AndExactScale()
    {
        // Precision is set to max(inferred, 18) so SqlClient 6.x accepts any value
        // for a standard DECIMAL(18,x) column.  Scale is the value's natural scale.
        // 123.4500m trims trailing fractional zeros → scale=2; inferred precision=5;
        // final Precision = max(5, 18) = 18.
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

        internal override Task<string> GetDatabaseVersionCoreAsync(ITrackedConnection connection, bool useAsync)
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