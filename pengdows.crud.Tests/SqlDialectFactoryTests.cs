using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.dialects;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectFactoryTests
{
    [Fact]
    public void CreateDialectForType_SqlServer_ReturnsSqlServerDialect()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.SqlServer,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<SqlServerDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_Firebird_ThrowsNotImplementedException()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Firebird);
        Assert.Throws<NotImplementedException>(() =>
            SqlDialectFactory.CreateDialectForType(
                SupportedDatabase.Firebird,
                factory,
                    NullLogger<SqlDialect>.Instance));
    }
}
