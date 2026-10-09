using System.Data.Common;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Whether two connection strings reach the same server is decided from the endpoint they name, not
/// from string equality: the reader and writer variants of one context already differ (application
/// name suffix, pool settings), and a read replica must NOT share a budget with its primary.
/// </summary>
public sealed class ServerEndpointTests
{
    private static string? Key(string connectionString, int? defaultPort = null) =>
        ServerEndpoint.TryGetKey(new DbConnectionStringBuilder { ConnectionString = connectionString }, defaultPort, out var key)
            ? key
            : null;

    [Fact]
    public void SameHostAndPort_DifferingOnlyInApplicationNameAndPoolSettings_ShareAKey()
    {
        var writer = Key("Host=db1;Port=5432;Database=a;Username=u;Password=p;Application Name=app-rw;Maximum Pool Size=20");
        var reader = Key("Host=db1;Port=5432;Database=a;Username=u;Password=p;Application Name=app;Maximum Pool Size=10");

        Assert.NotNull(writer);
        Assert.Equal(writer, reader);
    }

    [Fact]
    public void DifferentDatabasesOnTheSameServer_ShareAKey_BecauseTheServerLimitIsServerWide()
    {
        Assert.Equal(Key("Host=db1;Database=a"), Key("Host=db1;Database=b"));
    }

    [Fact]
    public void DifferentHosts_DoNotShareAKey()
    {
        Assert.NotEqual(Key("Host=primary;Port=5432"), Key("Host=replica;Port=5432"));
    }

    [Fact]
    public void DifferentPorts_DoNotShareAKey()
    {
        Assert.NotEqual(Key("Host=db1;Port=5432"), Key("Host=db1;Port=5433"));
    }

    [Theory]
    [InlineData("Host=localhost")]
    [InlineData("Host=127.0.0.1")]
    [InlineData("Host=LOCALHOST")]
    public void LoopbackSpellings_ShareAKey(string connectionString)
    {
        Assert.Equal(Key("Host=localhost"), Key(connectionString));
    }

    [Fact]
    public void AnOmittedPort_EqualsTheDialectsDefaultPort()
    {
        Assert.Equal(Key("Host=db1;Port=5432", defaultPort: 5432), Key("Host=db1", defaultPort: 5432));
    }

    [Fact]
    public void SqlServerStyle_TcpPrefixAndCommaPort_AreParsed()
    {
        Assert.Equal(Key("Server=db1,1433;Database=a", defaultPort: 1433), Key("Data Source=tcp:db1,1433;Initial Catalog=b", defaultPort: 1433));
    }

    [Fact]
    public void SqlServerNamedInstances_AreDifferentServers()
    {
        Assert.NotEqual(Key(@"Data Source=db1\SQLEXPRESS"), Key(@"Data Source=db1\OTHER"));
    }

    [Fact]
    public void NoEndpointInTheString_HasNoKey()
    {
        Assert.Null(Key("Database=a;Username=u"));
    }
}
