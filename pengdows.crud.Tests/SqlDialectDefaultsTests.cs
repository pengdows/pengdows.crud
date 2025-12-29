using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectDefaultsTests
{
    private sealed class DefaultDialect : SqlDialect
    {
        public DefaultDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public override string GetVersionQuery() => string.Empty;
    }

    private sealed class OverrideDialect : SqlDialect
    {
        public OverrideDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.SqlServer;
        public override string ParameterMarker => "@";
        public override bool SupportsNamedParameters => true;
        public override int MaxParameterLimit => 10;
        public override int ParameterNameMaxLength => 64;
        public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;
        public override bool SupportsNamespaces => true;
        public override string GetVersionQuery() => string.Empty;
    }

    [Fact]
    public void Defaults_are_sql92()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = NullLoggerFactory.Instance.CreateLogger<SqlDialect>();
        var dialect = new DefaultDialect(factory, logger);

        Assert.Equal("?", dialect.ParameterMarker);
        Assert.NotEqual("@", dialect.ParameterMarker);

        Assert.True(dialect.SupportsNamedParameters);
        Assert.True(dialect.SupportsNamedParameters);

        Assert.Equal(255, dialect.MaxParameterLimit);
        Assert.NotEqual(256, dialect.MaxParameterLimit);

        Assert.Equal(18, dialect.ParameterNameMaxLength);
        Assert.NotEqual(19, dialect.ParameterNameMaxLength);

        Assert.Equal(ProcWrappingStyle.None, dialect.ProcWrappingStyle);
        Assert.NotEqual(ProcWrappingStyle.Call, dialect.ProcWrappingStyle);

        Assert.False(dialect.SupportsNamespaces);
        Assert.False(dialect.SupportsNamespaces);
    }

    [Fact]
    public void Overrides_replace_sql92_defaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = NullLoggerFactory.Instance.CreateLogger<SqlDialect>();
        var dialect = new OverrideDialect(factory, logger);

        Assert.Equal("@", dialect.ParameterMarker);
        Assert.NotEqual("?", dialect.ParameterMarker);

        Assert.True(dialect.SupportsNamedParameters);
        Assert.True(dialect.SupportsNamedParameters);

        Assert.Equal(10, dialect.MaxParameterLimit);
        Assert.NotEqual(255, dialect.MaxParameterLimit);

        Assert.Equal(64, dialect.ParameterNameMaxLength);
        Assert.NotEqual(18, dialect.ParameterNameMaxLength);

        Assert.Equal(ProcWrappingStyle.Call, dialect.ProcWrappingStyle);
        Assert.NotEqual(ProcWrappingStyle.None, dialect.ProcWrappingStyle);

        Assert.True(dialect.SupportsNamespaces);
        Assert.True(dialect.SupportsNamespaces);
    }
}
