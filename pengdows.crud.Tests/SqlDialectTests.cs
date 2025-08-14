using System.Collections.Generic;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectTests
{
    [Fact]
    public void WrapObjectName_QuotesIdentifier()
    {
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        var wrapped = ctx.WrapObjectName("schema.table");
        Assert.Equal("\"schema\".\"table\"", wrapped);
    }

    [Fact]
    public void WrapObjectName_NullOrEmpty_ReturnsEmpty()
    {
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        Assert.Equal(string.Empty, ctx.WrapObjectName(null));
        Assert.Equal(string.Empty, ctx.WrapObjectName(string.Empty));
    }

    [Fact]
    public void MakeParameterName_NamedSupported_UsesMarker()
    {
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        var param = ctx.CreateDbParameter("p", DbType.Int32, 1);
        Assert.Equal(":p", ctx.MakeParameterName(param));
    }

    [Fact]
    public void MakeParameterName_NoNamedParameters_ReturnsQuestion()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var schema = DataSourceInformation.BuildEmptySchema("SQLite", "1", "?", "?", 64, "\\w+", "\\w+", false);
        var conn = (FakeDbConnection)factory.CreateConnection();
        var tracked = new FakeTrackedConnection(conn, schema, new Dictionary<string, object>());
        var info = DataSourceInformation.Create(tracked);
        var dialect = new SqlDialect(info, factory, (length, max) => "p");
        var param = new FakeDbParameter { ParameterName = "p", DbType = DbType.Int32, Value = 1 };
        Assert.Equal("?", dialect.MakeParameterName(param));
    }

    [Fact]
    public void CreateDbParameter_SetsProperties()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var info =new DataSourceInformation( DataSourceInformation.BuildEmptySchema("SQLite", "1", "?", "?", 64, "\\w+", "\\w+", true));
        var dialect = new SqlDialect(info, factory, (lenth, max) => "p");
        var p = dialect.CreateDbParameter("p", DbType.Int32, 1);
        Assert.Equal("p", p.ParameterName);
        Assert.Equal(DbType.Int32, p.DbType);
        Assert.Equal(1, p.Value);
    }
}
