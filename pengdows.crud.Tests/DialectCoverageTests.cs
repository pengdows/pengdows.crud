using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectCoverageTests
{
    [Fact]
    public void MySqlDialect_WrapObjectName_WrapsWithBackticks()
    {
        var dialect = new MySqlDialect(
            new FakeDbFactory(SupportedDatabase.MySql),
            NullLogger<MySqlDialect>.Instance);
        var wrapped = dialect.WrapObjectName("schema.table");
        Assert.Equal("`schema`.`table`", wrapped);
    }

    [Fact]
    public void MySqlDialect_WrapObjectName_Null_ReturnsEmpty()
    {
        var dialect = new MySqlDialect(
            new FakeDbFactory(SupportedDatabase.MySql),
            NullLogger<MySqlDialect>.Instance);
        var wrapped = dialect.WrapObjectName(null);
        Assert.Equal(string.Empty, wrapped);
    }

    [Fact]
    public void OracleDialect_MakeParameterName_PrefixesWithColon()
    {
        var dialect = new OracleDialect(
            new FakeDbFactory(SupportedDatabase.Oracle),
            NullLogger<OracleDialect>.Instance);
        var name = dialect.MakeParameterName("p1");
        Assert.Equal(":p1", name);
    }

    [Fact]
    public void OracleDialect_WrapObjectName_Null_ReturnsEmpty()
    {
        var dialect = new OracleDialect(
            new FakeDbFactory(SupportedDatabase.Oracle),
            NullLogger<OracleDialect>.Instance);
        var wrapped = dialect.WrapObjectName(null);
        Assert.Equal(string.Empty, wrapped);
    }
}
