using System;
using System.Data;
using System.Globalization;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// An arbitrary-size integer (BigInteger) and a 128-bit integer reach a dialect as BigInteger. A provider with no
/// native integer that wide refuses the parameter: Microsoft.Data.Sqlite (SQLite) and MySqlConnector (SingleStore)
/// report "No mapping exists from object type System.Numerics.BigInteger". Such a dialect binds the exact decimal
/// text, which its column stores without loss.
/// </summary>
public class WideIntegerBindingTests
{
    private const string Wide = "123456789012345678901234567890";

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.SingleStore)]
    public void BigInteger_BindsAsItsExactText(SupportedDatabase product)
    {
        var dialect = Dialect(product);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, BigInteger.Parse(Wide, CultureInfo.InvariantCulture));

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(Wide, parameter.Value);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.SybaseASE)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.FlatFile)]
    public void WideInteger_OnADatabaseWithNoNativeWideInteger_BindsAsItsExactText(SupportedDatabase product)
    {
        // These providers have no 128-bit or arbitrary-size integer parameter: each refused the BigInteger (ORA-50028,
        // "No mapping exists", "unknown field type 242", ...). Their columns are NUMERIC/DECIMAL/NUMBER, which take the
        // exact decimal text without loss. Firebird is not here: its driver binds a BigInteger natively (TYPE-005).
        var dialect = Dialect(product);

        var big = dialect.CreateDbParameter("p", DbType.Object, BigInteger.Parse(Wide, CultureInfo.InvariantCulture));
        var wide = dialect.CreateDbParameter("q", DbType.Object, (Int128)long.MaxValue * 4);

        Assert.Equal(DbType.String, big.DbType);
        Assert.Equal(Wide, big.Value);
        Assert.Equal(((Int128)long.MaxValue * 4).ToString(CultureInfo.InvariantCulture), wide.Value);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.SingleStore)]
    public void Int128_BindsAsItsExactText(SupportedDatabase product)
    {
        var dialect = Dialect(product);
        var value = (Int128)long.MaxValue * 4;

        var parameter = dialect.CreateDbParameter("p", DbType.Object, value);

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(value.ToString(CultureInfo.InvariantCulture), parameter.Value);
    }

    [Fact]
    public void BigInteger_OnPostgreSql_BindsUnchanged()
    {
        var dialect = Dialect(SupportedDatabase.PostgreSql);
        var value = BigInteger.Parse(Wide, CultureInfo.InvariantCulture);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, value);

        Assert.Equal(value, parameter.Value);
    }

    private static SqlDialect Dialect(SupportedDatabase product) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(product, new fakeDbFactory(product), NullLogger.Instance);
}
