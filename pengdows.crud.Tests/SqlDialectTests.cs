using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
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
    public void MakeParameterName_NamedSupported_UsesMarker()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        var param = ctx.CreateDbParameter("p", DbType.Int32, 1);
        var paramName = ctx.MakeParameterName(param);
        Assert.Equal(":p", paramName);
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
        public NoNamedParameterDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger) { }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Sqlite;
        public override string ParameterMarker => "@";
        public override bool SupportsNamedParameters => false;
        public override int MaxParameterLimit => 999;
        public override int ParameterNameMaxLength => 64;
        public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

        public override string GetVersionQuery() => string.Empty;
    }

}
