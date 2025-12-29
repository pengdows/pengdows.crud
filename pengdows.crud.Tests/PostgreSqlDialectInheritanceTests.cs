using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class PostgreSqlDialectInheritanceTests
{
    [Fact]
    public void PostgreSqlDialect_Inherits_From_SqlDialect()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance);
        Assert.IsAssignableFrom<SqlDialect>(dialect);
    }

    [Fact]
    public void PostgreSqlDialect_Does_Not_Inherit_From_Sql92Dialect()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance);
        Assert.False(typeof(Sql92Dialect).IsAssignableFrom(dialect.GetType()));
    }
}
