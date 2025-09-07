#region

using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlDialectUtilityTests
{
    [Fact]
    public void TryParseMajorVersion_ValidVersionString_ReturnsTrue()
    {
        var method = typeof(SqlDialect).GetMethod("TryParseMajorVersion",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var parameters = new object[] { "14.2.1", 0 };
        var result = (bool)method!.Invoke(null, parameters)!;
        var majorVersion = (int)parameters[1];

        Assert.True(result);
        Assert.Equal(14, majorVersion);
    }

    [Fact]
    public void TryParseMajorVersion_InvalidVersionString_ReturnsFalse()
    {
        var method = typeof(SqlDialect).GetMethod("TryParseMajorVersion",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var parameters1 = new object[] { "invalid", 0 };
        var result1 = (bool)method!.Invoke(null, parameters1)!;

        var parameters2 = new object[] { "", 0 };
        var result2 = (bool)method.Invoke(null, parameters2)!;

        Assert.False(result1);
        Assert.False(result2);
    }

    [Fact]
    public void GetPrime_ReturnsValidPrimes()
    {
        var method = typeof(SqlDialect).GetMethod("GetPrime",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var prime1 = (int)method!.Invoke(null, new object[] { 1 })!;
        var prime2 = (int)method.Invoke(null, new object[] { 10 })!;
        var prime3 = (int)method.Invoke(null, new object[] { 100 })!;

        Assert.True(prime1 >= 1);
        Assert.True(prime2 >= 10);
        Assert.True(prime3 >= 100);

        Assert.True(IsPrime(prime1));
        Assert.True(IsPrime(prime2));
        Assert.True(IsPrime(prime3));
    }

    [Fact]
    public void ExtractProductNameFromVersion_CallsImplementation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);

        var method = dialect.GetType().GetMethod(
            "ExtractProductNameFromVersion",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (string)method!.Invoke(dialect, new object[] { "any version" })!;

        Assert.Equal("Firebird", result);
    }

    [Fact]
    public void DetermineStandardCompliance_CallsImplementation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);

        var method = dialect.GetType().GetMethod(
            "DetermineStandardCompliance",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (SqlStandardLevel)method!.Invoke(dialect, new object?[] { null })!;

        Assert.Equal(SqlStandardLevel.Sql92, result);
    }

    private static bool IsPrime(int number)
    {
        if (number < 2) return false;
        for (int i = 2; i * i <= number; i++)
        {
            if (number % i == 0) return false;
        }
        return true;
    }
}
