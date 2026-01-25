using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class SqlDialectAdvancedTests
{
    private sealed class TestDialect : SqlDialect
    {
        public TestDialect(DbProviderFactory factory, ILogger logger)
            : base(factory, logger)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;

        public override string GetReadOnlyConnectionParameter()
        {
            return "ApplicationIntent=ReadOnly";
        }
    }

    private static TestDialect CreateDialect(int maxNameLength = 18)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = NullLogger.Instance;
        var dialect = new TestDialect(factory, logger);
        return dialect;
    }

    [Fact]
    public void WrapObjectName_IgnoresWhitespaceAndQuotesSegments()
    {
        var dialect = CreateDialect();
        var actual = dialect.WrapObjectName("  schema .  table  ");
        Assert.Equal("\"schema\".\"table\"", actual);
    }

    [Fact]
    public void MakeParameterName_StripsMarkersAndPrefixes()
    {
        var dialect = CreateDialect();
        var actual = dialect.MakeParameterName("@param");
        Assert.Equal("?param", actual);
    }

    [Fact]
    public void CreateDbParameter_ThrowsForInvalidCharacters()
    {
        var dialect = CreateDialect();
        Assert.Throws<ArgumentException>(() => dialect.CreateDbParameter("@bad-name!", DbType.Int32, 1));
    }

    [Fact]
    public void CreateDbParameter_NullNameGeneratesOne()
    {
        var dialect = CreateDialect();
        var parameter = dialect.CreateDbParameter(null, DbType.Boolean, true);
        Assert.NotNull(parameter.ParameterName);
        Assert.Equal(DbType.Boolean, parameter.DbType);
    }

    [Fact]
    public void GenerateRandomName_ReusesPoolForStandardLength()
    {
        var dialect = CreateDialect();
        var first = dialect.GenerateRandomName(5, 18);
        var second = dialect.GenerateRandomName(5, 18);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GetReadOnlyConnectionString_AppendsParameter()
    {
        var dialect = new TestDialect(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger.Instance);
        var connectionString = "Data Source=test";
        var readOnly = dialect.GetReadOnlyConnectionString(connectionString);
        Assert.Contains("ApplicationIntent=ReadOnly", readOnly);
    }
}