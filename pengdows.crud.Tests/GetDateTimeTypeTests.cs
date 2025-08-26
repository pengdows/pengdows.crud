using System.Reflection;
using pengdows.crud.enums;
using testbed;
using Xunit;

namespace pengdows.crud.Tests;
public class GetDateTimeTypeTests
{
    [Fact]
    public void ReturnsTimestampWithTimeZone_ForPostgreSql()
    {
        var method = typeof(TestProvider).GetMethod("GetDateTimeType", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = (string)method!.Invoke(null, new object[] { SupportedDatabase.PostgreSql })!;
        Assert.Equal("TIMESTAMP WITH TIME ZONE", result);
    }

    [Fact]
    public void ReturnsDatetime_ForNonPostgres()
    {
        var method = typeof(TestProvider).GetMethod("GetDateTimeType", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = (string)method!.Invoke(null, new object[] { SupportedDatabase.Sqlite })!;
        Assert.Equal("DATETIME", result);
    }
}
