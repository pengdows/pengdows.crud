using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
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
    }
}
