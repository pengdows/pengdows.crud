using System;
using System.Data;
using System.Globalization;
using System.Numerics;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// The Firebird .NET driver has no DateTime2 parameter (a DbType 26 is refused) and cannot send a BigInteger to a NUMERIC
/// column (its decimal conversion refuses it), so a DateTime2 is sent as DateTime and a wide integer as its exact text.
/// </summary>
public class FirebirdParameterTests
{
    private static SqlDialect Dialect() =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.Firebird,
            new fakeDbFactory(SupportedDatabase.Firebird), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

    [Fact]
    public void ADateTime2IsSentAsDateTime()
    {
        var parameter = Dialect().CreateDbParameter("p", DbType.DateTime2, new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc));

        Assert.Equal(DbType.DateTime, parameter.DbType);
    }

    [Fact]
    public void ABigIntegerIsSentAsItsExactText()
    {
        var wide = BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture);

        var parameter = Dialect().CreateDbParameter("p", DbType.Object, wide);

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("123456789012345678901234567890", parameter.Value);
    }
}
