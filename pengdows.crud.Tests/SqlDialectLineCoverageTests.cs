using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectLineCoverageTests
{
    private sealed class TestDialect : SqlDialect
    {
        public TestDialect() : base(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger.Instance)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Sqlite;
    }

    private sealed class FallbackDialect : SqlDialect
    {
        public FallbackDialect() : base(new fakeDbFactory(SupportedDatabase.Unknown), NullLogger.Instance)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
    }

    [Fact]
    public void FeatureFlags_AreAccessibleAfterInitialize()
    {
        var dialect = new TestDialect();
        dialect.InitializeUnknownProductInfo();

        Assert.False(dialect.SupportsWindowFunctions);
        Assert.False(dialect.SupportsMultidimensionalArrays);
        Assert.False(dialect.SupportsCommonTableExpressions);
        Assert.Equal(string.Empty, dialect.GetCompatibilityWarning());
    }

    [Fact]
    public void CompatibilityWarning_ReturnsForFallbackDialect()
    {
        var dialect = new FallbackDialect();
        dialect.InitializeUnknownProductInfo();

        Assert.Contains("fallback", dialect.GetCompatibilityWarning());
    }
}