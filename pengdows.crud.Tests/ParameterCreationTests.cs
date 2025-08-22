using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ParameterCreationTests
{
    private static SqlDialect CreateDialect()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        return new SqlServerDialect(factory, NullLogger.Instance);
    }

    [Fact]
    public void CreateDbParameter_WithDecimal_SetsPrecisionAndScale()
    {
        var dialect = CreateDialect();
        var p = dialect.CreateDbParameter(null, DbType.Decimal, 123.45m);
        Assert.Equal((byte)5, p.Precision);
        Assert.Equal((byte)2, p.Scale);
    }

    [Fact]
    public void CreateDbParameter_WithNullDecimal_DoesNotSetPrecision()
    {
        var dialect = CreateDialect();
        var p = dialect.CreateDbParameter(null, DbType.Decimal, (decimal?)null);
        Assert.Equal((byte)0, p.Precision);
        Assert.Equal((byte)0, p.Scale);
    }
}
