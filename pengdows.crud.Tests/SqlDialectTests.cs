using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectTests
{
    [Fact]
    public void WrapObjectName_QuotesIdentifier()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        var wrapped = ctx.WrapObjectName("schema.table");
        Assert.Equal("\"schema\".\"table\"", wrapped);
    }

    [Fact]
    public void WrapObjectName_NullOrEmpty_ReturnsEmpty()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        Assert.Equal(string.Empty, ctx.WrapObjectName(null!));
        Assert.Equal(string.Empty, ctx.WrapObjectName(string.Empty));
    }

    [Fact]
    public void WrapObjectName_ManyDistinctIdentifiers_CacheDoesNotGrowUnbounded()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);

        for (var i = 0; i < 5000; i++)
        {
            ctx.WrapObjectName($"distinct_identifier_{i}");
        }

        var count = GetWrappedNameCacheCount(ctx.Dialect);
        Assert.True(count <= 1024,
            $"_wrappedNameCache grew to {count} entries after 5000 distinct identifiers — it must be bounded.");
    }

    private static int GetWrappedNameCacheCount(object dialect)
    {
        var field = typeof(SqlDialect).GetField("_wrappedNameCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cache = field.GetValue(dialect)!;
        var mapField = cache.GetType().GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var map = (System.Collections.ICollection)mapField.GetValue(cache)!;
        return map.Count;
    }

    [Fact]
    public void MakeParameterName_NamedSupported_UsesMarker()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        var param = ctx.CreateDbParameter("p", DbType.Int32, 1);
        var paramName = ctx.MakeParameterName(param);
        Assert.Equal("@p", paramName);
    }

    [Fact]
    public void MakeParameterName_NoNamedParameters_ReturnsQuestion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = NullLoggerFactory.Instance.CreateLogger<SqlDialect>();
        var dialect = new NoNamedParameterDialect(factory, logger);
        var param = new fakeDbParameter { ParameterName = "p", DbType = DbType.Int32, Value = 1 };
        Assert.Equal("?", dialect.MakeParameterName(param));
    }

    [Fact]
    public void CreateDbParameter_SetsProperties()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var schema = DataSourceInformation.BuildEmptySchema("SQLite", "1", "?", "?", 64, "\\w+", "\\w+", true);
        var conn = (fakeDbConnection)factory.CreateConnection();
        var tracked = new FakeTrackedConnection(conn, schema, new Dictionary<string, object>());
        var info = DataSourceInformation.Create(tracked, factory);
        var dialect = SqlDialectFactory.CreateDialect(tracked, factory);
        var p = dialect.CreateDbParameter("p", DbType.Int32, 1);
        Assert.Equal("p", p.ParameterName);
        Assert.Equal(DbType.Int32, p.DbType);
        Assert.Equal(1, p.Value);
    }

    private sealed class NoNamedParameterDialect : SqlDialect
    {
        public NoNamedParameterDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Sqlite;
        public override string ParameterMarker => "@";
        public override bool SupportsNamedParameters => false;
        public override int MaxParameterLimit => 999;
        public override int ParameterNameMaxLength => 64;
        public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

        public override string GetVersionQuery()
        {
            return string.Empty;
        }
    }
}
