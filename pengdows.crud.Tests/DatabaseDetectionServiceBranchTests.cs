using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseDetectionServiceBranchTests
{
    [Fact]
    public void DetectFromConnection_UsesEmulatedProductWhenPresent()
    {
        // Use the real fakeDbConnection which has EmulatedProduct
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.MySql };

        var detected = DatabaseDetectionService.DetectFromConnection(connection);

        Assert.Equal(SupportedDatabase.MySql, detected);
    }

    [Theory]
    [InlineData("PostgreSQL 15", SupportedDatabase.PostgreSql)]
    [InlineData("Microsoft SQL Server", SupportedDatabase.SqlServer)]
    [InlineData("DuckDB", SupportedDatabase.DuckDB)]
    [InlineData("SQLite", SupportedDatabase.Sqlite)]
    public void DetectFromConnection_UsesSchemaTokens(string productName, SupportedDatabase expected)
    {
        // Use a non-"fake" named connection to test schema-based detection
        // (DatabaseDetectionService skips EmulatedProduct check for non-fake connections)
        var connection = new SchemaTestConnection(productName);

        var detected = DatabaseDetectionService.DetectFromConnection(connection);

        Assert.Equal(expected, detected);
    }

    [Fact]
    public void DetectFromConnection_UnknownWhenNoMatch()
    {
        var connection = new SchemaTestConnection("TotallyUnknownDb");

        var detected = DatabaseDetectionService.DetectFromConnection(connection);

        Assert.Equal(SupportedDatabase.Unknown, detected);
    }

    [Fact]
    public void DetectFromFactory_UsesPretendToBeWhenAvailable()
    {
        // Use the real fakeDbFactory which has PretendToBe
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);

        var detected = DatabaseDetectionService.DetectFromFactory(factory);

        Assert.Equal(SupportedDatabase.Oracle, detected);
    }

    [Theory]
    [InlineData(typeof(NpgsqlFactory), SupportedDatabase.PostgreSql)]
    [InlineData(typeof(MySqlFactory), SupportedDatabase.MySql)]
    [InlineData(typeof(MariaDbFactory), SupportedDatabase.MariaDb)]
    [InlineData(typeof(SqlServerFactory), SupportedDatabase.SqlServer)]
    [InlineData(typeof(OracleFactory), SupportedDatabase.Oracle)]
    [InlineData(typeof(FirebirdFactory), SupportedDatabase.Firebird)]
    [InlineData(typeof(DuckDbFactory), SupportedDatabase.DuckDB)]
    public void DetectFromFactory_UsesTypeNameTokens(Type factoryType, SupportedDatabase expected)
    {
        var factory = (DbProviderFactory)Activator.CreateInstance(factoryType)!;

        var detected = DatabaseDetectionService.DetectFromFactory(factory);

        Assert.Equal(expected, detected);
    }

    [Fact]
    public void DetectProduct_PrefersConnectionOverFactory()
    {
        // Use real fakeDbConnection for PostgreSQL
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        var factory = new SqlServerFactory();

        var detected = DatabaseDetectionService.DetectProduct(connection, factory);

        Assert.Equal(SupportedDatabase.PostgreSql, detected);
    }

    [Fact]
    public void DetectTopology_HandlesLocalDbAndEmbeddedFirebird()
    {
        var sqlLocal = DatabaseDetectionService.DetectTopology(
            SupportedDatabase.SqlServer,
            "Server=(localdb)\\mssqllocaldb;Database=Test;");
        Assert.True(sqlLocal.IsLocalDb);
        Assert.False(sqlLocal.IsEmbedded);

        var firebird = DatabaseDetectionService.DetectTopology(
            SupportedDatabase.Firebird,
            "ServerType=Embedded;Database=C:\\data\\test.fdb;");
        Assert.False(firebird.IsLocalDb);
        Assert.True(firebird.IsEmbedded);
    }

    /// <summary>
    /// Minimal DbConnection for testing schema-based detection.
    /// Named without "fake" so DatabaseDetectionService uses schema-based detection path.
    /// </summary>
    private sealed class SchemaTestConnection : DbConnection
    {
        private readonly string _productName;
        private ConnectionState _state = ConnectionState.Closed;

        public SchemaTestConnection(string productName)
        {
            _productName = productName;
        }

        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "TestDb";
        public override string DataSource => "TestSource";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void ChangeDatabase(string databaseName)
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotSupportedException();
        }

        public override DataTable GetSchema(string collectionName)
        {
            if (collectionName == "DataSourceInformation")
            {
                var table = new DataTable("DataSourceInformation");
                table.Columns.Add("DataSourceProductName", typeof(string));
                table.Columns.Add("DataSourceProductVersion", typeof(string));

                var row = table.NewRow();
                row["DataSourceProductName"] = _productName;
                row["DataSourceProductVersion"] = "1.0";
                table.Rows.Add(row);

                return table;
            }

            return new DataTable(collectionName);
        }
    }

    // Minimal factory stubs for type name detection testing
    private sealed class NpgsqlFactory : DbProviderFactory
    {
    }

    private sealed class MySqlFactory : DbProviderFactory
    {
    }

    private sealed class MariaDbFactory : DbProviderFactory
    {
    }

    private sealed class SqlServerFactory : DbProviderFactory
    {
    }

    private sealed class OracleFactory : DbProviderFactory
    {
    }

    private sealed class FirebirdFactory : DbProviderFactory
    {
    }

    private sealed class DuckDbFactory : DbProviderFactory
    {
    }
}