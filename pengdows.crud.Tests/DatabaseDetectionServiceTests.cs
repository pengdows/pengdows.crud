using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseDetectionServiceTests
{
    #region Detection from Factory Type

    [Fact]
    public void DetectFromFactory_SqlServer_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var result = DatabaseDetectionService.DetectFromFactory(factory);
        Assert.Equal(SupportedDatabase.SqlServer, result);
    }

    [Fact]
    public void DetectFromFactory_PostgreSql_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var result = DatabaseDetectionService.DetectFromFactory(factory);
        Assert.Equal(SupportedDatabase.PostgreSql, result);
    }

    [Fact]
    public void DetectFromFactory_MySql_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var result = DatabaseDetectionService.DetectFromFactory(factory);
        Assert.Equal(SupportedDatabase.MySql, result);
    }

    [Fact]
    public void DetectFromFactory_MariaDb_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var result = DatabaseDetectionService.DetectFromFactory(factory);
        Assert.Equal(SupportedDatabase.MariaDb, result);
    }

    [Fact]
    public void DetectFromFactory_Oracle_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var result = DatabaseDetectionService.DetectFromFactory(factory);
        Assert.Equal(SupportedDatabase.Oracle, result);
    }

    [Fact]
    public void DetectFromFactory_Sqlite_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var result = DatabaseDetectionService.DetectFromFactory(factory);
        Assert.Equal(SupportedDatabase.Sqlite, result);
    }

    [Fact]
    public void DetectFromFactory_Firebird_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var result = DatabaseDetectionService.DetectFromFactory(factory);
        Assert.Equal(SupportedDatabase.Firebird, result);
    }

    [Fact]
    public void DetectFromFactory_CockroachDb_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.CockroachDb);
        var result = DatabaseDetectionService.DetectFromFactory(factory);
        Assert.Equal(SupportedDatabase.CockroachDb, result);
    }

    [Fact]
    public void DetectFromFactory_DuckDB_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var result = DatabaseDetectionService.DetectFromFactory(factory);
        Assert.Equal(SupportedDatabase.DuckDB, result);
    }

    [Fact]
    public void DetectFromFactory_Null_ReturnsUnknown()
    {
        var result = DatabaseDetectionService.DetectFromFactory(null);
        Assert.Equal(SupportedDatabase.Unknown, result);
    }

    #endregion

    #region Detection from Connection

    [Fact]
    public void DetectFromConnection_SqlServer_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var connection = factory.CreateConnection();
        var result = DatabaseDetectionService.DetectFromConnection(connection);
        Assert.Equal(SupportedDatabase.SqlServer, result);
    }

    [Fact]
    public void DetectFromConnection_PostgreSql_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var connection = factory.CreateConnection();
        var result = DatabaseDetectionService.DetectFromConnection(connection);
        Assert.Equal(SupportedDatabase.PostgreSql, result);
    }

    [Fact]
    public void DetectFromConnection_MySql_ReturnsCorrectProduct()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        using var connection = factory.CreateConnection();
        var result = DatabaseDetectionService.DetectFromConnection(connection);
        Assert.Equal(SupportedDatabase.MySql, result);
    }

    [Fact]
    public void DetectFromConnection_Null_ReturnsUnknown()
    {
        var result = DatabaseDetectionService.DetectFromConnection(null);
        Assert.Equal(SupportedDatabase.Unknown, result);
    }

    #endregion

    #region Topology Detection

    [Fact]
    public void DetectTopology_SqlServerLocalDb_DetectsLocalDb()
    {
        var connectionString = "Server=(localdb)\\mssqllocaldb;Database=test";
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.SqlServer, connectionString);
        Assert.True(topology.IsLocalDb);
        Assert.False(topology.IsEmbedded);
    }

    [Fact]
    public void DetectTopology_SqlServerLocalDb_AlternateFormat_DetectsLocalDb()
    {
        var connectionString = "Server=localdb;Database=test";
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.SqlServer, connectionString);
        Assert.True(topology.IsLocalDb);
    }

    [Fact]
    public void DetectTopology_SqlServerRegular_NotLocalDb()
    {
        var connectionString = "Server=localhost;Database=test";
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.SqlServer, connectionString);
        Assert.False(topology.IsLocalDb);
        Assert.False(topology.IsEmbedded);
    }

    [Fact]
    public void DetectTopology_FirebirdEmbedded_ServerType_DetectsEmbedded()
    {
        var connectionString = "Database=test.fdb;ServerType=Embedded";
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.Firebird, connectionString);
        Assert.True(topology.IsEmbedded);
        Assert.False(topology.IsLocalDb);
    }

    [Fact]
    public void DetectTopology_FirebirdEmbedded_ClientLibrary_DetectsEmbedded()
    {
        var connectionString = "Database=test.fdb;ClientLibrary=fbembed.dll";
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.Firebird, connectionString);
        Assert.True(topology.IsEmbedded);
    }

    [Fact]
    public void DetectTopology_FirebirdEmbedded_FilePathHeuristic_DetectsEmbedded()
    {
        var connectionString = "Database=C:\\data\\test.fdb";
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.Firebird, connectionString);
        Assert.True(topology.IsEmbedded);
    }

    [Fact]
    public void DetectTopology_FirebirdServer_NotEmbedded()
    {
        var connectionString = "DataSource=localhost;Database=test";
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.Firebird, connectionString);
        Assert.False(topology.IsEmbedded);
    }

    [Fact]
    public void DetectTopology_PostgreSql_NoSpecialTopology()
    {
        var connectionString = "Host=localhost;Database=test";
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.PostgreSql, connectionString);
        Assert.False(topology.IsLocalDb);
        Assert.False(topology.IsEmbedded);
    }

    [Fact]
    public void DetectTopology_NullConnectionString_NoSpecialTopology()
    {
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.SqlServer, null);
        Assert.False(topology.IsLocalDb);
        Assert.False(topology.IsEmbedded);
    }

    #endregion

    #region Integrated Detection

    [Fact]
    public void DetectProduct_FromConnection_PreferredOverFactory()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var connection = factory.CreateConnection();

        // Connection should take precedence
        var result = DatabaseDetectionService.DetectProduct(connection, factory);
        Assert.Equal(SupportedDatabase.SqlServer, result);
    }

    [Fact]
    public void DetectProduct_NullConnection_FallsBackToFactory()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var result = DatabaseDetectionService.DetectProduct(null, factory);
        Assert.Equal(SupportedDatabase.PostgreSql, result);
    }

    [Fact]
    public void DetectProduct_BothNull_ReturnsUnknown()
    {
        var result = DatabaseDetectionService.DetectProduct(null, null);
        Assert.Equal(SupportedDatabase.Unknown, result);
    }

    #endregion
}