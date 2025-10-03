using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class fakeDbConnectionTests
{
    [Fact]
    public void GetSchema_UnknownProduct_ReturnsDefaultSchema()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}";
        conn.Open();
        var schema = conn.GetSchema();
        Assert.True(schema.Rows[0].Field<bool>("SupportsNamedParameters"));
        Assert.NotEqual(false, schema.Rows[0].Field<bool>("SupportsNamedParameters"));
    }

    [Fact]
    public void GetSchema_EmulatedProductNotConfigured_Throws()
    {
        var conn = new fakeDbConnection();
        Assert.Throws<InvalidOperationException>(() => conn.GetSchema());
    }

    [Fact]
    public void GetSchema_EmulatedProductConfiguredWithoutOpen_ReturnsSchema()
    {
        var conn = new fakeDbConnection
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}"
        };

        var schema = conn.GetSchema();

        Assert.Equal("SQLite", schema.Rows[0].Field<string>("DataSourceProductName"));
    }

    [Fact]
    public void GetSchema_WithCollectionName_EmulatedProductConfiguredWithoutOpen_ReturnsSchema()
    {
        var conn = new fakeDbConnection
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}"
        };

        var schema = conn.GetSchema("DataSourceInformation");

        Assert.Equal("Microsoft SQL Server", schema.Rows[0].Field<string>("DataSourceProductName"));
    }

    [Fact]
    public void GetSchema_WithCollectionName_EmulatedProductNotConfigured_Throws()
    {
        var conn = new fakeDbConnection();

        Assert.Throws<InvalidOperationException>(() => conn.GetSchema("DataSourceInformation"));
    }

    [Fact]
    public void Interface_RemainingQueuesReflectQueuedData()
    {
        IFakeDbConnection conn = new fakeDbConnection
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}"
        };

        conn.EnqueueScalarResult(11);
        conn.EnqueueNonQueryResult(1);
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object>
            {
                { "id", 5 }
            }
        });

        Assert.Single(conn.RemainingScalarResults);
        Assert.Single(conn.RemainingNonQueryResults);
        Assert.Single(conn.RemainingReaderResults);
    }

    [Fact]
    public void Interface_SetFailOnOpen_ThrowsWhenOpening()
    {
        IFakeDbConnection conn = new fakeDbConnection
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}"
        };

        conn.SetFailOnOpen();

        Assert.Throws<InvalidOperationException>(() => conn.Open());
    }

    [Fact]
    public void Interface_GetSchemaWithoutEmulatedProduct_Throws()
    {
        IFakeDbConnection conn = new fakeDbConnection();

        Assert.Throws<InvalidOperationException>(() => conn.GetSchema());
    }

    [Fact]
    public void FactoryInterface_CreateConnection_ConfiguresEmulatedProduct()
    {
        IFakeDbFactory factory = new fakeDbFactory(SupportedDatabase.MySql);

        var conn = factory.CreateConnection();

        Assert.Equal(SupportedDatabase.MySql, conn.EmulatedProduct);
    }

    [Fact]
    public void FactoryInterface_CreateConnectionWithFailure_FailsOnOpen()
    {
        IFakeDbFactory factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.Sqlite,
            ConnectionFailureMode.FailOnOpen);

        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";

        Assert.Throws<InvalidOperationException>(() => conn.Open());
    }
}
