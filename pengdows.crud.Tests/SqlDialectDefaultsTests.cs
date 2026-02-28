using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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

        public override string GetVersionQuery()
        {
            return string.Empty;
        }
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

        public override string GetVersionQuery()
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// A named-parameter dialect that opts in to common type conversions via the new virtual hook.
    /// Used to verify that NeedsCommonConversions can be overridden independently of SupportsNamedParameters.
    /// </summary>
    private sealed class ForcedConversionsDialect : SqlDialect
    {
        public ForcedConversionsDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public override bool SupportsNamedParameters => true; // named params ...
        protected override bool NeedsCommonConversions => true; // ... but still needs conversions

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

        Assert.Equal(2000, dialect.MaxParameterLimit);
        Assert.NotEqual(2001, dialect.MaxParameterLimit);

        Assert.Equal(128, dialect.ParameterNameMaxLength);
        Assert.NotEqual(129, dialect.ParameterNameMaxLength);

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

    /// <summary>
    /// Verifies that GenerateParameterName produces collision-free names beyond the old 10000-name
    /// wrapping boundary caused by (index % 10000).
    /// </summary>
    [Fact]
    public void GenerateParameterName_IsCollisionFreeAcrossWrappingBoundary()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = NullLoggerFactory.Instance.CreateLogger<SqlDialect>();
        var dialect = new DefaultDialect(factory, logger);

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 20_000; i++)
        {
            var name = dialect.GenerateParameterName();
            Assert.True(names.Add(name), $"Duplicate parameter name generated at iteration {i}: '{name}'");
        }
    }

    /// <summary>
    /// Verifies that parameter names starting with a digit are rejected, aligning
    /// CreateDbParameter validation with the published ParameterNamePattern regex.
    /// </summary>
    [Fact]
    public void CreateDbParameter_WithLeadingDigitName_ThrowsArgumentException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = NullLoggerFactory.Instance.CreateLogger<SqlDialect>();
        var dialect = new DefaultDialect(factory, logger);

        Assert.Throws<ArgumentException>(() => dialect.CreateDbParameter("1invalid", DbType.String, "v"));
    }

    /// <summary>
    /// Verifies that parameter names starting with an underscore are rejected, aligning
    /// CreateDbParameter validation with the published ParameterNamePattern regex (^[a-zA-Z]...).
    /// </summary>
    [Fact]
    public void CreateDbParameter_WithLeadingUnderscoreName_ThrowsArgumentException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = NullLoggerFactory.Instance.CreateLogger<SqlDialect>();
        var dialect = new DefaultDialect(factory, logger);

        Assert.Throws<ArgumentException>(() => dialect.CreateDbParameter("_invalid", DbType.String, "v"));
    }

    /// <summary>
    /// Verifies that a dialect can opt in to common type conversions via NeedsCommonConversions
    /// even when SupportsNamedParameters is true. This tests the new virtual hook that
    /// decouples "clear parameter name for positional provider" from "apply type conversions".
    /// </summary>
    [Fact]
    public void NeedsCommonConversions_WhenOverriddenTrue_AppliesGuidConversionForNamedProvider()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = NullLoggerFactory.Instance.CreateLogger<SqlDialect>();
        var dialect = new ForcedConversionsDialect(factory, logger);

        var guid = Guid.NewGuid();
        var param = dialect.CreateDbParameter("testparam", DbType.Guid, guid);

        // With NeedsCommonConversions => true, the GUID should be converted to its string form
        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(guid.ToString("D"), param.Value);
    }
}
