using System.Data.Common;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ConnectionStringHelperTests
{
    [Fact]
    public void Create_WithNullFactory_FallsBackToRawDataSource()
    {
        var builder = ConnectionStringHelper.Create((DbProviderFactory)null!, "Data Source=foo");
        Assert.Equal("foo", builder["Data Source"]);
    }

    [Fact]
    public void Create_WithBuilderThatThrows_UsesFallBackBuilder()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite)
        {
            ConnectionStringBuilderBehavior = ConnectionStringBuilderBehavior.ThrowOnConnectionStringSet
        };
        var result = ConnectionStringHelper.Create(factory, "Data Source=bar");

        Assert.Equal("Data Source=bar", result["Data Source"]);
    }

    [Fact]
    public void Create_WithBuilderThatThrowsAndMalformedInput_UsesRawDataSource()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite)
        {
            ConnectionStringBuilderBehavior = ConnectionStringBuilderBehavior.ThrowOnConnectionStringSet
        };
        var result = ConnectionStringHelper.Create(factory, "just-a-string");

        Assert.Equal("just-a-string", result["Data Source"]);
    }

    [Fact]
    public void Create_WithFactoryReturningNullBuilder_ParsesNormally()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite)
        {
            ConnectionStringBuilderBehavior = ConnectionStringBuilderBehavior.ReturnNull
        };

        var result = ConnectionStringHelper.Create(factory, "Data Source=foo");

        Assert.Equal("foo", result["Data Source"]);
    }
}
