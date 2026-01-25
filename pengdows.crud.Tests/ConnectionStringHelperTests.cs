using System;
using System.Data.Common;
using pengdows.crud.@internal;
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
        var builder = new ThrowingConnectionStringBuilder();
        var result = ConnectionStringHelper.Create(builder, "Data Source=bar");

        Assert.Equal("Data Source=bar", result["Data Source"]);
    }

    [Fact]
    public void Create_WithBuilderThatThrowsAndMalformedInput_UsesRawDataSource()
    {
        var builder = new ThrowingConnectionStringBuilder();
        var result = ConnectionStringHelper.Create(builder, "just-a-string");

        Assert.Equal("just-a-string", result["Data Source"]);
    }

    #nullable disable
    private sealed class ThrowingConnectionStringBuilder : DbConnectionStringBuilder
    {
        public override object this[string keyword]
        {
            get => base[keyword];
            set => throw new InvalidOperationException("boom");
        }
    }
    #nullable restore
}
